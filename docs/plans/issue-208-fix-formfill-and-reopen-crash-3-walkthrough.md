# 検証報告書: Issue #208 ファイル再オープン時のクラッシュ解消とフォーム環境初期化の完全回避 (Walkthrough-3)

## 1. 概要
- **対象Issue**: #208「ファイルを開いたとき、切り替えたときに強制終了してしまう」
- **対応バージョン**: `0.11.3`
- **作業ブランチ**: `fix/issue-208-crash-on-switch-file`
- **目的**: 大容量ファイルを開いてタブから閉じ、再度同じファイルを開いた際に発生していた強制終了（`AccessViolationException`）の根本原因を特定し、PDFium のフォーム記入環境（FormFillEnvironment）の不要な生成・破棄ループを完全バイパスすることでクラッシュを根絶した。

---

## 2. 根本原因の特定と対策

### 2.1 原因の特定
1. **`RenderFlags.RenderAnnotations` に伴う PDFium の FormFillEnvironment 生成・破棄ループ**:
   - `pageReader.GetImage(RenderFlags.RenderAnnotations)` を呼び出すと、Docnet.Core は内部で `DocumentWrapper.GetFormHandle` を呼び出し、PDFium の対話型フォーム記入環境（`FPDFDOCInitFormFillEnvironment`）を生成していた。
   - そして `docReader.Dispose()` 時に `FPDFDOC_ExitFormFillEnvironment` でこれを破棄していた。
   - 大容量PDFで大量ページのレンダリングが連続すると、短時間で数百回ものフォーム環境生成・破棄が繰り返され、PDFium のネイティブ内部ヒープが破損して `FPDFDOC_ExitFormFillEnvironment` でアクセス違反（`0xc0000005`）を引き起こす（PDFium の既知の不具合）。
   - 単なるページ表示・サムネイル生成には対話型フォーム記入環境は不要であり、`pageReader.GetImage()`（引数なし）を使用すれば、`FormFillEnvironment` の生成も破棄も一切行われない。
2. **ファイルクローズ時の `DetailEditor` レンダリング未中断**:
   - `CloseDocumentAsync` でサムネイルタスクは中断されていたが、`DetailEditor` の動的レンダリングタスク（`CancelDynamicRender`）が中断されていなかったため、旧ページの描画タスクが背後で残存していた。
3. **危険な非同期強制GC（`Task.Run(() => GC.Collect(...))`）**:
   - `CloseDocumentAsync` でバックグラウンドスレッドから強制 GC を走らせており、ワーカースレッドがネイティブ P/Invoke を実行している最中にメモリが移動してヒープ破壊を誘発していた。

### 2.2 対策と実装
1. **`PdfiumRenderer.cs`**:
   - `pageReader.GetImage(RenderFlags.RenderAnnotations)` を `pageReader.GetImage()`（引数なし / `RenderFlags.None`）に変更。
   - `FormFillEnvironment` の初期化および `FPDFDOC_ExitFormFillEnvironment` の破棄呼び出しを完全に回避し、ネイティブクラッシュを根絶。
2. **`MainViewModel.cs`**:
   - `CloseDocumentAsync` で `DetailEditor?.CancelDynamicRender()` を呼び出し、詳細エディタのレンダリングタスクを確実に即時中断。
   - `CloseDocumentAsync` 末尾の危険な `_ = Task.Run(() => GC.Collect(...))` を撤廃。

---

## 3. テストと検証結果

### 3.1 単体テストの追加
`tests/PDFBinder.Tests/ViewModels/MainViewModelMultiFileTests.cs` に以下のテストケースを追加：
- `OpenCloseAndReopenSameDocument_CancelsDetailRenderAndReopensSafely`:
  300ページの大容量ドキュメントをオープンし、タブを閉じた直後に再度同じドキュメントを再オープンしても、例外なく安全に復元されることを検証。

### 3.2 テストスイート実行結果
```text
Passed!  - Failed: 0, Passed: 609, Skipped: 0, Total: 609, Duration: 7 s
```
全 609 件の単体テストがすべて PASS し、回帰不具合がないことを確認しました。

### 3.3 ビルド検証結果
```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```
0 警告、0 エラーで正常にビルドが完了することを確認しました。
