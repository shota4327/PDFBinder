# 手書き文字の再編集可能な保存・復元 (Issue #89) 実装計画

## 概要 (Overview)
PDF Binder で手書き（ペン、蛍光ペン、直線、筆圧等）を描画して保存したファイルを再度開いた際、現行仕様では PDF の本文（コンテンツストリーム）にベクター描画が焼き込まれ（フラット化され）、背景画像の一部となってしまうため、消しゴムツール等で再編集することができませんでした。

本改修では、**「PDF Binder では100%の品質（筆圧・色・太さ・蛍光ペン）で完全再編集可能（消しゴム消去・追記）とし、他社製PDFリーダー（Edge, Chrome, Acrobat等）では綺麗に表示・印刷可能」** という方針のもと、**「アピアランス付き専用注釈（`/AP`） ＋ ISF（Ink Serialized Format）メタデータ埋め込み ＆ レンダリング時の他社注釈分離」** アーキテクチャを実装します。

---

## ユーザーレビュー確認事項 (User Review Required)

> [!IMPORTANT]
> **他社製注釈の保護とPDF Binder手書きの選別方針（方針A採用）**
> - 他社製ソフト（Adobe Acrobat、Microsoft Edge等）で作成されたテキスト注釈、付箋、ハイライト等は、背景画像として100%忠実に描画されます。
> - PDF Binder の手書き注釈（`/PdfBinderInk` 識別タグ付き）のみを背景ラスタライズから除外し、最前面の `EditorInkCanvas` にのみ展開します。
> - これにより、「背景の二重描画」や「消しゴムで消しても背景に線が残る問題」を完全に防止し、他社製注釈の表示消失リスクもゼロになります。

---

## 提案する変更点 (Proposed Changes)

### 1. PDFドメイン・モデル層 (`PDFBinder.Core/Models`)
- [`PdfPageModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PdfPageModel.cs)
  - ページが PDF Binder 専用の手書き注釈を保持しているかどうかの判定プロパティ・フラグの整理。

---

### 2. PDF入出力サービス層 (`PDFBinder.Core/Services`)

#### [MODIFY] [`PdfService.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfService.cs)
- **保存処理の改修 (`AppendPageToDocument` / `DrawInkStrokesOnPage`)**:
  - 本文コンテンツストリームへの焼き込み（`XGraphics.FromPdfPage(page, Append)`）を廃止。
  - ページ辞書内の既存の `/PdfBinderInk` 注釈をクリーンアップ（再保存時の蓄積防止）。
  - ストロークが存在する場合（`pageModel.InkStrokes.Count > 0`）：
    1. `pageModel.InkStrokes` を ISF (Ink Serialized Format) バイナリにシリアライズ。
    2. ページの `/Annots` 配下に注釈オブジェクト（`/Subtype /Ink` または `/Subtype /Stamp`）を生成。
    3. 他社リーダー閲覧用のアピアランスストリーム（`/AP /N` Form XObject）を生成し、ベクター描画命令を書き込む（印刷・拡大時の高精細ベクター表示を維持）。
    4. 注釈辞書に専用識別子と ISF バイナリ（`/PdfBinderInk`）を格納。
- **読み込み処理の改修 (`LoadDocumentAsync`)**:
  - ドキュメント読み込み時、各ページの `/Annots` を走査。
  - `/PdfBinderInk` を持つ注釈を検出した場合、ISF バイナリから `StrokeCollection` を復元して `pageModel.InkStrokes` に設定。

---

### 3. PDFレンダリングサービス層 (`PDFBinder.Core/Services`)

#### [MODIFY] [`PdfiumRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs)
- **他社注釈を維持しつつ PDF Binder 手書きを除外した背景レンダリング**:
  - `RenderPageAsync`:
    - 対象ページに `/PdfBinderInk` 注釈が含まれる場合、PDFium（Docnet）に渡す PDF バイト列から該当注釈のみをメモリ上で安全に除外。
    - Docnet の `pageReader.GetImage(RenderFlags.RenderAnnotations)` により、**他社製注釈（テキストコメント・付箋・ハイライト等）は 100% 描画** させつつ、**PDF Binder の手書きのみ背景から除外**。
    - 手書きが含まれない通常の PDF の場合は、余計なメモリコピーを行わず従来通りの最速パスで直接レンダリング。

---

### 4. 単体テスト層 (`PDFBinder.Tests`)

#### [NEW] [`ReEditableInkAnnotationTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ReEditableInkAnnotationTests.cs)
- 手書きストローク（ペン・太さ・色・筆圧・蛍光ペン）を含む PDF の保存 → `LoadDocumentAsync` による完全復元検証。
- 復元されたストロークの一部消去（消しゴム） → 上書き保存 → 再読み込みで消去が反映されていることの検証。
- 全消去時の注釈削除検証。
- 他社製注釈（リンクやテキスト注釈）が共存する PDF において、他社注釈と PDF Binder 手書きが両立して保持・分離されることの検証。

---

## 検証計画 (Verification Plan)

### 自動テスト (Automated Tests)
1. 新規単体テストの実行:
   ```pwsh
   dotnet test --filter "FullyQualifiedName~ReEditableInkAnnotationTests"
   ```
2. 既存の全テストスイートの回帰テスト実行:
   ```pwsh
   dotnet test
   ```
3. プロジェクト全体のビルド検証:
   ```pwsh
   dotnet build
   ```

### 手動検証 (Manual Verification)
1. アプリを起動し、PDF を開く。
2. ペンツール・蛍光ペン・直線・筆圧で文字や図形を書き込み、`Ctrl+S` で保存。
3. 一度アプリを閉じるか別ファイルを開いた後、再度保存した PDF を開く。
4. **検証項目**:
   - 開いた時点で手書き文字が綺麗に表示されていること。
   - 消しゴムツール（全体消し・部分消し）で手書き文字をなぞった際、背景に線が残らず綺麗に消去できること。
   - 追加で新しい線を書き込み、再度上書き保存できること。
   - 他の PDF リーダー（Microsoft Edge や Google Chrome）でその PDF を開いた際、手書き文字が崩れず綺麗に表示・印刷できること。
