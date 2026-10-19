# Issue #221: ドキュメントオープン時の詳細ビュー初期描画パイプラインの二重起動・ロック競合の解消 実装計画

## 1. 概要・背景
PDFドキュメントを開いた（またはセッションを切り替えた）際、`MainViewModel` と `DetailEditorViewModel` の双方から複数回にわたってフィット倍率計算（`ApplyFitMode`）および描画スケジュールが重複起動され、初期描画タスク（`_renderCts`）と250ms後の統合パイプライン（`_thumbnailCts`）が `_renderLock`（`PdfiumRenderer` 内の単一 `SemaphoreSlim(1, 1)`）を競合する問題が発生しています。

本改修では、初期化処理の流れを整流化し、`ApplyFitMode` の重複呼び出しを排除するとともに、初期描画およびサムネイル生成を単一の統合パイプライン（`RunDetailViewPipelineAsync`）へ完全に一本化します。

---

## 2. 現状の課題と原因詳細

1. **`ApplyFitMode()` の多重呼び出し**:
   - `MainViewModel.OnActiveSessionChanged` において、`Document = newValue.Document` を代入する前に `DetailEditor.FitMode = newValue.FitMode` をセットしているため、直前の古いドキュメント（または初期空ドキュメント）に対して不要な `ApplyFitMode()` が実行される。
   - `Document = newValue.Document` のプロパティ変更通知 `OnDocumentChanged` 経由で `DetailEditor.InitializeDocument` が呼ばれ、内部で2回目の `ApplyFitMode()` が実行される。
   - `OnActiveSessionChanged` に戻った後、L662 で条件分岐により3回目の `DetailEditor.ApplyFitMode()` が呼び出されている。

2. **初期描画タスクと統合パイプラインの二重起動・ロック競合**:
   - `InitializeDocument` 内で `_ = LoadInitialDocumentBackgroundAsync()` が呼ばれ、`isInitialLoad: true` で `ScheduleDynamicRender` が実行される。
   - `DetailEditorViewModel.PerformDynamicRenderAsync` では `if (PipelineExecutionHandler != null && !isInitialLoad)` となっており、`isInitialLoad: true` の場合は `PipelineExecutionHandler`（統合パイプライン）をバイパスして `_renderCts` による直接描画（Step 1: 主ページ描画 ＋ Step 3: 隣接ページ描画）を開始する。
   - 同時に、`OnActiveSessionChanged` の末尾（L676）で `_ = ScheduleInitialThumbnailsAsync();`（250msデバウンス）が呼ばれ、250ms後に `_thumbnailCts` による統合パイプライン（Step 1〜Step 4）が起動される。
   - 結果として、`_renderCts` が隣接ページのフル解像度描画や文字・リンク抽出で `_renderLock` を占有している最中に、`_thumbnailCts` が Step 1（主ページ）や Step 2（サムネイル）の処理を開始しようとしてロック待ち（競合）に陥り、1ページ目の表示やサムネイル生成の完了が著しく遅延する。

3. **ビュー状態更新順序の不整合**:
   - `OnActiveSessionChanged` において `Document = newValue.Document` の設定が `IsDetailViewActive = ...` の設定より前に行われているため、グリッド表示から詳細表示のセッションへ切り替えた際に `InitializeDocument` が走る時点では `IsDetailViewActive` がまだ `false` のままとなっている。

---

## 3. 改修方針・設計

### 3.1 `ApplyFitMode()` の呼び出し整理
1. `DetailEditorViewModel.InitializeDocument` のシグネチャを拡張し、オプショナルな `DetailViewFitMode? preferredFitMode = null` を受け取れるようにする。
   - `preferredFitMode` が渡された場合、`_fitMode` を更新してプロパティ変更通知を行い、その後の `ApplyFitMode()` 呼び出しで新ドキュメントのページ寸法に基づきフィット倍率を1回のみ計算・適用する。
