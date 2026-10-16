# 実装計画 - Issue #219 1ページ表示ごとのPDFファイル重複オープン・解析（DocReader / PdfReader）の解消

## 1. 概要
- **対象Issue**: #219 [性能改善][影響度: 高] 1ページ表示ごとのPDFファイル重複オープン・解析（DocReader / PdfReader）の解消
- **目的**: 
  1ページの描画およびインタラクティブデータ（テキスト文字座標・リンク注釈）取得において、同一PDFファイルを最大3回オープン（PDFiumで2回、PdfSharpで1回）し排他ロックを2回取得している非効率を解消し、詳細ビューのページめくり・初期表示およびサムネイル生成を高速化する。

---

## 2. 課題の再確認と他箇所への影響分析

### 2.1 現状の課題再確認
1. **詳細ビューでの3重オープン**:
   - `DetailEditorViewModel.RenderPageItemAsync` において、1ページを描画するたびに以下が直列実行されている。
     - 1回目: `RenderPageAsync` -> `_renderLock` 取得 -> `DocLib.GetDocReader`（xrefパース） -> 画像生成 -> `Dispose` -> ロック解放
     - 2回目: `ExtractInteractiveDataAsync` -> `_renderLock` 再取得 -> `DocLib.GetDocReader`（xrefパース） -> `GetCharacters` -> `Dispose`
     - 3回目: `ExtractLinks` -> `PdfSharp.PdfReader.Open`（ファイル全読み込み・xrefパース・全オブジェクト構築） -> リンク抽出 -> `Dispose` -> ロック解放
2. **サムネイル生成時の毎回オープン**:
   - `GenerateThumbnailsAsync` で複数ページを順次生成する際も、1ページごとに `DocLib.GetDocReader` の生成と破棄がページ数分繰り返される。

### 2.2 他の箇所・システム要件への影響分析と対策
1. **ファイルロック回避原則（上書き保存）への影響**:
   - **制約**: PDF Binder では「開いているPDFファイルをロックせず、安全な上書き保存（`File.Replace`）を実現する」ことが絶対原則。
   - **分析**: `IDocReader` をアプリ起動中やビュー表示中に恒常的にキャッシュ（開きっぱなし）すると、Windows OS レベルでファイルハンドルがロックされ、上書き保存時に `IOException` が発生するリスクがある。
   - **対策**: `IDocReader` の恒常キャッシュは行わず、**「1回の描画リクエスト（または10ページ程度のサムネイルバッチ）の中で画像と文字抽出を同一セッションで行い、完了直後に即時 `Dispose` する」** 設計とする。これによりファイルロックの完全回避を100%維持する。
2. **リンク注釈・文字選択機能への影響**:
   - **分析**: 初回ドキュメント読み込み時（`PdfService.ParseDocumentModel`）に `PdfSharp` は既にファイル全体をメモリ上に展開・解析している。
   - **対策**: 初回ロード時に各ページのリンク情報（ポイント単位の生矩形、リンク種別、URL、遷移先ページ番号）を解析して `PdfPageModel.RawLinks`（および `PdfiumRenderer` の内部キャッシュ）に保持する。ページ描画時は純粋な座標変換（アフィン変換）のみを行うため、I/O不要かつリンク機能・文字選択機能の精度と動作を完全互換で維持する。
3. **メモリ消費への影響**:
   - 保持するのはリンクの矩形数値（`Rect`）とURL文字列／ページインデックスのみ（1ページあたり数十バイト程度）。数千ページのドキュメントでも数KB〜数十KB程度であり、メモリ消費への悪影響は皆無。

---

## 3. 実装計画・設計

### Step 1: リンク情報モデルと初回ロード時抽出
- `src/PDFBinder.Core/Models/PdfRawLinkAnnotation.cs`:
  - 元PDF座標系（ポイント単位）のリンク情報レコード `PdfRawLinkAnnotation`（`Rect RawPdfRect`, `PdfLinkType LinkType`, `string? Uri`, `int TargetPageIndex`）を定義。
- `src/PDFBinder.Core/Models/PdfPageModel.cs`:
  - `public IReadOnlyList<PdfRawLinkAnnotation> RawLinks { get; set; } = Array.Empty<PdfRawLinkAnnotation>();` プロパティを追加。
- `src/PDFBinder.Core/Services/PdfService.cs`:
  - `ParseDocumentModel` のページ走査ループ内で、各ページの `/Link` 注釈を解析して `pageModel.RawLinks` に格納。
