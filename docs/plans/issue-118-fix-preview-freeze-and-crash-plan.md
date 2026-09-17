# Issue #118: ファイルオープン時および高速ページ切り替え時のプレビュー真っ白フリーズ・クラッシュの解消 実装計画

## 問題の概要
PDFファイルを開いた直後や、手書き詳細エディタでページを次々と高速に切り替えた際に、プレビュー画面が真っ白のまま固まり（フリーズ）、最終的にアプリケーションが突然終了（クラッシュ）する現象が発生します。

### 根本原因の特定
1. **PDFium（Docnet.Core）の非スレッドセーフ性とネイティブクラッシュ**:
   - `DocLib.Instance` がラップするネイティブライブラリ `pdfium.dll` はスレッドセーフではありません。
   - バックグラウンドのサムネイル生成（`MainViewModel.UpdatePageThumbnailAsync`）、初回先行レンダリング（`DetailEditorViewModel.LoadInitialDocumentBackgroundAsync`）、詳細表示レンダリング（`PerformDynamicRenderAsync`）、およびテキスト抽出（`ExtractInteractiveDataAsync`）が複数の `Task.Run` スレッドから同時に PDFium を呼び出した結果、ネイティブレイヤでメモリアクセス違反（`AccessViolationException`）や内部デッドロックが発生し、CLRがクラッシュして異常終了します。
   - 例外がキャッチされた場合でも `CreateBlankPageBitmap` により白紙画像が返却され、プレビューが真っ白になります。
2. **高速ページ切り替え時のデバウンス欠落と大量レンダリングの多重実行**:
   - ページ切り替え時（`OnCurrentPageChanged`）に `immediate: true` で即時レンダリングがトリガーされます。
   - 矢印キーやマウス等で連続してページを切り替えると、ネイティブレンダリング処理は即座に中断できないため、未完了のタスクがスレッドプールに蓄積し、CPU飽和とメモリ圧迫（大容量LOH確保）が発生します。
   - `_renderCts?.Dispose()` によるレースコンディションと `ObjectDisposedException` の潜在的リスク。
3. **高解像度レンダリング完了前のプレビュー空白（白紙感）**:
   - ページ遷移直後は `PageBackground` が `null` のため、高解像度画像の完成まで背景の白い枠線（純白）のみが表示され、ユーザーには「固まって真っ白になった」ように知覚されます。

---

## ユーザーレビュー事項（方針合意済み）
- **PDFium 排他制御と優先度制御**:
  - PDFium ネイティブ呼び出しへの同時アクセスを 1 スレッドに制限する排他制御を導入。
  - 現在表示中のプレビューレンダリングを「高優先度（High）」、サムネイル生成や先読みを「低優先度（Low）」として制御し、ユーザーの閲覧を最優先にします。
- **サムネイルを活用した仮プレビュー表示**:
  - 高解像度レンダリング完了までの間、既存のサムネイルを仮プレビューとして即座に下敷き表示し、真っ白な画面をゼロにします。
- **ページ切り替えデバウンス（約75ms）**:
  - 連続切り替え時の不要な中間ページに対するフルレンダリング負荷を排除します。

---

## 変更対象ファイルと具体的な改修内容

### 1. Core サービス層: レンダリング排他・優先度制御

#### [MODIFY] [`IPdfRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/IPdfRenderer.cs)
- `RenderPriority` 列挙型（`High`, `Normal`, `Low`）を定義。
- `RenderPageAsync` に `RenderPriority priority = RenderPriority.Normal` パラメータを追加。

#### [MODIFY] [`PdfiumRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs)
- 優先度付き排他制御メカニズム（`PriorityAsyncLock` または優先度対応 `SemaphoreSlim` ラッパー）を実装。
- `RenderPageAsync` および `ExtractInteractiveDataAsync` の PDFium ネイティブ呼び出し（`DocLib.Instance`）をこのロックで保護し、多重実行を完全に防止。
- `High` 優先度（プレビュー）の要求が来た場合、待機中の `Low` 優先度（サムネイル・先読み）よりも先にロックを取得して実行。
- キャンセル要求（`cancellationToken`）の確実な反映と、ネイティブ例外時の安全な回復処理を強化。

---

### 2. App ViewModel 層: 仮プレビュー・デバウンス・優先度指定

#### [MODIFY] [`DetailPageItemViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailPageItemViewModel.cs)
- `PageBackground` が `null` の間でも `Page.Thumbnail` が存在すれば仮背景として提供できるプロパティ（または XAML 側でのサムネイル下敷きバインディング）を導入。

#### [MODIFY] [`DetailEditorView.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- `DetailPageItemTemplate` の背景表示領域において、高解像度画像（`PageBackground`）の下にサムネイル画像（`Page.Thumbnail`）を配置。
- これにより、ページを切り替えた瞬間にサムネイルが拡大表示され、高解像度画像がレンダリング完了した時点で滑らかに上書き表示される構造にします。

#### [MODIFY] [`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- `OnCurrentPageChanged` でのレンダリング呼び出しに短時間のデバウンス（75ms）を適用。キー連打や連続スクロール中の中間ページへの過剰レンダリングを防止。
- カレントページのレンダリング呼び出しには `RenderPriority.High` を指定。
- 前後ページの先読みおよび `LoadInitialDocumentBackgroundAsync` には `RenderPriority.Low` を指定。
- `_renderCts` の破棄タイミングを安全化し、レースコンディションを解消。

#### [MODIFY] [`MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- `UpdatePageThumbnailAsync`（サムネイル生成）の `_pdfRenderer.RenderPageAsync` 呼び出しに `RenderPriority.Low` を明示指定。
- 印刷プレビュー・出力呼び出しには `RenderPriority.Normal` を指定。

---

### 3. 単体テスト

#### [MODIFY] [`PdfiumRendererTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PdfiumRendererTests.cs)
- `PdfiumRenderer_ConcurrentCalls_ExecuteSafelyWithoutCrashing`: 複数スレッドからの同時呼び出し時に例外・クラッシュが発生せず正常に完了することの検証。
- `PdfiumRenderer_PriorityQueue_HighPriorityExecutesBeforeLowPriority`: 高優先度レンダリングが低優先度レンダリングより先に処理されることの検証。

#### [MODIFY] [`ViewModelsTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ViewModelsTests.cs)
- ページ連続切り替え時のデバウンス動作およびプレビュー表示状態の検証テストを追加。

---

## 検証手順

### 1. 自動テスト
- `dotnet test` を実行し、既存テストおよび新規追加テストがすべて PASS することを確認。

### 2. 手動・動作確認
- `dotnet build` で警告およびエラーがないことを確認。
- 複数ページ（10〜30ページ以上）のPDFを開き、詳細画面で矢印キーや次ページボタンを高速連打した際、プレビューが真っ白に固まったりアプリがクラッシュせず、サムネイルが即座に仮表示された後に高解像度レンダリングへ滑らかに更新されることを確認。
