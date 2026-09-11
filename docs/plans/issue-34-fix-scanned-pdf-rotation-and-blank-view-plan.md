# スキャンしたPDFのサムネイル横向き表示および詳細ビュー白紙化の不具合修正 実装計画

## 概要
プリンターや複合機でスキャンされたPDF（用紙が横向きスキャンされ、メタデータに `/Rotate 270` や `90` が付与されたファイル）を開いた際に、以下の2つの不具合が発生する問題を根本修正します。

1. **サムネイルが横向き（倒れた状態）で表示される問題**:
   - PDFium（DocLib）がPDF内部の `/Rotate` を解釈して自動的に正立状態でレンダリングするのに対し、アプリ側（`PdfiumRenderer`）でも `Rotation` に応じて二重に変形回転（`RotateTransform`）を適用していたため、正立していた画像が倒れて横向きになっていた。
2. **詳細ビュー（手書きエディタ）を開くと真っ白になる問題**:
   - 横向き用紙のPDF（`Width > Height`）において、詳細エディタの高解像度ターゲットサイズ（`targetWidth > targetHeight`）をそのまま Docnet の `PageDimensions(dimOne, dimTwo)` に渡すと、`dimOne <= dimTwo`（短辺・長辺）制約違反により `ArgumentException: dimOne can't be more than dimTwo` がスローされていた。
   - `PdfiumRenderer` の `catch` 節でこの例外を捕捉し白紙ビットマップ（`CreateBlankPageBitmap`）を返却していたため、詳細画面が真っ白になっていた。
3. **手書きストローク保存時の座標ズレ（先回り防止）**:
   - 画面上の `InkCanvas` は正立表示サイズ（`DisplayWidth × DisplayHeight`）で描画されますが、PdfSharp の `XGraphics` は用紙未回転座標系（`Width × Height`）に対して描画するため、保存時に Display 座標系から用紙座標系への回転逆変換を行わないと手書きが 90°/270° ズレて保存されてしまう問題を防止。

---

## 変更内容

### 1. PDFBinder.Core

#### [MODIFY] [PdfPageModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PdfPageModel.cs)
- `OriginalRotation` プロパティを追加（元PDFから読み取った初期回転角度）。
- `RenderRotation` 算出プロパティを追加:
  ```csharp
  public PageRotation RenderRotation =>
      PageRotationExtensions.FromDegrees(((int)Rotation - (int)OriginalRotation + 360) % 360);
  ```
  DocLib が元PDFの `OriginalRotation` を反映して正立描画するため、アプリ側のレンダラが適用すべき追加回転量は元PDFからの差分角度のみとします。

#### [MODIFY] [PdfService.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfService.cs)
- `LoadDocumentAsync`: ページ読み込み時に `OriginalRotation` と `Rotation` の両方に `pdfPage.Rotate` を設定。
- `DrawSingleStroke`: 手書きストローク描画時、`page.Rotate` に応じて画面の Display 座標 `(x, y)` を用紙本来の座標系 `(x_p, y_p)` へ逆回転変換する `TransformDisplayToPagePoint` を適用。

#### [MODIFY] [PdfiumRenderer.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs)
- `RenderPageAsync`:
  - `Docnet.Core.Models.PageDimensions` 生成時、`Math.Min(targetWidth, targetHeight), Math.Max(targetWidth, targetHeight)` を指定し、`dimOne <= dimTwo` 制約を常に満たすよう正規化。

---

### 2. PDFBinder.App

#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- `LoadPageBackgroundAsync`:
  - `targetWidth` / `targetHeight` の計算元を `CurrentPage.Width / Height` から `CurrentPage.DisplayWidth / DisplayHeight` に変更。
  - `_pdfRenderer.RenderPageAsync` 呼び出し時の回転引数に `CurrentPage.RenderRotation` を渡す。

#### [MODIFY] [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- `UpdatePageThumbnailAsync`:
  - 既存PDFページのレンダリング時に `page.RenderRotation` を渡す（白紙ページは `page.Rotation` を維持）。

---

### 3. 単体テスト

#### [NEW] [ScannedPdfRotationTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ScannedPdfRotationTests.cs)
- `sample.pdf`（横向きスキャン・Rotate270）および回転付きテスト用PDFを対象とした自動テストを作成:
  1. ドキュメント読み込み時の `OriginalRotation` と `Rotation` の一致検証
  2. サムネイルおよび詳細エディタ向けレンダリングで例外が発生せず、正しく正立画像が生成されることの検証
  3. ユーザーによる回転操作（⟳, ⟲）時に `RenderRotation` が差分角度として正しく計算され、反映されることの検証
  4. 手書きストロークの保存時に座標変換が正しく適用されることの検証

---

## 検証計画

### 自動テスト
- `dotnet test`: 既存の58テスト＋新規追加テストがすべて PASS することを確認。

### ビルド確認
- `dotnet build`: 警告・エラーなくビルドが成功することを確認。
