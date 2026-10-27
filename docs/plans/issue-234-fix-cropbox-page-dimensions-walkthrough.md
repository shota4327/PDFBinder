# 検証報告: CropBoxが定義されたPDFページで用紙サイズが正しく反映されず画像が引き伸ばされる不具合の修正 (Issue #234)

## 1. 概要
`CropBox`（表示枠・トリミング領域）が設定されているPDF（表紙が縦長、本文が見開き横長など）において、ページサイズが物理用紙枠（`MediaBox`）の寸法で一律に認識され、表示時にアスペクト比が崩れて横方向に強制的に引き伸ばされてしまう不具合を修正しました。

## 2. 実施した変更内容
1. **`PdfService.cs` における有効ページ寸法取得の実装**:
   - `GetEffectivePageSize(PdfPage pdfPage)` ヘルパーメソッドを追加。
   - `pdfPage.CropBox` の幅・高さが正の値を持つ場合は `CropBox` の寸法を優先して返し、未定義または無効な場合はフォールバックとして `MediaBox`（`pdfPage.Width.Point`, `pdfPage.Height.Point`）の寸法を返すよう改修。
   - ドキュメント読み込み時の `ParseDocumentModel` で各ページの `Width` / `Height` に上記有効寸法を設定。
   - 手書き注釈保存処理 `AttachInkAnnotationToPage` において、注釈の境界矩形（`/Rect`）を `cropBox` に合わせ、アピアランスストリームのサイズも有効寸法に連動。
2. **`PdfLinkHelper.cs` におけるリンク注釈抽出処理の適正化**:
   - `ExtractRawLinks` で `PdfService.GetEffectivePageSize` を使用し、ページの有効寸法を取得。
   - `CropBox` にオフセット（`cropBox.X1`, `cropBox.Y1`）が存在する場合でも、リンク注釈の座標（`/Rect`）が表示領域基準で正確に正規化されるよう対応。
3. **バージョン更新および CHANGELOG の確定**:
   - `Directory.Build.props`: `0.13.6` -> `0.13.7` にインクリメント。
   - `CHANGELOG.md`: エンドユーザー向けリリースノートとして `[0.13.7] - 2026-10-27` セクションを追加。
   - `docs/basic_design.md`: `IPdfService.LoadDocumentAsync` の仕様記述に CropBox 認識に関する記述を反映。

## 3. テスト・検証結果
1. **新規単体テストの追加と成功**:
   - `LoadDocumentAsync_PdfWithCropBox_UsesCropBoxDimensionsInsteadOfMediaBox`:
     - 1ページ目（MediaBox: 840×590 横長、CropBox: 360×510 縦長）が縦長（360×510 pt）として認識されることを検証（PASS）。
     - 2ページ目（MediaBox: 840×590 横長、CropBox: 720×510 横長）が横長（720×510 pt）として認識されることを検証（PASS）。
     - 3ページ目（CropBoxなし）が MediaBox（500×700 pt）として認識されることを検証（PASS）。
   - `LoadDocumentAsync_ActualShaverPdf_RecognizesFirstPageAsPortrait`:
     - 実ファイル `シェーバー.pdf` を読み込み、1ページ目が縦長（約 362.8 × 515.9 pt）、2ページ目が横長（約 725.7 × 515.9 pt）、19ページ目（裏表紙）が縦長（約 362.8 × 515.9 pt）として正しく識別されることを検証（PASS）。
2. **テストスイート全体の実行**:
   - `dotnet test`: 全654件の単体テストがすべて 100% PASS（失敗 0、スキップ 0）。
3. **ビルド検証**:
   - `dotnet build`: 警告・エラーなし（0 個の警告、0 エラー）。

## 4. 変更ファイル一覧
- `src/PDFBinder.Core/Services/PdfService.cs`
- `src/PDFBinder.Core/Helpers/PdfLinkHelper.cs`
- `tests/PDFBinder.Tests/PdfServiceTests.cs`
- `Directory.Build.props`
- `CHANGELOG.md`
- `docs/basic_design.md`
- `docs/plans/issue-234-fix-cropbox-page-dimensions-plan.md`
- `docs/plans/issue-234-fix-cropbox-page-dimensions-walkthrough.md`