- `src/PDFBinder.Core/Services/PdfiumRenderer.cs`:
  - スタンドアロン呼び出し用の `_rawLinksCache`（`ConcurrentDictionary<string, (DateTime, Dictionary<int, List<PdfRawLinkAnnotation>>)>`）を追加し、`ExtractLinks` でも一度解析したリンク情報を再利用して重複オープンを排除。

### Step 2: レンダリングと文字抽出の統合（`RenderPageWithInteractiveDataAsync`）
- `src/PDFBinder.Core/Services/IPdfRenderer.cs`:
  - 画像レンダリングとインタラクティブデータ抽出を一度の排他ロック・同一 `IDocReader` セッションで実行するメソッド `RenderPageWithInteractiveDataAsync` を追加。
  ```csharp
  Task<PageRenderResult> RenderPageWithInteractiveDataAsync(
      string? filePath,
      int pageIndex,
      int targetWidth,
      int targetHeight,
      double displayWidth,
      double displayHeight,
      PageRotation rotation,
      CancellationToken cancellationToken,
      RenderPriority priority = RenderPriority.Normal,
      string? password = null,
      IReadOnlyList<PdfRawLinkAnnotation>? cachedRawLinks = null);
  ```
  - 戻り値レコード `PageRenderResult(BitmapSource? Bitmap, PageInteractiveData InteractiveData)` を定義。
- `src/PDFBinder.Core/Services/PdfiumRenderer.cs`:
  - `RenderPageWithInteractiveDataAsync` を実装。
  - `_renderLock` を1回だけ取得。
  - `DocLib.Instance.GetDocReader` を1回だけ呼び出し。
  - `docReader.GetPageReader(pageIndex)` から `pageReader.GetImage()` と `pageReader.GetCharacters()` を取得。
  - `pageReader` および `docReader` を直ちに `Dispose`。
  - `_renderLock` を解放。
  - 文字座標とリンク座標を表示寸法・回転に合わせて変換し、`PageRenderResult` を返却。

### Step 3: 詳細ビュー描画ロジック（`DetailEditorViewModel`）の最適化
- `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`:
  - `RenderPageItemAsync` において、`item.InteractiveData == null || item.InteractiveData.Rotation != item.Page.RenderRotation` の場合は `RenderPageWithInteractiveDataAsync` を呼び出して1回の処理で背景画像とインタラクティブデータの両方を一括取得。
  - ズーム変更時（`InteractiveData` 取得済み）は従来の `RenderPageAsync`（画像描画のみ）を呼び出し。
  - 先読みページ（`RenderSecondaryNeighborPagesAsync`）では、不可視ページのインタラクティブデータ抽出をスキップし、メイン表示処理の待機時間を最小化。

### Step 4: サムネイル生成（`MainViewModel`）のバッチ最適化
- `src/PDFBinder.Core/Services/IPdfRenderer.cs` & `PdfiumRenderer.cs`:
  - 同一ファイル・同一寸法向けのバッチレンダリングメソッド `RenderPageBatchAsync`（10ページ程度のチャンクで `IDocReader` を再利用）を追加。
- `src/PDFBinder.App/ViewModels/MainViewModel.cs`:
  - サムネイル生成時に同一ファイルの連続ページをチャンク単位で一括レンダリングし、xref再パース回数を大幅に削減。

### Step 5: 単体テストの追加と回帰検証
- `tests/PDFBinder.Tests/PdfiumRendererTests.cs`:
  - `RenderPageWithInteractiveDataAsync` の単体テスト（画像と文字座標・リンクが正常に同時取得できること）。
  - `_rawLinksCache` および `RawLinks` による `PdfReader.Open` 重複呼び出し排除の検証テスト。
- `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs`:
  - 詳細ビューの初期レンダリングおよびページ切り替え時に `InteractiveData` が正常に設定されることの検証。
- 全単体テストの実行（`dotnet test`）およびビルド検証（`dotnet build`）。

---

## 4. 期待される効果
1. 詳細ビューでの1ページ表示あたりのファイルオープン数が 3回 -> 1回（66%削減）、排他ロック取得数が 2回 -> 1回（50%削減）。
2. `PdfSharp.PdfReader.Open` によるディスクからのPDF再パースがページ表示ごとに発生していたのを完全にゼロ（0回）に排除。
3. サムネイル一括生成時のファイルオープン・xrefパース回数が最大90%削減。
4. ファイルロックを保持しないため、既存の上書き保存・別名保存・安全性は完全に保証。
