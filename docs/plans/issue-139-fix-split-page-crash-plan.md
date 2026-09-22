# ページ分割時のアプリ強制終了不具合の修正計画（Issue #139）

起動直後など、バックグラウンドでのサムネイル生成が完了する前に「ページ分割」ボタンを押した際、サムネイル生成とページ分割・新規レンダリングが競合してアプリが強制終了する不具合を修正します。

## ユーザーフィードバックに基づく設計方針
- **排他制御の追加は行わない**: レンダリングエンジン（`PdfiumRenderer`）のロック変更等の複雑な排他制御は追加せず、既存実装を維持します。
- **サムネイル生成タスクの中断と完了待機**: ページ分割ボタンが押された際、進行中のサムネイル生成タスクを「その時点までで安全に終了」させ、タスクの完了を待機した後にページ分割処理を開始します。

---

## 変更内容の概要

### 1. `PDFBinder.App` (ViewModel でのサムネイル生成中断と完了待機)

#### [MODIFY] [`MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- **サムネイル生成タスクの追跡フィールドの追加**:
  - `private CancellationTokenSource? _thumbnailCts;`
  - `private Task? _thumbnailTask;`
- **サムネイル生成処理（`EnsureThumbnailsGeneratedAsync`）の中断対応**:
  - 内部で新しい `CancellationTokenSource` を発行し、進行中タスクとして `_thumbnailTask` に保持。
  - ページごとのサムネイル生成ループ（`foreach`）内で `token.IsCancellationRequested` をチェックし、中断要求があった場合は未処理のページをスキップしてその時点で安全にループを終了。
- **安全な中断・完了待機メソッド `CancelAndAwaitThumbnailsAsync()` の実装**:
  - 進行中の `_thumbnailCts?.Cancel()` を発行。
  - `_thumbnailTask` が存在する場合、その完了を `await _thumbnailTask` で確実に待機（キャンセル等による例外は安全に補足）。
  - 待機完了後、フィールドをリセット。
- **`SplitPagesHalfAsync` 冒頭での中断・待機呼び出し**:
  - ページ分割処理の開始直前に `await CancelAndAwaitThumbnailsAsync()` を実行。
  - サムネイル生成タスクが終了したことを確認してから `_pdfService.SplitPagesHalfAsync(oldPages)` を実行し、完了後に新ページのサムネイル生成を開始。

---

### 2. 単体テスト

#### [NEW] [`tests/PDFBinder.Tests/SplitConcurrentCrashTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/SplitConcurrentCrashTests.cs)
- サムネイル生成がバックグラウンドで走っている最中に `SplitPagesHalfCommand` を実行するテストを作成。
- 進行中のサムネイル生成タスクが中断され、安全に待機した後にページ分割が完了し、新ページのサムネイルが正しく生成されることを検証。

---

## 検証手順

### 自動テスト
1. `dotnet test --filter "FullyQualifiedName~SplitConcurrentCrashTests"`
2. `dotnet test`（全テスト 100% PASS を確認）

### 手動検証
1. `dotnet build`
2. ページ数の多い PDF を開き、サムネイル生成中（プログレスバー表示中）に素早く「ページ分割」ボタンをクリック。
3. アプリが強制終了することなく、サムネイル生成が中断されてページ分割が正常に完了し、分割後の全ページサムネイルが生成されることを確認。
