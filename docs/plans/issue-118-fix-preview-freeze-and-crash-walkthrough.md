# Issue #118: ファイルオープン時および高速ページ切り替え時のプレビュー真っ白フリーズ・クラッシュ解消 検証報告 (Walkthrough)

## 概要
PDFファイルを開いた直後や、手書き詳細エディタでページを次々と高速に切り替えた際に、プレビューが真っ白のまま固まり（フリーズ）、アプリケーションが突然終了（クラッシュ）する問題を解消しました。

---

## 実施した変更内容

### 1. レンダリング排他・優先度制御（`PDFBinder.Core`）
- **[`RenderPriority.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/RenderPriority.cs)**:
  - レンダリング要求の優先度を表す列挙型（`High`: カレントページ表示、`Normal`: 印刷等、`Low`: サムネイル・先読み）を新設。
- **[`PriorityAsyncLock.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PriorityAsyncLock.cs)**:
  - PDFium（Docnet.Core）の非スレッドセーフなネイティブ呼び出し（`pdfium.dll`）を直列化する優先度付き非同期排他ロックを実装。
  - `High` 優先度の要求がキュー内の `Low` 優先度要求を追い越して最優先で実行される仕組みを導入。
  - CancellationToken の安全なキャンセル処理とリソース解放を保証。
- **[`IPdfRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/IPdfRenderer.cs)** / **[`PdfiumRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs)**:
  - `RenderPageAsync` および `ExtractInteractiveDataAsync` に `RenderPriority` を追加し、`PriorityAsyncLock` を経由して PDFium を安全に呼び出すよう保護。
  - 既存テスト・モック実装を破壊しない後方互換インターフェイスメソッドを提供。

### 2. プレビュー表示・デバウンス・仮プレビュー（`PDFBinder.App`）
- **[`DetailEditorView.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
  - ページ描画コンテナにおいて、高解像度レンダリング画像（`PageBackground`）の下層にサムネイル画像（`Page.Thumbnail`）を配置。
  - ページ切り替え直後にサムネイルを即座に拡大表示し、高解像度レンダリングが完了した瞬間にシームレスに差し替えることで、白紙表示（真っ白な画面）を完全に排除。
- **[`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)**:
  - ページ切り替え（`OnCurrentPageChanged`）時に 75ms のデバウンス（`PageSwitchDebounceDelayMs`）を導入。矢印キーやマウスでの高速切り替え時に中間ページの過剰なレンダリングを自動スキップ。
  - カレントページの描画には `RenderPriority.High`、先読み・事前ロードには `RenderPriority.Low` を割り当て。
  - レンダリング処理のメソッド分割（`PerformDynamicRenderAsync` / `RenderPageItemAsync`）により30〜50行ルールを遵守。
  - `_renderCts` の安全なキャンセル処理。
- **[`MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)**:
  - サムネイル生成処理（`UpdatePageThumbnailAsync`）の優先度を `RenderPriority.Low` に設定。
  - 印刷処理（`RenderPageWithInkAsync`）の優先度を `RenderPriority.Normal` に設定。

### 3. ドキュメント同期
- **[`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md)**: `IPdfRenderer` の優先度パラメータおよび `PriorityAsyncLock` による排他制御仕様を反映。
- **[`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md)**: 機能インベントリに **F59** を追加し、単体テスト数を更新（312件全PASS）。

---

## 検証結果

### 1. 単体テスト（`dotnet test`）
新規テスト 7 件を含む全 312 件のテストが 100% 成功しました。
- `PriorityAsyncLockTests.AcquireAsync_SingleThread_LocksAndReleasesCorrectly`: PASS
- `PriorityAsyncLockTests.AcquireAsync_PriorityOrdering_HighAcquiresBeforeLow`: PASS
- `PriorityAsyncLockTests.AcquireAsync_CancelledRequest_DoesNotBlockSubsequentRequests`: PASS
- `PriorityAsyncLockTests.AcquireAsync_AlreadyCancelledToken_ThrowsImmediately`: PASS
- `PdfiumRendererTests.RenderPageAsync_ConcurrentCalls_ExecuteSafelyWithoutCrashing`: 10並行スレッドからの同時呼び出しでクラッシュ・例外なく全ビットマップ生成に成功 (PASS)
- `DetailEditorViewModelTests.OnCurrentPageChanged_RapidPageSwitch_DebouncesSafelyWithoutExceptions`: 高速切り替え時の安全なデバウンスと描画完了を検証 (PASS)
- `DetailEditorViewModelTests.RenderPageItemAsync_CurrentPageUsesHighPriority`: カレントページに高優先度が指定されることを検証 (PASS)

```text
成功!   -失敗:     0、合格:   312、スキップ:     0、合計:   312、期間: 4 s - PDFBinder.Tests.dll (net10.0)
```

### 2. ビルド（`dotnet build`）
警告およびエラー 0 件でビルドが正常に完了することを確認。
```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```
