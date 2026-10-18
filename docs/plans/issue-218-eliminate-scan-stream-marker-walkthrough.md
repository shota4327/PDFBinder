# Issue #218: PDFオープン直後の重複ファイル全量読み込み＆全バイト走査（ScanStreamForMarker）の排除 検証報告（Walkthrough）

## 1. 概要
- **Issue 番号**: #218
- **タスク名**: eliminate-scan-stream-marker
- **対応バージョン**: v0.13.2
- **目的**: 
  PDFファイルを開いた直後の初回ページ描画時に、すでに `PdfService.LoadDocumentAsync` で解析済みであるにもかかわらず、`PdfiumRenderer.RenderPageAsync` 内で再びファイル全体をディスクから走査・再読み込みしている問題（最大3回のフルリードおよびディスク全量走査）を解消し、特に大容量PDFを開いた際の1ページ目表示までの時間を大幅に短縮する。

---

## 2. 実施した主な改修内容

### 2.1 `PdfBinderInkAnnotation` へのクリーンバイト列除去メソッド共通化
- [`src/PDFBinder.Core/Models/PdfBinderInkAnnotation.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PdfBinderInkAnnotation.cs)
  - `RemoveBinderInkAnnotationsIfPresent(byte[] pdfBytes, string? password = null)` を追加。
  - `PdfiumRenderer` に閉じていた注釈除去処理を共通化し、パスワード付きPDFにも対応。自前手書き注釈が存在しない場合は再シリアライズ（`doc.Save`）をスキップして元のバイト列をそのまま返却するゼロコスト設計。

### 2.2 `PdfDocumentModel` へのクリーンバイト列プロパティの追加
- [`src/PDFBinder.Core/Models/PdfDocumentModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PdfDocumentModel.cs)
  - `CleanRenderBytes` (`byte[]?`) プロパティを追加。
  - `HasBinderInkAnnotations == true` の場合のみ生成・保持し、通常のPDF（`false`）の場合は `null` のままとすることで、メモリを一切浪費しない設計。

### 2.3 `IPdfRenderer` / `PdfiumRenderer` への事前登録インターフェースの追加
- [`src/PDFBinder.Core/Services/IPdfRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/IPdfRenderer.cs)
- [`src/PDFBinder.Core/Services/PdfiumRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs)
  - `RegisterBinderInkMarker(string filePath, bool hasMarker, byte[]? cleanRenderBytes = null)` メソッドを追加。
  - `_hasBinderInkMarkerCache[filePath]` に判定結果を登録し、マーカーあり時は `_fileBytesCache[filePath]` にクリーンバイト列を直接登録。
  - 事前登録が存在する場合、レンダリング排他ロック（`_renderLock`）内でのディスク全量バイト走査（`ScanStreamForMarker`）および重複した `File.ReadAllBytes` を100%バイパス。

### 2.4 `PdfService` での自動事前登録とクリーンバイト列生成
- [`src/PDFBinder.Core/Services/PdfService.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfService.cs)
  - `IPdfRenderer?` を受け取るコンストラクタを追加。
  - `ParseDocumentModel` で各ページの注釈走査完了後、もし `HasBinderInkAnnotations` が `true` の場合はメモリ上の `fileBytes` から `PdfBinderInkAnnotation.RemoveBinderInkAnnotationsIfPresent` を用いて `CleanRenderBytes` を生成。
  - `LoadDocumentAsync` 完了時に `_pdfRenderer?.RegisterBinderInkMarker(model.FilePath, model.HasBinderInkAnnotations, model.CleanRenderBytes)` を自動実行。

### 2.5 `MainViewModel` での連携と保存時同期
- [`src/PDFBinder.App/ViewModels/MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
  - コンストラクタで `_pdfService = pdfService ?? new PdfService(_pdfRenderer);` として初期化順序を連携。
  - `OpenSingleDocumentAsync` でドキュメント読み込み直後に `_pdfRenderer.RegisterBinderInkMarker` を実行。
  - `SaveDocumentSessionAsync` でPDF保存成功後、保存されたインク状態を反映して `_pdfRenderer.RegisterBinderInkMarker(targetPath, hasInk)` を実行し、保存直後の再描画でも全量スキャンを防止。

---

## 3. 検証結果

### 3.1 単体テスト実行結果 (`dotnet test`)
追加した4件のテストを含む、**全642件の単体テストがすべて合格（100% PASS）** することを確認しました。
- `RegisterBinderInkMarker_WithNoMarker_CachesFalseAndRendersSuccessfully`: マーカーなしの事前登録により走査をスキップして正常描画
- `RegisterBinderInkMarker_WithMarkerAndCleanBytes_CachesCleanBytes`: マーカーあり＆クリーンバイト列登録によりディスク再読み込みをスキップして描画
- `PdfService_LoadDocumentAsync_WithPdfRenderer_RegistersMarkerAutomatically`: `PdfService` 経由のドキュメントオープンで自動的にマーカー情報が登録されること
- `PdfBinderInkAnnotation_RemoveBinderInkAnnotationsIfPresent_CleansAnnotationCorrectly`: 手書き注釈除去および未存在時の同一バイト列返却検証
- `LoadDocumentAsync_WithInkStrokesAndRenderer_SetsCleanRenderBytesAndRegistersMarker`: 手書き注釈付きPDFのロード時にクリーンバイト列生成とレンダラーへの事前登録が連動すること

```text
成功!   -失敗:     0、合格:   642、スキップ:     0、合計:   642、期間: 6 s - PDFBinder.Tests.dll (net10.0)
```

### 3.2 ビルド検証結果 (`dotnet build`)
```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```
