# 検証報告書 (Walkthrough) - Issue #219 1ページ表示ごとのPDFファイル重複オープン・解析の解消

## 1. 概要
- **対象Issue**: #219 [性能改善][影響度: 高] 1ページ表示ごとのPDFファイル重複オープン・解析（DocReader / PdfReader）の解消
- **作業ブランチ**: `feature/issue-219-eliminate-duplicate-pdf-open`
- **バージョン**: `0.13.0` -> `0.13.1`

---

## 2. 実施した変更内容

### 2.1 生リンク注釈モデルと抽出ヘルパーの追加
- `src/PDFBinder.Core/Models/PdfRawLinkAnnotation.cs`:
  - 元PDFポイント座標系（`RawPdfRect`, `PtWidth`, `PtHeight`）およびリンク種別・URL・遷移先ページ番号を保持する軽量なレコード `PdfRawLinkAnnotation` を新規作成。
- `src/PDFBinder.Core/Helpers/PdfLinkHelper.cs`:
  - `ExtractRawLinks`: PDFSharpのページから生リンク注釈を抽出。
  - `TransformRawLinks` / `TransformPdfPointRect`: ページの回転・表示寸法に応じたWPF表示座標系への純粋なインメモリ座標変換（アフィン変換）を実行。
  - 各メソッドを30〜50行以内で疎結合に集約。
- `src/PDFBinder.Core/Models/PdfPageModel.cs`:
  - `RawLinks`（`IReadOnlyList<PdfRawLinkAnnotation>`）プロパティを追加。
- `src/PDFBinder.Core/Services/PdfService.cs`:
  - `ParseDocumentModel` の初回ドキュメント走査ループ内で、各ページの生リンク情報を一括抽出して `pageModel.RawLinks` に設定。

### 2.2 レンダリングと文字抽出の統合（`RenderPageWithInteractiveDataAsync`）
- `src/PDFBinder.Core/Models/PageRenderResult.cs`:
  - `BitmapSource? Bitmap` と `PageInteractiveData InteractiveData` をカプセル化する一括結果モデルを新規作成。
- `src/PDFBinder.Core/Services/IPdfRenderer.cs`:
  - `RenderPageWithInteractiveDataAsync` および `RenderPagesBatchAsync` を追加（モック・テスト用の安全なフォールバック既定実装付き）。
- `src/PDFBinder.Core/Services/PdfiumRenderer.cs`:
  - `RenderPageWithInteractiveDataAsync` を実装。単一の `_renderLock` 取得および1回の `DocLib.Instance.GetDocReader` 呼び出し内で画像レンダリング（`GetImage`）と文字座標抽出（`GetCharacters`）を同時に実行し、完了直後に即時 `Dispose`。
  - リンク注釈は `cachedRawLinks`（またはファイル単位の `_rawLinksCache`）から純粋なインメモリ座標変換のみで生成し、ディスクからの `PdfSharp.PdfReader.Open` を完全排除。
  - `RenderPagesBatchAsync` を実装。同一PDFファイルの連続ページを単一のリーダーセッションでレンダリングし、完了直後に即時 `Dispose`。

### 2.3 詳細ビュー描画ロジックの最適化
- `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`:
  - `RenderPageItemAsync` において、`item.InteractiveData` 未取得時は `RenderPageWithInteractiveDataAsync` を呼び出して1回の処理で画像とインタラクティブデータを一括取得。
  - ズーム変更時などインタラクティブデータ取得済みの再描画時は、画像のみを再描画（`RenderPageAsync`）。

### 2.4 サムネイル一括生成のバッチ最適化
- `src/PDFBinder.App/ViewModels/MainViewModel.cs`:
  - `GenerateThumbnailsAsync` において、同一ファイルの連続ページ（最大10ページ単位）を `RenderPagesBatchAsync` によりバッチレンダリング。xref解析・ファイルオープン回数を最大90%削減。

---

## 3. 検証結果

### 3.1 単体テスト実行結果（`dotnet test`）
- テストスイート全件（637件）が 100% 成功。
  - 既存テスト: 633件 PASS
  - 新規追加テスト: 4件 PASS
    - `RenderPageWithInteractiveDataAsync_ValidPdf_ReturnsBitmapAndInteractiveDataSimultaneously`: 画像とインタラクティブデータが単一セッションで同時に取得され、完了後にファイルロックが残らないことを検証。
    - `RenderPageWithInteractiveDataAsync_WithCachedRawLinks_UsesProvidedLinksWithoutDiskParse`: 渡された生リンク情報から直接変換され、ディスク解析キャッシュが生成されないことを検証。
    - `RenderPagesBatchAsync_MultiplePages_RendersAllRequestedPagesAndReleasesLock`: 複数ページの一括レンダリングおよびファイルロック解放を検証。
    - `ExtractLinks_MultiplePagesWithoutCachedRawLinks_CachesLinksAndClearsOnInvalidate`: スタンドアロン呼び出し時のリンクキャッシュ動作および `InvalidateCache` による解放を検証。

```text
成功!   -失敗:     0、合格:   637、スキップ:     0、合計:   637、期間: 16 s - PDFBinder.Tests.dll (net10.0)
```

### 3.2 ビルド検証（`dotnet build`）
- 警告・エラー 0 件でビルド成功。
