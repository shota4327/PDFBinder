# 実装計画: Issue #208 ファイルを開いたとき、切り替えたときに強制終了してしまう問題の解消

## 1. 概要・背景
大容量PDFファイル（200〜300ページ以上）を開いた際や、別のファイルから200〜300ページ目を表示中のファイルへ切り替えた際に、アプリケーションがフリーズし、エラーダイアログの表示なく突然プロセスごと強制終了（クラッシュ）する問題が発生していました。

Windows イベントビューアーのログ解析により、クラッシュの根本原因は以下の通り特定されました:
- **直接のクラッシュ要因**: `Docnet.Core.Bindings.fpdf_view.FPDFDOC_ExitFormFillEnvironment`（`DocReader.Dispose()` 内）でのネイティブメモリアクセス違反（`AccessViolationException`）。
- **発生メカニズム**: `PdfiumRenderer.RenderPageAsync` および `ExtractInteractiveDataAsync` において、`_renderLock`（排他ロック）のスコープ外で `await Task.Run(..., cancellationToken)` を呼び出しているため、タスクキャンセル発生時に外側の待機が即座に中断されて排他ロックが解放され、**バックグラウンドスレッドで native PDFium が実行中であるにもかかわらず次のスレッドが native PDFium に同時進入**してネイティブメモリの二重解放・破損を引き起こしていた。
- **大容量ファイルでの誘発原因**: ドキュメント切り替え時に一度「1ページ目」として `InitializeDocument` が走って動的描画を開始し、直後に「200ページ目」へ切り替わって直前の描画がキャンセルされるという二重処理が発生しており、大容量ファイルでパイプラインのキャンセルが重なった際に100%競合が発生していた。
- **エクスプローラーからの起動タイムアウト**: `SingleInstanceManager.ConnectionTimeoutMs` が 800ms と極端に短いため、大容量ファイル読み込み中に別ファイルを開いた際にタイムアウトし二重起動を試みる問題があった。

---

## 2. 改修方針・設計

### 方針 1: `PdfiumRenderer` における排他ロック保持の完全化（最重要）
- 対象ファイル: `src/PDFBinder.Core/Services/PdfiumRenderer.cs`
- `RenderPageAsync` および `ExtractInteractiveDataAsync` において:
  - `Task.Run` に `cancellationToken` を渡して外側の `await` が先行キャンセルされる設計を廃止。
  - `await _renderLock.AcquireAsync(...)` で取得した `releaser` を `try-finally` で保護し、**`Task.Run` 内のバックグラウンド処理および `docReader.Dispose()` / `pageReader.Dispose()` が100%完了するまで `releaser.Dispose()` を絶対に呼ばない構造**に改修。
  - これにより、native PDFium への複数スレッド同時進入を原理的に不可能とし、`AccessViolationException` によるプロセス消滅を根本根絶する。

### 方針 2: ドキュメント切り替え時の初期ページ確定の適正化
- 対象ファイル: `src/PDFBinder.App/ViewModels/MainViewModel.cs`
- `OnActiveSessionChanged` において:
  - `Document = newValue.Document` による `OnDocumentChanged` で `InitializeDocument` が呼ばれる前に、切り替え先セッションの `CurrentPageNumber` に対応するページ（`preferredPage`）を特定。
  - `DetailEditor?.InitializeDocument(newValue.Document, preferredPage)` を呼び出すことで、最初から目的のページ（例: 200ページ目）で初期化を開始し、1ページ目の無駄なレンダリング開始と直後のキャンセル重複を排除する。

### 方針 3: IPC接続タイムアウト値の適正化
- 対象ファイル: `src/PDFBinder.App/Services/SingleInstanceManager.cs`
- `ConnectionTimeoutMs` を `800ms` から `5000ms`（5秒）に拡大。
- 既存プライマリインスタンスが大容量PDFの読み込みや解析中であっても、接続待機中にタイムアウトして開き失敗や二重起動が発生しないようにする。

---

## 3. 実装ステップ

### ステップ 1: `PdfiumRenderer.cs` の排他ロック完全化
1. `RenderPageAsync`:
   - `var releaser = await _renderLock.AcquireAsync(priority, cancellationToken).ConfigureAwait(false);`
   - `try ... finally { releaser.Dispose(); }` で `Task.Run` の完了までロックを確実に保持。
   - `Task.Run` 内部では `cancellationToken.ThrowIfCancellationRequested()` により早期中断しつつ、`using` によるリソース破棄を確実に完了させる。
2. `ExtractInteractiveDataAsync`:
   - 同様に `_renderLock` の解放タイミングを `Task.Run` 完了後まで厳格に保持。

### ステップ 2: `MainViewModel.cs` の初期表示ページ同期の適正化
1. `OnActiveSessionChanged` において、`newValue` が存在する場合に `newValue.Document.Pages.FirstOrDefault(p => p.PageNumber == newValue.CurrentPageNumber)` を算出して `DetailEditor?.InitializeDocument` に渡す。
2. `OnDocumentChanged` との重複呼び出しフローを整理し、無駄な再生成を排除。

### ステップ 3: `SingleInstanceManager.cs` のタイムアウト値適正化
1. `ConnectionTimeoutMs = 5000` に更新。

### ステップ 4: 単体テストの追加と動作検証
1. `tests/PDFBinder.Tests/PdfiumRendererTests.cs`:
   - キャンセルが連続発生しても排他ロックが正しく保たれ、別スレッドとの競合や例外が発生しないことを検証するテストを追加。
2. `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs` / `MainViewModelTests.cs`:
   - 複数ページ（数百ページ相当）を持つドキュメント間でセッション切り替えを行った際、指定ページで直接初期化され、クラッシュしないことを検証するテストを追加。
3. `dotnet test` および `dotnet build` による全テストの完全通過を確認。

---

## 4. 完了条件
1. `dotnet test`: 全単体テストが 100% PASS すること（既存600件＋新規テスト）。
2. `dotnet build`: 警告・エラーなくビルドが成功すること。
3. 大容量ドキュメントの切り替えおよび複数ファイルオープンでクラッシュしないこと。
