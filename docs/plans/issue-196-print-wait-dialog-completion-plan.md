# 実装計画: Issue #196 印刷中に終了したときの印刷待ちダイアログ未終了バグの解消

## 1. 概要・背景
PDF Binder では、印刷スプール処理のバックグラウンド実行中にアプリケーションを終了（ウィンドウの「×」ボタン等）しようとした場合、印刷待ちダイアログが表示され、プリンターへのデータ送信が完了した後に自動でウィンドウを閉じる設計となっています（Issue #190）。
しかし現在、印刷データ送信がすべて正常に完了しても印刷待ちダイアログが閉じることなく残ったままとなり、アプリが終了しない不具合が発生しています（Issue #196）。

---

## 2. 原因分析（Root Cause）

### 2.1 `BackgroundPrintQueueService.ActiveJobCount` の計算不整合
- `ActiveJobCount` は `_queue.Count + (_isProcessing ? 1 : 0)` で算出されます。
- `ProcessQueueLoopAsync` では、ジョブ実行完了時に呼び出される `JobCompleted` イベントの時点でも、ループフラグ `_isProcessing` は依然として `true` のままです。
- そのため、最後の印刷ジョブ（キュー残り0件）が正常完了した瞬間であっても、`ActiveJobCount` は `0 + 1 = 1` を返します。

### 2.2 `MainViewModel.OnPrintJobCompleted` の判定スキップ
- `MainViewModel` では `OnPrintJobCompleted` ハンドラー内で以下の判定を行っています:
  ```csharp
  if (IsPrintWaitDialogVisible && _printQueueService.ActiveJobCount == 0)
  {
      IsPrintWaitDialogVisible = false;
      RequestCloseWindow?.Invoke();
  }
  ```
- `JobCompleted` 発火時点で `ActiveJobCount` が `1` であるため、この条件式が必ず `false` となり、待機ダイアログの非表示化およびウィンドウ終了要求（`RequestCloseWindow`）がスキップされます。

### 2.3 `ActiveJobCountChanged` が残数 0 で発火しない
- 単一ジョブ完了後、ループ内で `remaining = _queue.Count + (_isProcessing ? 1 : 0)`（値: 1）が計算され、`ActiveJobCountChanged(1)` が発火します。
- その後のループ先頭で `_queue.Count == 0` を検知して `_isProcessing = false` となりループを抜けますが、この時点で `ActiveJobCountChanged(0)` は一切発行されません。
- 結果として、キューが空になったこと（残数 0）を通知するイベントが外部に届きません。

### 2.4 単体テストで検知できなかった要因
- `MainViewModelPrintExitTests.cs` のモック（`FakeBackgroundPrintQueueService`）では、`SimulateJobCompleted` 内で `ActiveJobCount` を先にデクリメント（0に）してから `JobCompleted` を発火していたため、実サービスとの乖離がありテストがパスしていました。

---

## 3. 修正設計方針

### 3.1 `BackgroundPrintQueueService` の状態管理と通知の是正
1. **現在処理中ジョブ（`_currentJob`）の明示的管理**:
   - `_currentJob` フィールド（`PrintSpoolJob?`）を導入し、`ActiveJobCount` は `_queue.Count + (_currentJob != null ? 1 : 0)` で正確に算出します。
2. **完了タイミングでの即時状態リセット**:
   - 単一ジョブのスプール送信（`SpoolDocumentAsync`）が完了した直後、ロック下で `_currentJob = null` とし、キュー残数を再評価します。
   - キューが空（`_queue.Count == 0`）の場合は `_isProcessing = false` と `_allCompletedTcs` の完了を設定します。
3. **正確なイベント発行順序**:
   - ジョブ完了時（`JobCompleted?.Invoke`）には、`ActiveJobCount` はすでに完了ジョブを除外した正確な残数（最後のジョブなら 0）を返します。
   - 続いて `ActiveJobCountChanged?.Invoke(remaining)` を発行し、全ジョブ終了時には確実に `0` が通知されるようにします。

### 3.2 `MainViewModel` における二重セーフティの導入
1. **`OnPrintJobCompleted` での確実なクローズ**:
   - `_printQueueService.ActiveJobCount == 0` が真となり、待機ダイアログを閉じて `RequestCloseWindow?.Invoke()` を実行します。
2. **`OnPrintQueueCountChanged` での二重保護（セーフティネット）**:
   - `count == 0` が通知された場合にも、もし `IsPrintWaitDialogVisible` が `true` であれば自動的にダイアログを閉じ、`RequestCloseWindow?.Invoke()` をトリガーします。
   - これにより、イベント受信のタイミングや順序に関わらず確実にダイアログが閉じられ、アプリが終了します。

### 3.3 テストコードの拡充
1. `BackgroundPrintQueueServiceTests.cs`:
   - ジョブ完了イベント発火時における `ActiveJobCount` が 0 になっていることの検証。
   - 全ジョブ完了時に `ActiveJobCountChanged` が引数 `0` で発火されることの検証。
2. `MainViewModelPrintExitTests.cs`:
   - モックだけでなく、実サービス `BackgroundPrintQueueService`（`MockPrintService` を注入）と `MainViewModel` を接続した結合テストを追加。
   - 印刷中に終了待機ダイアログが表示された状態でスプールが完了した際、ダイアログが閉じ、`RequestCloseWindow` が確実に呼び出されることを検証。

---

## 4. 変更対象ファイル一覧

| ファイルパス | 変更内容 |
|---|---|
| `src/PDFBinder.App/Services/BackgroundPrintQueueService.cs` | `_currentJob` による状態管理、ジョブ完了時の残数正確化、`ActiveJobCountChanged(0)` の確実な発行 |
| `src/PDFBinder.App/ViewModels/MainViewModel.cs` | `OnPrintQueueCountChanged` における残数 0 時の待機ダイアログ閉塞・終了要求二重セーフティの追加 |
| `tests/PDFBinder.Tests/BackgroundPrintQueueServiceTests.cs` | 完了時の残数 0 および `ActiveJobCountChanged(0)` 発行のテスト追加 |
| `tests/PDFBinder.Tests/MainViewModelPrintExitTests.cs` | 実 `BackgroundPrintQueueService` を組み合わせた印刷完了時ダイアログ閉塞・終了要求の結合テスト追加 |
| `Directory.Build.props` | バージョンを `0.10.1` → `0.10.2`（パッチインクリメント）に更新 |
| `CHANGELOG.md` | `0.10.2` リリースノート（エンドユーザー向け）の追記 |
| `docs/PROJECT.md` / `docs/basic_design.md` | ドキュメント同期更新 |

---

## 5. 検証手順
1. `dotnet test` を実行し、既存テストおよび新規追加テストがすべて 100% PASS することを確認。
2. `dotnet build` を実行し、エラーおよび警告がないことを確認。
