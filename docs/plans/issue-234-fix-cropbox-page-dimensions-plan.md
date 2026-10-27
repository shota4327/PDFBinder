# 実装計画: CropBoxが定義されたPDFページで用紙サイズが正しく反映されず画像が引き伸ばされる不具合の修正 (Issue #234)

## 1. 概要
`CropBox`（表示枠・トリミング枠）が定義されているPDF（冊子・カタログ形式などで表紙が縦長、本文が見開き横長など）において、ページサイズが物理用紙枠（`MediaBox`）の寸法で一律に認識され、表示時にアスペクト比が崩れて横方向に強制的に引き伸ばされてしまう不具合を修正します。

## 2. 根本原因
1. **ページ寸法取得時の仕様不整合**:
   - `PdfService.ParseDocumentModel` では `pdfPage.Width.Point` および `pdfPage.Height.Point` を直接参照して `PdfPageModel.Width` / `Height` を決定している。
   - `PdfSharp` の実装において、`pdfPage.Width / Height` は `CropBox` を考慮せず、常に `MediaBox`（物理用紙サイズ）の幅と高さを返す。
   - その結果、表紙など `CropBox` により縦長（例: 362.84 × 515.91 pt）に設定されたページも、見開き全体（A4横: 841.89 × 595.28 pt）として `PdfPageModel` に登録されていた。
2. **描画エンジンとUI表示枠の乖離**:
   - レンダリングエンジン（PDFium）は PDF 標準仕様に従い `CropBox`（縦長）に基づいてビットマップを生成する。
   - しかし、詳細エディタ（`DetailEditorView.xaml`）のコンテナ `Grid` は `Page.DisplayWidth` / `DisplayHeight`（横長）で確保され、配置される画像が `Stretch="Fill"` となっているため、縦長画像が横長枠に合わせて横方向に強制的に引き伸ばされて歪んでいた。
3. **手書き注釈やリンク注釈への波及**:
   - `PdfService.AttachInkAnnotationToPage` の注釈枠（`/Rect`）やリンク注釈抽出処理でも、`CropBox` ではなく `page.Width / Height` が使われていたため、位置ずれの原因となり得る。

## 3. 改修方針
1. **`PdfService.cs` におけるページ寸法の適正化**:
   - `PdfPage` からページ寸法を取得するヘルパーメソッド（`GetEffectivePageSize(PdfPage pdfPage)`）を導入。
   - `pdfPage.CropBox` を参照し、正の幅・高さを持つ有効な `CropBox` が定義されている場合は、その寸法を優先して `PdfPageModel.Width` / `Height` に採用する。
   - `CropBox` が未定義または無効な場合は、フォールバックとして従来の `MediaBox`（`pdfPage.Width.Point`, `pdfPage.Height.Point`）を採用する。
2. **手書き注釈保存・リンク抽出処理の適正化**:
   - `PdfService.AttachInkAnnotationToPage` において、注釈の境界矩形（`/Rect`）をページの `CropBox`（または `MediaBox`）に正確に一致させ、アピアランスストリームのサイズも有効寸法に合わせる。
   - `PdfLinkHelper.ExtractRawLinks` において、ページ寸法を `GetEffectivePageSize` と同期させ、リンク注釈座標を CropBox 基準で適正に正規化する。
3. **単体テストの追加と既存機能の保護**:
   - CropBox を含むテスト用PDF（表紙が縦長、本文が横長見開き）をインメモリで生成し、`LoadDocumentAsync` 時に各ページが正しい寸法・向き（縦長・横長）としてモデル化されることを検証するテストケースを `PDFBinder.Tests` に追加。
   - 手書き注釈付与、PDF保存後も `CropBox` が正しく保持されることを確認する。
   - 既存のすべての単体テスト（`dotnet test`）が 100% PASS することを確認する。

## 4. 影響範囲
- `src/PDFBinder.Core/Services/PdfService.cs`（ページ読み込み時の寸法決定、注釈付与）
- `src/PDFBinder.Core/Helpers/PdfLinkHelper.cs`（リンク注釈抽出時の基準寸法）
- `tests/PDFBinder.Tests/PdfServiceTests.cs`（単体テスト追加）

## 5. タスク一覧
- [ ] Task 1: `PdfService.cs` に `GetEffectivePageSize` を実装し、ドキュメント解析処理（`ParseDocumentModel`）および手書き注釈付与（`AttachInkAnnotationToPage`）に適用
- [ ] Task 2: `PdfLinkHelper.cs` のリンク注釈抽出処理に有効ページ寸法を適用
- [ ] Task 3: `PDFBinder.Tests` に CropBox を持つPDFの読み込みおよび寸法検証の単体テストを追加
- [ ] Task 4: ビルドおよびテスト実行（`dotnet test` / `dotnet build`）の完全パス確認
- [ ] Task 5: バージョンインクリメント（`Directory.Build.props` / `CHANGELOG.md`）の準備
