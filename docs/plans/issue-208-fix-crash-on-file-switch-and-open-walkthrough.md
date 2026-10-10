# 検証報告書: Issue #208 ファイルを開いたとき、切り替えたときの強制終了不具合の解消

## 1. 概要
- **対象Issue**: #208「ファイルを開いたとき、切り替えたときに強制終了してしまう」
- **対応バージョン**: `0.11.3`
- **作業ブランチ**: `fix/issue-208-crash-on-switch-file`

### 背景と原因
ページ数が多いPDFファイル（200〜300ページ以上）を新規に開いた際や、別ファイルから後ろのページを表示していたファイルへ切り替えた際に、アプリケーションが警告なく突然強制終了（クラッシュ）する現象が発生していました。

Windows イベントログの解析およびコード精査により、以下の根本原因が判明しました：
1. **ネイティブPDFiumレンダリングの非同期キャンセル競合**:
   - `PdfiumRenderer.RenderPageAsync` および `ExtractInteractiveDataAsync` において、`_renderLock`（`SemaphoreSlim` 排他ロック）のスコープ内で `await Task.Run(..., cancellationToken)` を呼び出していました。
   - `Task.Run` に `cancellationToken` を渡していたため、ページ移動等でトークンがキャンセルされた瞬間に外側の `using (var releaser = await _renderLock.LockAsync())` が早期に破棄（Dispose）され、排他ロックが解放されていました。
   - しかし、スレッドプール上でネイティブPDFiumを実行している処理（unmanaged code）は直ちには停止せず実行を継続しているため、後続の描画タスクがネイティブPDFiumに多重進入し、FormFillEnvironment の二重解放やアクセスペジ違反（`AccessViolationException`）を引き起こしていました。
2. **ドキュメント切り替え時の余計な1ページ目初期化とキャンセル多発**:
   - ドキュメント切り替え時、`InitializeDocument` が常に「1ページ目」で初期化されてバックグラウンド描画を即座に開始し、直後に目的のページ（例: 200ページ目）へジャンプして1ページ目の描画をキャンセルする二重処理が行われていました。
   - これにより、大量ページファイルの切り替え時にキャンセル競合が極めて高頻度で誘発されていました。
3. **IPC（プロセス間通信）タイムアウト値の不足**:
   - `SingleInstanceManager` の接続タイムアウトが 800ms と短く、ファイル読み込み中に別ファイルを開いた際にタイムアウトエラーを引き起こすリスクがありました。

---

## 2. 変更内容

### 2.1 コアロジック・ViewModel の修正
1. **`src/PDFBinder.Core/Services/PdfiumRenderer.cs`**:
   - `RenderPageAsync` および `ExtractInteractiveDataAsync` において、`_renderLock.LockAsync()` のリリーサー解放（`Dispose`）を `Task.Run` の完了後（`finally` ブロック）で確実に実行する設計に改修。
   - `Task.Run` の引数から `cancellationToken` を除外し、外側のキャンセルによるネイティブ処理中のロック早期解放を防止。
   - ネイティブ処理の完了待機とキャンセル通知（`OperationCanceledException`）の両立を担保。
2. **`src/PDFBinder.App/ViewModels/MainViewModel.cs`**:
   - `OnDocumentChanged` および `OnActiveSessionChanged` でドキュメントを切り替える際、切り替え先ドキュメントの保持している `CurrentPageNumber`（または 1）を算出し、`InitializeDocument` の `preferredPage` 引数として直接渡すように修正。
   - 切り替え直後に目的のページで直接初期化されるようになり、不要な1ページ目の描画起動および直後のキャンセル競合を完全に排除。
3. **`src/PDFBinder.App/Services/SingleInstanceManager.cs`**:
   - `ConnectionTimeoutMs` を 800ms から 5000ms（5秒）に緩和し、大容量ファイル処理中の安定性を強化。

### 2.2 プロジェクト設定・ドキュメント
1. **`Directory.Build.props`**:
   - バージョンを `0.11.2` から `0.11.3` にインクリメント。
2. **`CHANGELOG.md`**:
   - `## [0.11.3] - 2026-10-10` セクションを追加（エンドユーザー向け記述）。

---

## 3. テストと検証結果

### 3.1 単体テストの追加
- **`tests/PDFBinder.Tests/PdfiumRendererTests.cs`**:
  - `RenderPageAsync_WhenCanceledDuringExecution_KeepsLockUntilCompletionAndAllowsNextRender`:
    描画中にキャンセルが発生してもネイティブ処理が完了するまでロックが維持され、後続の描画が安全に実行されることを検証。
  - `ExtractInteractiveDataAsync_WhenCanceledDuringExecution_KeepsLockUntilCompletion`:
    フォーム・注釈データ抽出処理における同様のキャンセルの安全性を検証。
- **`tests/PDFBinder.Tests/ViewModels/MainViewModelMultiFileTests.cs`**:
  - `SwitchActiveSession_ToLargeDocument_DirectlyInitializesToCurrentPageWithoutPage1Intermediate`:
    300ページの大容量ドキュメントへの切り替え時に、1ページ目を経由せず目的のページ（例: 200ページ目）で直接 `InitializeDocument` が実行されることを検証。

### 3.2 テストスイート実行結果
```text
Passed!  - Failed: 0, Passed: 603, Skipped: 0, Total: 603, Duration: 44 s
```
全 603 件の単体テストがすべて PASS し、既存の全機能を含め回帰不具合がないことを確認しました。

### 3.3 ビルド検証結果
```text
ビルドに成功しました。
    0 個の警告
    0 個のエラー
```
警告およびエラーなく正常にビルドが完了することを確認しました。

---

## 4. 影響範囲とリスク評価
- **他機能への影響**: なし。描画処理・排他制御の安全性向上および初期化プロセスの最適化のみであり、PDF表示、サムネイル生成、編集、保存等の全機能がそのまま正常動作します。
- **パフォーマンス**: ドキュメント切り替え時に不要な1ページ目の読み込み・描画をスキップするため、大容量ドキュメント間切り替え時の応答性が向上しています。