2. `MainViewModel.OnActiveSessionChanged` の整理:
   - `Document = newValue.Document` の前に呼んでいた `DetailEditor.FitMode = newValue.FitMode;` を撤廃（古いドキュメントに対する不要な再計算を防止）。
   - `Document = newValue.Document` の後に存在していた `DetailEditor.ApplyFitMode();`（L662）を撤廃（`InitializeDocument` 内で既に計算済みのため）。
   - `newValue.FitMode == DetailViewFitMode.None` の場合のみ、保存されていたカスタムズーム倍率 `newValue.ZoomFactor` を `DetailEditor.Zoom` に反映する。

### 3.2 初期描画パイプラインの一本化
1. `DetailEditorViewModel.PerformDynamicRenderAsync` の条件変更:
   - `if (PipelineExecutionHandler != null && !isInitialLoad)` の `&& !isInitialLoad` を除去し、`PipelineExecutionHandler != null` の場合は初回ロードも含めて常に `PipelineExecutionHandler` に委譲する。
   - `immediate: true`（初回ロード時等）の場合は `delay = 0` となり、即座に `PipelineExecutionHandler(0)`（統合パイプラインの即時実行）が起動される。
   - `PipelineExecutionHandler == null` の単体テスト環境では、従来の直接描画フォールバック（Step 1 + Step 3）がそのまま機能する。
2. `MainViewModel.OnActiveSessionChanged` における重複スケジュールの排除:
   - `IsDetailViewActive` の更新を `Document = newValue.Document` の代入より前に行う。これにより、`InitializeDocument` 経由でキックされるパイプラインが確実に詳細ビュー有効状態で即時実行される。
   - `OnActiveSessionChanged` 内の `_ = ScheduleInitialThumbnailsAsync();`（250msデバウンス）を撤廃。`InitializeDocument` から即座（delay = 0）に Step 1（主ページ高解像度描画）→ Step 2（可視サムネイル生成）→ Step 3（隣接ページ先読み）→ Step 4（残りサムネイル生成）が1本のパイプライン（`_thumbnailCts`）として実行される。
   - `!IsDetailViewActive`（グリッド表示）の場合は、安全に `EnsureThumbnailsGeneratedAsync()` を呼び出して一覧サムネイルを生成する。
3. `MainViewModel.OnDetailEditorPropertyChanged` の整理:
   - `CurrentPageNumber` 変更時に呼ばれていた `_ = ScheduleDetailViewThumbnailsAsync();`（250ms）を撤廃。ページ切り替え時は `DetailEditorViewModel` 自身から 50ms デバウンスで統合パイプラインが実行されるため、無駄な再スケジュールや遅延を解消する。

---

## 4. 影響範囲

| 対象ファイル | 変更内容 |
|---|---|
| `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs` | `InitializeDocument` に `preferredFitMode` 引数を追加、`PerformDynamicRenderAsync` でのパイプライン委譲を初回ロードにも適用 |
| `src/PDFBinder.App/ViewModels/MainViewModel.cs` | `OnActiveSessionChanged` での `ApplyFitMode` 重複呼び出し撤廃、ビュー状態更新順序の適正化、重複した 250ms スケジュールの撤廃 |
| `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs` | 初回ロード時のパイプライン委譲およびフォールバック動作の単体テスト追加・確認 |
| `tests/PDFBinder.Tests/DetailViewThumbnailPipelineTests.cs` | ドキュメント初期化時に単一の統合パイプラインが遅延なく実行されることの検証テスト追加 |

---

## 5. 検証手順・完了条件
1. **単体テストの実行**:
   - `dotnet test`: 既存テストおよび新規テストを含め 100% PASS すること。
2. **ビルド検証**:
   - `dotnet build`: 警告・エラーなくビルドが成功すること。
3. **検証項目**:
   - ファイルオープン時に `_renderLock` の競合や二重描画が発生せず、1ページ目およびサムネイルが最速で表示されること。
   - タブ切り替え時やファイル切り替え時のフィットモード・ページ番号復元が従来通り正常に動作すること。
