# 実装計画: Issue #207 JPEG書き出し時の画像切り取り（クリッピング）不具合の修正

## 1. 概要・不具合の原因
- **現象**: PDFページをJPEG画像として書き出した際、画像の一部分（左上）だけが切り取られて出力される。
- **原因**:
  - `ImageExportService.PrepareBitmapForExport` 内で、JPEG出力時の白背景合成に `RenderTargetBitmap` を使用している。
  - `new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32)` で DPI（例: 300）を指定しているが、WPFの `DrawingVisual` の座標系は常に **96 DPI 基準の DIP（Device Independent Pixels）** である。
  - `dc.DrawImage(bitmap, new Rect(0, 0, width, height))` のようにピクセル単位の寸法をそのまま渡しているため、DrawingVisual 上では `dpi / 96` 倍（300 DPI なら約 3.125 倍）に拡大されて描画される。
  - 一方、`RenderTargetBitmap` のバッファサイズは `width x height` ピクセルであるため、描画領域の左上約 `(96 / dpi)`（300 DPI の場合は全体の約 32%）のみがバッファ内に収まり、残りの領域が切り取られて消失していた。

## 2. 修正方針
1. **白背景合成時の 1:1 ピクセル描画と指定 DPI 設定の分離**:
   - `RenderTargetBitmap` を 96 DPI（`new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32)`）で初期化し、`new Rect(0, 0, width, height)` で描画する。
   - これにより、1 DIP = 1 ピクセルとなり、浮動小数点丸め誤差やリサンプリング（再補間によるボケ・にじみ）を一切発生させず、1:1 ピクセル精度で白背景への合成が行われる。
   - 合成後のピクセルデータを取得し、`BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgr32, null, pixels, stride)` により、指定された出力 DPI メタデータを付与した `BitmapSource` を生成する。
   - これにより、JPEGエンコーダに対して意図した解像度（DPI）と正確な全画面画像データが渡される。
2. **単体テストの拡充**:
   - `ImageExportServiceTests` において、300 DPI 等の 96 以外の DPI で JPEG 出力した際、画像の右下領域（クリッピングされやすかった領域）のピクセルが正しく保持されているかを検証するテストを追加する。
   - 既存のテストスイート（609件）がすべて合格することを確認する。

## 3. 変更対象ファイル
- `src/PDFBinder.Core/Services/ImageExportService.cs`: 白背景合成および DPI 設定ロジックの修正
- `tests/PDFBinder.Tests/ImageExportServiceTests.cs`: 高DPIでのJPEG画像内容（クリッピング防止）検証テスト追加
- `docs/plans/issue-207-fix-jpeg-export-clipping-plan.md`: 実装計画の永続化記録

## 4. 検証手順
1. `dotnet test`: 新規テストを含む全単体テストが成功することを確認
2. `dotnet build`: ビルドが警告・エラーなく成功することを確認
