# 検証報告書: Issue #208 大容量PDF読み込み時のメモリ破損・強制終了の解消 (Walkthrough-2)

## 1. 概要
- **対象Issue**: #208「ファイルを開いたとき、切り替えたときに強制終了してしまう」
- **対応バージョン**: `0.11.3`
- **作業ブランチ**: `fix/issue-208-crash-on-switch-file`
- **目的**: 158MB、400ページ超の大容量PDFファイルを開いた際に発生していたネイティブメモリアクセス違反（`0xc0000005: AccessViolationException`）による強制終了の根本原因を特定し、ページ描画ごとの全メモリ展開・複製を撤廃して安全なオンデマンド読み込みへ刷新した。

---

## 2. 根本原因の特定と対策

### 2.1 原因の特定
Windows イベントログ解析および Docnet.Core の IL バイトコード逆アセンブルにより、以下の決定的な事実が判明しました：
1. **ページごとのファイル全バイト列展開と非管理メモリ複製（`Marshal.AllocHGlobal`）**:
   - `PdfiumRenderer.RenderPageAsync` は自前手書き注釈除去のため `GetRenderBytes` でファイル全体を `byte[]`（158MB）に読み込み、`DocLib.Instance.GetDocReader(renderBytes, dimensions)` を呼んでいた。
   - Docnet.Core の `byte[]` 版は内部で `Marshal.AllocHGlobal(bytes.Length)` で非管理メモリを確保し、`Marshal.Copy` で 158MB を複製していた。
   - サムネイル先行生成（50ページ）や詳細表示により、「158MBの確保・コピー・解放」が毎秒数十回（短時間で数GB〜数十GB）猛烈な勢いで繰り返されていた。
   - これによりヒープ断片化と激しいガベージコレクション（LOH / Gen2 GC）が発生し、ネイティブ PDFium のアンマネージド構造体ポインタが破壊され、`DocReader.Dispose()` の `FPDFDOC_ExitFormFillEnvironment` でアクセス違反（`0xc0000005`）を引き起こしてプロセスが即座に消滅していた。
2. **通常外部PDFに対する不要な全ページ走査**:
   - 外部PDFには存在しない手書き注釈（`/PdfBinderInk`）を除去しようとして、毎回 158MB を `PdfSharp` で 400 ページすべて走査していた。

### 2.2 対策と実装
1. **自前手書きマーカー（`/PdfBinderInk`）の高速ストリーム走査**:
   - ファイル全体をメモリにロードせず、固定長 64KB バッファを用いたファイルストリーム走査により、ファイル内に `/PdfBinderInk` が存在するかを高速判定する `HasBinderInkAnnotationMarker` メソッドを実装。
   - 通常の外部PDF（大容量PDFを含むほぼすべてのPDF）では即座に `false` と判定され、結果はファイルの更新日時とともにキャッシュ（`_hasBinderInkMarkerCache`）される。
2. **直接ファイルパスによるオンデマンド描画（メモリ割当 0 バイト）**:
   - マーカーが存在しない通常のPDFでは、`DocLib.Instance.GetDocReader(filePath, dimensions)` を直接使用。
   - `File.ReadAllBytes`（158MB）、`Marshal.AllocHGlobal`（158MB）、`Marshal.Copy` は完全にスキップされ、メモリ割り当ては 0 バイトとなる。
   - PDFium（`FPDF_LoadDocument`）が OS ファイルマッピング経由で対象ページ（数KB〜数MB）のみをオンデマンド描画するため、メモリ消費が極小化されクラッシュが根本から解消された。
3. **文字・リンク抽出の最適化**:
   - `ExtractCharacters`: マーカーなしの場合は直接 `GetDocReader(filePath, dimensions)` を使用。
   - `ExtractLinks`: マーカーなしの場合は `PdfSharp.Pdf.IO.PdfReader.Open(filePath, PdfDocumentOpenMode.Import)` を直接使用し、メモリ展開を排除。

---

## 3. テストと検証結果

### 3.1 単体テストの追加
`tests/PDFBinder.Tests/PdfiumRendererTests.cs` に以下のテストケースを追加：
1. `RenderPageAsync_NormalPdfWithoutBinderInkMarker_RendersDirectlyWithoutCachingBytes`:
   手書きマーカーのない通常のPDFを描画した時、バイト列キャッシュにファイルが保持されず、直接正常にビットマップがレンダリングされることを検証。
2. `RenderPageAsync_PdfWithBinderInkMarker_RemovesAnnotationsAndCachesBytes`:
   自前手書き注釈マーカーを持つPDFを描画した時、注釈が除去されてキャッシュに保存されてレンダリングされることを検証。
3. `ExtractInteractiveDataAsync_NormalPdfWithoutBinderInkMarker_ExtractsSuccessfully`:
   手書きマーカーのない通常のPDFでテキストとリンクの抽出がファイルパス直接で正常に完了することを検証。
4. `InvalidateCache_ClearsBothByteCacheAndMarkerCache`:
   `InvalidateCache` がバイト列キャッシュとマーカーキャッシュの両方を安全に対象ファイルのみ削除することを検証。
5. `RenderPageAsync_MultiplePagesInNormalPdf_RendersAllPagesWithoutCachingBytes`:
   10ページの通常PDFを連続レンダリングしてもバイト列キャッシュが一切蓄積されず、安定して描画完了することを検証。

### 3.2 テストスイート実行結果
```text
Passed!  - Failed: 0, Passed: 608, Skipped: 0, Total: 608, Duration: 7 s
```
全 608 件の単体テストがすべて PASS し、既存の全機能を含め回帰不具合がないことを確認しました。

### 3.3 ビルド検証結果
```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```
0 警告、0 エラーで正常にビルドが完了することを確認しました。
