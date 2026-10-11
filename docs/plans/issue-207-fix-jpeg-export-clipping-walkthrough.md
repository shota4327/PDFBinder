# 検証報告: Issue #207 JPEG書き出し時の画像切り取り（クリッピング）不具合の修正

## 1. 実施内容の概要
- **対象不具合**: JPEG画像書き出し時に、画像の一部分（左上）だけが切り取られて出力される不具合の修正。
- **原因**:
  - `ImageExportService.PrepareBitmapForExport` において、`RenderTargetBitmap` に指定した解像度（DPI）と、`DrawingVisual` の描画座標系（96 DPI 基準の DIP）の食い違いにより、`dpi > 96`（例: 300 DPI）の際に画像が拡大描画され、RTB のピクセルバッファ境界を超えた右側および下側が切り取られていました。
- **修正内容**:
  - `ImageExportService` 内の白背景合成を `CompositeOnWhiteBackground` メソッドに分離。
  - `RenderTargetBitmap` を 96 DPI（1 DIP = 1 ピクセル）で初期化して描画することで、浮動小数点誤差やリサンプリング（再補間によるボケ・にじみ）を発生させずに 1:1 ピクセル精度で白背景への合成を実施。
  - 合成後のピクセルデータから `CreateBitmapWithDpi` メソッドにより指定 DPI メタデータを付与した `BitmapSource` を生成し、JPEG エンコーダへ渡すように修正。
- **ルール更新 (`GEMINI.md`)**:
  - PRマージ後にIssueが閉じられない事象を防止するため、PR作成時（`gh pr create`）に自動クローズ用キーワード（`Closes #<Issue番号>` / `Fixes #<Issue番号>`）を明記すること、およびマージ後に `gh issue view` で確認し必要に応じて手動クローズ（`gh issue close`）を行うルールを明文化。

## 2. 変更ファイル
- `src/PDFBinder.Core/Services/ImageExportService.cs`: 白背景合成ロジックの修正およびメソッド分割
- `tests/PDFBinder.Tests/ImageExportServiceTests.cs`: 高DPI（300 DPI）でのJPEG書き出し時に四隅（右下）の画像データが切り取られずに保持されることを検証するテストケース（`SaveImageAsync_JpegFormat_HighDpi_DoesNotClipContent`）を追加
- `GEMINI.md`: PR作成時およびマージ後のIssueクローズ確実化ルールを追記
- `Directory.Build.props`: バージョン更新（0.11.3 → 0.11.4）
- `CHANGELOG.md`: 0.11.4 リリースノート追加
- `docs/basic_design.md` / `docs/PROJECT.md`: ドキュメント同期更新

## 3. 検証結果
1. **単体テスト (`dotnet test`)**:
   - 合格: 610件 / 失敗: 0件 / スキップ: 0件（100% 成功）
   - 新規追加した高DPI時のクリッピング防止テストを含む全テストが正常に通過しました。
2. **ビルド検証 (`dotnet build`)**:
   - 0 警告、0 エラーで正常にビルド完了しました。
