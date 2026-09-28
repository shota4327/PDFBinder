# 実装計画: Issue #170 画像の回転処理で異常終了する不具合の修正

## 1. 概要
- **Issue**: #170 画像の回転処理で異常終了する
- **現象**: PNG（およびJPEG）ファイルを開いた後、回転ボタン（時計回り／反時計回り）を押すとアプリが固まって異常終了（クラッシュ）する。
- **根本原因**:
  - `PdfiumRenderer.RenderImagePage` がバックグラウンドスレッドで `BitmapDecoder.Frames[0]` をベースにビットマップを生成している。
  - `BitmapDecoder` は生成されたワーカースレッドの Dispatcher 所有権（スレッドアフィニティ）を保持している。
  - 回転ボタン押下時、UIスレッド上の `DetailPageItemViewModel.ApplyInstantRotation` で即時幾何回転プレビューとして `BitmapTransformHelper.CreateRotatedBitmap(PageBackground, deltaDegrees)`（`TransformedBitmap.Freeze()`）が実行される。
  - WPF Freezable の `FreezeCore` が `BitmapDecoder.get_IsDownloading()` にアクセスした際、別スレッド所有オブジェクトへのアクセスとして `Dispatcher.VerifyAccess()` が `InvalidOperationException` をスローし、UIスレッドで未補足クラッシュが発生する。

---

## 2. 改修設計方針

### 2.1 デコーダー・スレッド依存を切り離すヘルパーの追加
- **対象ファイル**: `src/PDFBinder.Core/Helpers/BitmapTransformHelper.cs`
- **追加メソッド**: `CreateDetachedBitmap(BitmapSource source)`
  - 元画像の `PixelFormat`、幅、高さ、DPI、パレット（存在する場合）を完全に維持。
  - `source.CopyPixels` でピクセルバッファを抽出し、`BitmapSource.Create(...)` によりスレッド所有権やデコーダーへの参照を一切持たない純粋なメモリビットマップを生成。
  - 生成直後に `Freeze()` を実行し、全スレッドから安全に読み取り・変換可能なオブジェクトとして返却。

### 2.2 レンダラーおよび画像保存処理の安全化
- **対象ファイル**: `src/PDFBinder.Core/Services/PdfiumRenderer.cs`
  - `RenderImagePage` において、`decoder.Frames[0]` 取得直後に `BitmapTransformHelper.CreateDetachedBitmap` を適用し、デコーダーとスレッドアフィニティを完全に切り離したビットマップを生成・使用する。
- **対象ファイル**: `src/PDFBinder.Core/Services/ImageService.cs`
  - `BuildAndSaveImageFile` および `BuildAndSaveImagePdf` において、同様に `CreateDetachedBitmap` を適用し、将来的なスレッド間参照リスクを未然に防止。

### 2.3 単体テストの追加・強化
- **対象ファイル**: `tests/PDFBinder.Tests/PdfiumRendererTests.cs`
  - PNGおよびJPEG画像を `RenderPageAsync` で各種角度（90度、180度、270度）へレンダリングできることの検証。
- **対象ファイル**: `tests/PDFBinder.Tests/ViewModels/MainViewModelImageTests.cs`
  - 実際のPNGおよびJPEGファイルを開き、`RotateClockwiseCommand` / `RotateCounterClockwiseCommand` を実行しても例外が発生せず、正常に回転・更新されることの統合検証。

---

## 3. 実装手順
1. `src/PDFBinder.Core/Helpers/BitmapTransformHelper.cs` に `CreateDetachedBitmap` メソッドを実装。
2. `src/PDFBinder.Core/Services/PdfiumRenderer.cs` の `RenderImagePage` に `CreateDetachedBitmap` を適用。
3. `src/PDFBinder.Core/Services/ImageService.cs` のデコード処理に `CreateDetachedBitmap` を適用。
4. `tests/PDFBinder.Tests/PdfiumRendererTests.cs` および `tests/PDFBinder.Tests/ViewModels/MainViewModelImageTests.cs` にテストケースを追加。
5. `dotnet test` および `dotnet build` を実行し、全テストのパスとビルドエラーゼロを確認。
6. 検証報告（Walkthrough）を作成し、ユーザーへ報告。
