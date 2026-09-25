# 実装計画: ページ最下層への白色矩形挿入による透明背景PDFレンダリング改善 (第2版)

## 1. 概要・背景
前回の改修で導入したピクセル単位の白背景後処理合成（`CompositeOverWhite`）は、PDFium（FreeType）が透明背景に対して出力したアンチエイリアスピクセルに対して線形加算を行ったため、ガンマ補正が破綻して文字の輪郭が滲み・ボケる現象を引き起こしていました。

本改修では、後処理のピクセル演算を完全に撤廃し、PDFium にレンダリングを委譲する直前の一時メモリ処理（`SanitizeForRendering`）において、PdfSharp の `XGraphicsPdfPageOptions.Prepend` を用いて**ページコンテンツストリームの最下層（文字や図形の背後）に白色の矩形を描画**します。

これにより、PDFium がネイティブに「白い用紙の上」にすべての文字・図形・アンチエイリアスを描画するため、フォントの輪郭が一切損なわれず、完全不透明な白背景画像が生成されます。

---

## 2. 変更対象ファイル
1. `src/PDFBinder.Core/Services/PdfiumRenderer.cs`:
   - `SanitizeForRendering` 内で `XGraphicsPdfPageOptions.Prepend` を使用し、対象ページの最下層に白色矩形（`XBrushes.White, 0, 0, page.Width.Point, page.Height.Point`）を描画。
   - `CompositeOverWhite` 呼び出しおよびメソッド定義を完全削除。
2. `tests/PDFBinder.Tests/PdfiumRendererTests.cs`:
   - 削除した `CompositeOverWhite` の単体テストを削除。
   - `RenderPageAsync_TransparentPdf_ProducesOpaqueWhiteBackground` により、最下層白色矩形挿入によって透明PDFが完全不透明な白背景として正常にレンダリングされることを検証。
3. `CHANGELOG.md`:
   - エンドユーザー向けリリースノートの記載内容を確認。

---

## 3. 検証手順
1. `dotnet test`: 全単体テストが 100% PASS すること。
2. `dotnet build`: Release ビルドが警告・エラー 0 件で成功すること。
