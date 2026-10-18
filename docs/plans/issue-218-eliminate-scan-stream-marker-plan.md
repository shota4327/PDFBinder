# Issue #218: PDFオープン直後の重複ファイル全量読み込み＆全バイト走査（ScanStreamForMarker）の排除 実装計画

## 1. 概要
- **Issue 番号**: #218
- **タスク名**: eliminate-scan-stream-marker
- **目的**: 
  PDFファイルを開いた直後の初回ページ描画時に、すでに `PdfService.LoadDocumentAsync` で解析済みであるにもかかわらず、`PdfiumRenderer.RenderPageAsync` 内で再びファイル全体をディスクから走査・再読み込みしている問題（最大3回のフルリードおよびディスク全量走査）を解消し、特に大容量PDFを開いた際の1ページ目表示までの時間を大幅に短縮する。

---

## 2. 現状の課題とボトルネック分析
1. **1回目のフルリード（`PdfService.LoadDocumentAsync`）**:
   `File.ReadAllBytesAsync(filePath)` でPDFファイル全体をメモリに読み込み、`PdfSharp`（`PdfReader.Open`）で全ページの `/Annots` を検査して、各ページの `pageModel.HasSourceBinderInkAnnotation` およびドキュメント全体の `model.HasBinderInkAnnotations` を **この時点で既に正確に判定済み** である。
2. **2回目のフルリード＆ディスク全量走査（`PdfiumRenderer.HasBinderInkAnnotationMarker` -> `ScanStreamForMarker`）**:
   その直後、1ページ目を描画する `PdfiumRenderer.RenderPageAsync` において、排他ロック（`_renderLock`）を保持したまま `HasBinderInkAnnotationMarker(filePath)` が呼ばれる。
   このメソッドはキャッシュミス時に `FileStream` を開き、`ScanStreamForMarker` によって `/PdfBinderInk` のバイト列を探すために **ディスク上のPDFファイルを先頭から末尾まで64KBチャンクで全量スキャン** する。
   - **特に深刻な影響**: PDF Binderの独自インクを含まない一般的な大容量PDF（例: 100MB〜500MBのスキャンPDFや図面PDF）を開いた場合、マーカーが存在しないことを確認するために **ファイル末尾まで数百MBすべてを読み切るまで1ページ目の描画が始まらず**、`_renderLock` を長時間ブロックする。
3. **3回目のフルリード（`PdfiumRenderer.GetRenderBytes`）**:
   もし `/PdfBinderInk` が存在した場合、`GetRenderBytes` 内で再び `File.ReadAllBytes(filePath)` が実行され、`PdfSharp` で再度ドキュメントを開いてアノテーション除去・メモリストリーム書き出し（`RemoveBinderInkAnnotationsIfPresent`）が行われる。

---

## 3. 設計方針とアーキテクチャ

### 3.1 `PdfBinderInkAnnotation` へのクリーンバイト列除去メソッド共通化
- `PdfiumRenderer` にプライベート実装されていた `RemoveBinderInkAnnotationsIfPresent` を `PdfBinderInkAnnotation.RemoveBinderInkAnnotationsIfPresent(byte[] pdfBytes, string? password = null)` として共通化・公開。
- パスワード保護されたPDFにも対応する。

### 3.2 `PdfDocumentModel` に描画用クリーンバイト列プロパティを追加
- `PdfDocumentModel` に `public byte[]? CleanRenderBytes { get; set; }` を追加。
- `model.HasBinderInkAnnotations == true` の場合のみ生成・保持し、通常のPDF（`false`）の場合は `null` のままとすることでメモリ消費をゼロに抑える。

### 3.3 `IPdfRenderer` / `PdfiumRenderer` への事前登録インターフェースの追加
- `IPdfRenderer` に以下を追加：
  ```csharp
  /// <summary>
  /// 指定されたPDFファイルの自前手書き注釈マーカー（/PdfBinderInk）の有無および描画用クリーンバイト列をキャッシュに事前登録します。
  /// 事前登録することで、レンダリング時のファイル全量バイト走査（ScanStreamForMarker）および再読み込みを排除します。
  /// </summary>
  void RegisterBinderInkMarker(string filePath, bool hasMarker, byte[]? cleanRenderBytes = null);
  ```
- `PdfiumRenderer` の実装：
  - `_hasBinderInkMarkerCache[filePath] = (lastWrite, hasMarker);`
  - `hasMarker && cleanRenderBytes != null` の場合、`_fileBytesCache[filePath] = (lastWrite, cleanRenderBytes);`
  - 事前登録がある場合、`HasBinderInkAnnotationMarker` は即座にキャッシュを返し、`ScanStreamForMarker` は完全にバイパスされる。

### 3.4 `PdfService` での事前登録とクリーンバイト列生成
- `PdfService` のコンストラクタでオプショナルに `IPdfRenderer?` を受け取れるようにする。
- `ParseDocumentModel`:
  - `model.HasBinderInkAnnotations` が `true` の場合、メモリにある `fileBytes` から `PdfBinderInkAnnotation.RemoveBinderInkAnnotationsIfPresent(fileBytes, password)` で `CleanRenderBytes` を生成。
- `LoadDocumentAsync`:
  - パース完了後、`_pdfRenderer?.RegisterBinderInkMarker(model.FilePath, model.HasBinderInkAnnotations, model.CleanRenderBytes);` を実行。

### 3.5 `MainViewModel` での連携
- コンストラクタで `_pdfService = pdfService ?? new PdfService(_pdfRenderer);` として連携。
- `OpenSingleDocumentAsync` / `AppendDocumentAsync` / `SaveDocumentSessionAsync` で明示的に `_pdfRenderer.RegisterBinderInkMarker(...)` を呼び出し、ファイル更新・保存後も最新キャッシュ状態を維持。

---

## 4. 変更対象ファイル一覧
1. `src/PDFBinder.Core/Models/PdfBinderInkAnnotation.cs` [MODIFY]
2. `src/PDFBinder.Core/Models/PdfDocumentModel.cs` [MODIFY]
3. `src/PDFBinder.Core/Services/IPdfRenderer.cs` [MODIFY]
4. `src/PDFBinder.Core/Services/PdfiumRenderer.cs` [MODIFY]
5. `src/PDFBinder.Core/Services/PdfService.cs` [MODIFY]
6. `src/PDFBinder.App/ViewModels/MainViewModel.cs` [MODIFY]
7. `tests/PDFBinder.Tests/PdfiumRendererTests.cs` [MODIFY]

---

## 5. 検証手順
1. **単体テストの実行・追加**:
   - `RegisterBinderInkMarker` でマーカーなし（`hasMarker = false`）が登録された場合、`ScanStreamForMarker` が走らず、`CachedMarkerCount` にキャッシュされ、レンダリングが即座に成功すること。
   - `hasMarker = true` かつ `cleanRenderBytes` が登録された場合、`GetRenderBytes` によるディスク再読み込みが発生せず、登録されたバイト列でレンダリングされること。
   - `PdfService.LoadDocumentAsync` を通して読み込んだ際に、`IPdfRenderer` に自動的にマーカー情報が登録されること。
   - パスワード保護PDFや手書き注釈付きPDFの既存テストがすべて正常に通過すること。
2. **全体ビルド＆テスト検証**:
   - `dotnet test`: 全単体テストが成功すること。
   - `dotnet build`: 警告・エラーなくビルドが成功すること。
