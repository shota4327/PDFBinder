# 実装計画: Issue #208 ファイル再オープン時のクラッシュ解消とフォーム環境初期化の完全回避 (Plan-3)

## 1. 概要
- **対象Issue**: #208「ファイルを開いたとき、切り替えたときに強制終了してしまう」
- **対象バージョン**: `0.11.3`
- **作業ブランチ**: `fix/issue-208-crash-on-switch-file`
- **目的**: 大容量ファイルを開いてタブを閉じ、再度同じファイルを開いた際に発生する強制終了を根本から解消する。

---

## 2. 根本原因の特定
1. **`RenderFlags.RenderAnnotations` による PDFium の FormFillEnvironment 生成・破棄ループ**:
   - `pageReader.GetImage(RenderFlags.RenderAnnotations)` を呼ぶと、PDFium はページごとに「対話型フォーム記入環境（`FPDFDOCInitFormFillEnvironment`）」を生成し、`DocReader.Dispose()` 時に `FPDFDOC_ExitFormFillEnvironment` で破棄する。
   - 大容量PDFで大量ページのレンダリングが連続すると、短時間で数百回のフォーム環境生成・破棄が繰り返され、PDFium のネイティブ内部ヒープが破損して `FPDFDOC_ExitFormFillEnvironment` でアクセス違反（`0xc0000005`）を引き起こす（PDFium の既知の不具合）。
   - 単なるページ表示・サムネイル生成には対話型フォーム記入環境は不要であり、`pageReader.GetImage()`（引数なし）を使用すれば、`FormFillEnvironment` の生成も破棄も一切行われないため、クラッシュ箇所を完全にバイパスできる。
2. **ファイルクローズ時の `DetailEditor` レンダリング未中断**:
   - `CloseDocumentAsync` でサムネイルタスクは中断されていたが、`DetailEditor` の動的レンダリングタスク（`CancelDynamicRender`）が中断されていなかったため、ドキュメント破棄後も旧ページの描画タスクが背後で残存していた。
3. **危険な非同期強制GC（`Task.Run(() => GC.Collect(...))`）**:
   - `CloseDocumentAsync` でバックグラウンドスレッドから強制 GC を走らせており、ワーカースレッドがネイティブ P/Invoke を実行している最中にメモリが移動してヒープ破壊を誘発していた。

---

## 3. 解決方針

1. **`PdfiumRenderer.cs`**:
   - `pageReader.GetImage(RenderFlags.RenderAnnotations)` を `pageReader.GetImage()`（引数なし / `RenderFlags.None`）に変更。
   - `FormFillEnvironment` の初期化および `FPDFDOC_ExitFormFillEnvironment` の破棄呼び出しを完全に回避し、ネイティブクラッシュを根絶。
2. **`MainViewModel.cs`**:
   - `CloseDocumentAsync` で `DetailEditor?.CancelDynamicRender()` を呼び出し、詳細エディタのレンダリングタスクを確実に即時中断。
   - `CloseDocumentAsync` 末尾の危険な `_ = Task.Run(() => GC.Collect(...))` を撤廃。

---

## 4. 変更対象ファイルと責務

| ファイル | 変更内容 |
| :--- | :--- |
| `src/PDFBinder.Core/Services/PdfiumRenderer.cs` | `GetImage(RenderFlags.RenderAnnotations)` を `GetImage()` に変更 |
| `src/PDFBinder.App/ViewModels/MainViewModel.cs` | `CloseDocumentAsync` での `CancelDynamicRender` 呼び出し追加、不要なバックグラウンド `GC.Collect` の撤廃 |
| `tests/PDFBinder.Tests/PdfiumRendererTests.cs` | `GetImage()` による正常描画と再オープン耐性の単体テスト確認 |

---

## 5. 検証手順
1. `dotnet build`: 0 警告、0 エラーでビルド成功を確認。
2. `dotnet test`: 全単体テストが 100% PASS することを確認。
3. 検証報告書（Walkthrough-3）の作成と永続保存。
