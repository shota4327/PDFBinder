# スキャンPDFのサムネイル横向き表示および詳細ビュー白紙化の不具合修正 検証報告 (Walkthrough)

## 概要
GitHub Issue [#34](https://github.com/shota4327/PDFBinder/issues/34): スキャナーや複合機で横向きスキャンされ、メタデータに `/Rotate 270` や `90` が設定されたPDFファイルにおいて発生していた以下の2つの不具合を修正しました。

1. **サムネイルが横向き（倒れた向き）で表示される不具合（二重回転バグ）の解消**
2. **詳細ビュー（手書きエディタ）を開くと真っ白になる不具合（Docnet引数制約違反例外の捕捉フォールバック）の解消**
3. **手書きストローク保存時の座標変換（画面表示系から用紙未回転座標系への逆回転マッピング）の導入**

---

## 変更内容詳細

### 1. `PDFBinder.Core`
- **[`PdfPageModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PdfPageModel.cs)**:
  - `OriginalRotation` プロパティを追加（PDF読込時の初期回転角度を記録）。
  - `RenderRotation` 算出プロパティを追加:
    ```csharp
    public PageRotation RenderRotation =>
        PageRotationExtensions.FromDegrees(((int)Rotation - (int)OriginalRotation + 360) % 360);
    ```
    PDFiumが元PDFの回転を反映して正立レンダリングするため、アプリが追加適用すべき回転量は元PDFからの差分角度のみとする。
- **[`PdfiumRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs)**:
  - Docnet の `PageDimensions(dimOne, dimTwo)` は `dimOne <= dimTwo`（短辺・長辺）を要求するため、`Math.Min(targetWidth, targetHeight), Math.Max(targetWidth, targetHeight)` で正規化して例外を完全防止。
- **[`PdfService.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfService.cs)**:
  - `LoadDocumentAsync`: `OriginalRotation` と `Rotation` の両方に `pdfPage.Rotate` を設定。
  - `DrawSingleStroke`: 手書きストローク描画時、`page.Rotate` に応じて画面の表示座標系（DisplayWidth × DisplayHeight）から用紙未回転座標系（Width × Height）へ逆回転変換する `TransformDisplayToPagePoint` を適用。

### 2. `PDFBinder.App`
- **[`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)**:
  - `LoadPageBackgroundAsync`: `CurrentPage.DisplayWidth` および `DisplayHeight` をもとに高解像度レンダリングサイズを計算し、`CurrentPage.RenderRotation` を指定してレンダリング。
- **[`MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)**:
  - `UpdatePageThumbnailAsync`: サムネイル生成時に `page.RenderRotation` を指定してレンダリング。

### 3. ドキュメント同期
- **[`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md)**: `PdfPageModel` に `OriginalRotation`, `RenderRotation`, `DisplayWidth`, `DisplayHeight` を追記。
- **[`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md)**: 機能インベントリに `F22` を追加、テスト件数を64件に更新。

---

## テスト＆検証結果

### 1. 単体テスト (`PDFBinder.Tests`)
[`ScannedPdfRotationTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ScannedPdfRotationTests.cs) を新規作成し、実ファイル `sample_scanned.pdf`（横向きスキャン・Rotate270）を用いた以下のシナリオを自動検証しました:
- `LoadDocumentAsync_ScannedPdf_PreservesOriginalRotationAndSetsDeltaToZero`: 初期読込時に `OriginalRotation = 270`, `Rotation = 270`, `RenderRotation = 0` となり、`DisplayWidth < DisplayHeight`（縦向き）となること
- `RenderThumbnail_ScannedPdf_RendersPortraitWithoutDoubleRotation`: サムネイルが二重回転されず、正立縦向き（PixelHeight > PixelWidth）で描画されること
- `RenderDetailView_ScannedPdf_SucceedsWithoutArgumentException`: 詳細エディタサイズで例外（`dimOne > dimTwo`）が発生せず、高解像度正立ビットマップが生成されること
- `UserRotateOperations_CorrectlyUpdatesRenderRotationDelta`: ユーザーの回転操作（⟳, ⟲）で `RenderRotation` が差分角度として正しく計算されること
- `DetailEditorViewModel_ScannedPdf_LoadsBackgroundSuccessfully`: `DetailEditorViewModel` 経由で白紙にならず背景画像が正常にロードされること
- `SaveDocumentAsync_ScannedPdfWithInk_TransformsCoordinatesAndSavesSuccessfully`: 手書きストローク付きで保存後、再読込できること

```text
成功!   -失敗: 0、合格: 64、スキップ: 0、合計: 64、期間: 908 ms - PDFBinder.Tests.dll (net10.0)
```

### 2. ソリューションビルド
- `dotnet build`: 警告0・エラー0でビルド成功。
