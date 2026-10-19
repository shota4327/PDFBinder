# Issue #221: ドキュメントオープン時の詳細ビュー初期描画パイプラインの二重起動・ロック競合の解消 検証報告 (Walkthrough)

## 1. 概要
PDFドキュメントを開いた（またはセッションを切り替えた）際に発生していた、`ApplyFitMode()` の多重実行および初期描画タスク（`_renderCts`）と250ms後発パイプライン（`_thumbnailCts`）による `_renderLock` 排他ロック競合・重複レンダリングを解消し、単一の統合パイプライン（`RunDetailViewPipelineAsync`）による遅延のない直列実行へ一本化しました。

---

## 2. 変更内容詳細

### 2.1 `DetailEditorViewModel.cs`
- **`InitializeDocument` のシグネチャ拡張とFitMode適用の1回化**:
  - `preferredFitMode` 引数を追加し、新ドキュメントのページ構成展開後に `FitMode` を設定した上で `ApplyFitMode()` を1回のみ実行するように整理。
  - `PipelineExecutionHandler` が登録されている場合は、独立した未管理の直接描画（`LoadInitialDocumentBackgroundAsync`）を起動せず、オーケストレーターである `MainViewModel` の統合パイプラインに一本化。
- **`PerformDynamicRenderAsync` の条件一本化**:
  - `PipelineExecutionHandler != null` の場合は初回ロード（`isInitialLoad`）であっても常に統合パイプラインへ委譲。
  - ハンドラー不在時（単体テスト環境等）の直接フォールバック描画は既存通り維持。

### 2.2 `MainViewModel.cs`
- **`OnActiveSessionChanged` の整理と即時統合パイプライン起動**:
  - `Document` 更新前に呼んでいた古いドキュメントに対する `DetailEditor.FitMode` 設定、および `Document` 更新後の重複 `DetailEditor.ApplyFitMode()`（計3回）を完全に撤廃。
  - `IsDetailViewActive` の更新を `Document` 代入より先に行うことで、初期化時点から詳細ビュー有効状態を確定。
  - 250msデバウンスの `ScheduleInitialThumbnailsAsync` を撤廃し、`ScheduleDetailViewPipelineAsync(debounceMs: 0)` を1本のみ起動。
  - これにより、Step 1（主ページ高解像度描画: High優先度）→ Step 2（可視サムネイル生成）→ Step 3（隣接ページ先読み: Low優先度）→ Step 4（残りサムネイル生成）が1つの `CancellationTokenSource` 下で排他ロック競合なく最速で直列実行される。
- **`OnDetailEditorPropertyChanged` / `Undo` / `Redo` の整理**:
  - `CurrentPageNumber` 変更時の 250ms サムネイル再スケジュールを撤廃（ページ切り替え時は `DetailEditorViewModel` から 50ms デバウンスで統合パイプラインが実行されるため）。
  - `Undo` / `Redo` における 250ms サムネイル再スケジュールを撤廃し、即時描画パイプラインの遅延を解消。

### 2.3 バージョンおよびドキュメント更新
- `Directory.Build.props`: `0.13.2` → `0.13.3` にインクリメント
- `CHANGELOG.md`: `0.13.3` のエンドユーザー向けリリースノートを追記
- `docs/PROJECT.md`: Feature Inventory に `F80` を追加（完了）
- `docs/basic_design.md`: 初期描画パイプライン一本化の設計仕様を追記

---

## 3. 検証結果

### 3.1 単体テスト実行結果 (`dotnet test`)
既存の全テストに加え、今回の改修を検証する3件の新規単体テストを追加し、全件パスを確認しました。

- **新規追加テスト**:
  1. `DetailEditorViewModelTests.InitializeDocument_WithPreferredFitMode_AppliesSpecifiedFitMode`: 新ドキュメント初期化時に `preferredFitMode` が反映され、ズーム倍率が正確に1度で計算されることを検証。
  2. `DetailEditorViewModelTests.InitializeDocument_WhenPipelineExecutionHandlerPresent_DoesNotLaunchStandaloneDynamicRender`: ハンドラー存在時に独立した未管理の直接描画が走らないことを検証。
  3. `DetailViewThumbnailPipelineTests.ActiveSessionChanged_ExecutesUnifiedPipelineImmediately`: セッション切り替え時に即時統合パイプラインが起動され、イベントログ先頭で主ページの高解像度描画が最優先実行されることを検証。

```
成功!   -失敗:     0、合格:   645、スキップ:     0、合計:   645、期間: 10 s - PDFBinder.Tests.dll (net10.0)
```

### 3.2 ビルド検証 (`dotnet build`)
```
ビルドに成功しました。
    0 個の警告
    0 エラー
```
