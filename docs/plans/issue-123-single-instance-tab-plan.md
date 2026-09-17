# [実装計画] Issue #123: PDFファイル関連付け起動時に同一ウィンドウの新しいタブで開く（単一インスタンス管理）

Windowsのエクスプローラー等でPDFファイルを関連付け（または「プログラムから開く」）によってダブルクリックして開いた際、現在は毎回別プロセスとして新しいウィンドウが起動します。
本改修では、既にPDF Binderが起動している場合は、直近に操作していた既存ウィンドウの新しいタブ（新規セッション）としてPDFを開き、そのウィンドウを自動的に最前面に復元・アクティブ化する単一インスタンス（Single Instance & IPC）連携を実装します。

## ユーザー確認・合意済み仕様 (/grill-me 結果)

- **単一インスタンス動作**: 常に単一インスタンスとして動作させ、既存ウィンドウの新しいタブで開く（必要に応じて `--new-window` / `-n` で別ウィンドウ起動を許可）。
- **ウィンドウのフォーカス制御**: 既存ウィンドウが最小化または背面にあれば、自動的に最前面（アクティブ状態）に復元して表示する。
- **引数なし起動時の挙動**: スタートメニューやデスクトップショートカット等からの引数なし起動時は、常に新しいウィンドウを起動する。
- **複数ウィンドウ存在時の対象先**: 最も直近にアクティブ（操作）されていたウィンドウの新しいタブで開く。
- **オプション引数**: `--new-window`（別名 `-n`）で新規ウィンドウを強制。
- **アーキテクチャ**: 単一プロセス内で複数ウィンドウ（`MainWindow`）を統合管理するモデルを採用。

## 変更対象ファイルと設計詳細

### 1. コマンドライン引数解析の拡張
- **`src/PDFBinder.App/Helpers/CommandLineArgsHelper.cs`** [MODIFY]
  - `CommandLineArgsResult` レコードを刷新:
    ```csharp
    public readonly record struct CommandLineArgsResult(
        IReadOnlyList<string> Files,
        bool ForceNewWindow);
    ```
  - `--new-window` および `-n` オプションの認識ロジックを追加。
  - 複数ファイル引数を安全に正規化・リスト抽出。
  - 不要となった旧 `LaunchAdditionalProcess` / `CreateProcessStartInfo` を整理・リファクタリング。

### 2. 単一インスタンス・IPCサービスの新規作成
- **`src/PDFBinder.App/Services/SingleInstanceManager.cs`** [NEW]
  - 名前付きミューテックス（`Local\PDFBinder_SingleInstance_Mutex_{UserName}`）および名前付きパイプ（`PDFBinder_SingleInstance_Pipe_{UserName}`）によるプロセス間通信を実装。
  - クライアント側（第2以降の起動プロセス）:
    - 起動引数（ファイルリスト、`ForceNewWindow` フラグ）をパイプへ送信。
    - 正常送信後に Win32 `AllowSetForegroundWindow` を発行し、即時終了。
    - 接続失敗（タイムアウト等）時はフォールバックしてプライマリインスタンスとして動作。
  - サーバー側（プライマリプロセス）:
    - バックグラウンドタスクでパイプリクエストを受信待機。
    - 受信データを解析し、UIスレッド（`Dispatcher.InvokeAsync`）へディスパッチ。
    - リクエストに応じたウィンドウ表示制御（新規ウィンドウ作成 または 直近アクティブウィンドウへのタブ追加）。

### 3. ウィンドウアクティブ化およびフォーカス制御ヘルパー
- **`src/PDFBinder.App/Helpers/WindowActivationHelper.cs`** [NEW]
  - 直近アクティブな `MainWindow` の選定（各ウィンドウの `Activated` イベントでタイムスタンプを更新）。
  - ウィンドウの安全な最前面化・フォーカス復元（最小化解除 `WindowState.Normal`、`Activate()`、`Topmost` トグル、Win32 `SetForegroundWindow`）。

### 4. アプリケーションライフサイクル制御の改修
- **`src/PDFBinder.App/App.xaml.cs`** [MODIFY]
  - `OnStartup` を改修し、起動時に `SingleInstanceManager` によるプライマリ判定を実施。
  - セカンダリプロセスの場合は引数送信後に `Shutdown()`。
  - プライマリプロセスの場合は `ShutdownMode = ShutdownMode.OnLastWindowClose;` を設定し、IPCサーバーを起動。
  - `Exit` イベントでミューテックスおよびパイプサーバーを確実に破棄。

### 5. メインウィンドウのトラッキング対応
- **`src/PDFBinder.App/MainWindow.xaml.cs`** [MODIFY]
  - `Activated` イベントを購読し、`WindowActivationHelper` へアクティブ時刻を登録。

### 6. 基本設計書およびドキュメントの同期
- **`docs/basic_design.md`** [MODIFY]
  - 1.3節に単一インスタンス管理、IPC、および `--new-window` 引数仕様を追記。
- **`README.md`** [MODIFY]
  - コマンドライン引数（`--new-window`, `-n`）の説明を追加。
- **`docs/PROJECT.md`** [MODIFY]
  - 機能インベントリのステータス更新。

### 7. 単体テストの追加・更新
- **`tests/PDFBinder.Tests/CommandLineArgsHelperTests.cs`** [MODIFY]
  - `--new-window` / `-n` 指定時の判定テスト。
  - ファイルリスト抽出、無効引数フィルタリング、空引数時のテストケースを拡充。

---

## 検証計画 (Verification Plan)

### 自動テスト (Automated Tests)
- `dotnet test` を実行し、既存テストおよび新規追加テストがすべて 100% PASS することを確認。
- `dotnet build` を実行し、警告・エラーなくビルドが成功することを確認。

### 手動検証 (Manual Verification)
1. **基本タブ統合動作**:
   - アプリを1つ起動した状態で、別のターミナルから `PDFBinder.exe test.pdf` を実行。
   - 既存ウィンドウが最前面に復元され、新しいタブとして `test.pdf` が追加されることを確認。
2. **重複オープン抑止**:
   - 既に開いている `test.pdf` を再度コマンドから指定した場合、重複タブを作らず既存の `test.pdf` タブがアクティブ化されることを確認。
3. **新規ウィンドウ明示オプション (`--new-window`, `-n`)**:
   - `PDFBinder.exe --new-window test2.pdf` を実行した際、既存ウィンドウのタブではなく別ウィンドウとして新しく開くことを確認。
4. **引数なし起動時の新規ウィンドウ**:
   - アプリ起動中に引数なしで `PDFBinder.exe` を実行した際、新しい空のウィンドウが立ち上がることを確認。
5. **最小化からの復元**:
   - 既存ウィンドウを最小化した状態でファイルを開いた際、自動的に通常サイズで最前面に表示されることを確認。
