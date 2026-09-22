# [検証報告] Issue #123: PDFファイル関連付け起動時に同一ウィンドウの新しいタブで開く（単一インスタンス管理）

## 実装内容まとめ

Windowsのエクスプローラー等でPDFファイルを関連付け（または「プログラムから開く」）によってダブルクリックして開いた際、従来は起動ごとに新しいウィンドウ（別プロセス）が立ち上がっていました。
本改修では、名前付きミューテックスおよび名前付きパイプ（Named Pipe）による単一インスタンス管理（Single Instance & IPC）を導入し、既に起動中のインスタンスが存在する場合は、直近に操作していた既存ウィンドウの新しいタブとしてPDFを開き、そのウィンドウを自動的に最前面に復元・アクティブ化する仕組みを構築しました。

### 1. 主な変更点
- **`CommandLineArgsHelper.cs` & `CommandLineArgsResult`**:
  - `CommandLineArgsResult(Files, ForceNewWindow)` へレコード構造を刷新（旧プロパティへの後方互換も保持）。
  - `--new-window` および `-n` オプションに対応し、明示的な新規ウィンドウ起動をサポート。
  - 複数ファイル引数の正規化および重複排除を実装。
- **`SingleInstanceManager.cs` [NEW]**:
  - 名前付きミューテックス（`Local\PDFBinder_SingleInstance_Mutex_{UserName}`）による多重起動検知。
  - 名前付きパイプ（`PDFBinder_SingleInstance_Pipe_{UserName}`）によるプロセス間通信（引数ペイロード送信、ACK応答、Win32 `AllowSetForegroundWindow` 付与）。
  - サーバー接続失敗時や前インスタンス異常終了時の安全なプライマリ引き継ぎ（フォールバック）。
- **`WindowActivationHelper.cs` [NEW]**:
  - メインウィンドウのアクティブ化履歴（`Activated` イベント）を追跡し、直近に操作されていたウィンドウを即座に特定。
  - 最小化時の `WindowState.Normal` 復元、`Topmost` 一時トグル、Win32 `SetForegroundWindow` / `ShowWindow(SW_RESTORE)` による確実な最前面表示。
- **`MainWindow.xaml.cs`**:
  - ウィンドウ初期化時に `WindowActivationHelper.RegisterWindow(this)` を追加。
- **`App.xaml.cs`**:
  - `OnStartup` で単一インスタンス判定を実施。
  - セカンダリプロセスの場合は引数を送信して即時 `Shutdown()`。
  - プライマリプロセスの場合は `ShutdownMode.OnLastWindowClose` を設定し、パイプサーバーで新規タブまたは新規ウィンドウへのディスパッチ待機。
  - `OnExit` でミューテックスおよびパイプリソースを確実に解放。
- **ドキュメントの同期**:
  - `docs/basic_design.md`（1.3節に単一インスタンス仕様・新規ウィンドウオプションを追記）
  - `README.md`（関連付け起動時のタブ統合、`--new-window` オプションの説明を更新）
  - `docs/PROJECT.md`（F19およびテスト数を最新化）

---

## 検証結果

### 1. 自動単体テスト
- 新規追加した単体テスト：
  - `CommandLineArgsHelperTests`:
    - `Parse_WithNewWindowFlag_SetsForceNewWindowTrue` (Theory: `--new-window`, `-n`, 大小文字両対応)
    - `Parse_WithoutNewWindowFlag_SetsForceNewWindowFalse`
    - `Parse_WithDuplicateFiles_DeduplicatesEntries`
  - `SingleInstanceManagerTests`:
    - `TryAcquireOwnership_FirstInstance_ReturnsTrue`
    - `IPC_Communication_SendsAndReceivesPayload` (名前付きパイプによるプロセス間通信、ペイロード送受信、ACK検証)
- **テスト実行結果**:
  - `dotnet test`: **326件全テスト PASS (失敗 0)**
  - `dotnet build`: **0 警告 / 0 エラー** でビルド成功
