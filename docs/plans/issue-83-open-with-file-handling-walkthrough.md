# Issue 83: 「プログラムから開く」や関連付け起動時にPDFファイルが開かれない問題の修正 - 検証報告 (Walkthrough)

## 実施した変更

エクスプローラーでPDFファイルを右クリックし、「プログラムから開く」や関連付け（ダブルクリック）によって起動した際、渡されたファイルパスの引数が読み込まれず、初期状態（空ドキュメント）のままとなる不具合を修正しました。

### 1. `CommandLineArgsHelper` の新設（`src/PDFBinder.App/Helpers/CommandLineArgsHelper.cs`）
- 起動引数（`string[] args`）から `.pdf` 拡張子を持つファイルパスを抽出・正規化（前後の引用符・空白の除去、フルパス化）。
- オプション引数（`-` または `/` で始まるスイッチ）や非PDFファイルの安全な除外。
- 先頭のPDF（現在のウィンドウで開く対象）と、2つ目以降のPDF（別プロセスで起動する対象）への分割。
- `ProcessStartInfo` を構築し、2つ目以降のファイルを引数として自プロセスを別プロセスとして起動する `LaunchAdditionalProcess` の実装。

### 2. `App.xaml` および `App.xaml.cs` の起動フロー刷新
- `App.xaml` から `StartupUri="MainWindow.xaml"` を削除。
- `App.xaml.cs` で `OnStartup(StartupEventArgs e)` をオーバーライド。
- 引数を解析し、2つ目以降のファイルがあれば別プロセスを即時起動。
- `MainWindow` を生成・表示した上で、先頭ファイルが存在する場合は `MainViewModel.OpenDocumentAsync(filePath)` を非同期実行してドキュメントを即時表示。
- ファイルが存在しない場合は警告メッセージを表示し、アプリは空ドキュメントの初期状態で安全に継続起動。

### 3. ドキュメントの同期更新
- `docs/basic_design.md`: 「1.3 アプリケーション起動仕様とファイル連携」を追加。
- `docs/PROJECT.md`: 機能インベントリに `F19` を追加。
- `README.md`: 主な機能に「プログラムから開く」＆関連付け起動の項目を追記。

---

## テストと検証結果

### 1. 自動テスト（xUnit）
- `CommandLineArgsHelperTests.cs` を新規作成し、以下のテストケースを追加：
  - 引数が null または空配列の場合
  - 単一の PDF パスが渡された場合
  - 複数の PDF パスが渡された場合の分割処理
  - 引用符付きパスや空白を含むパスの正規化
  - 非PDF拡張子やオプショントークン（`--option`, `/v` 等）の除外
  - `ProcessStartInfo` の生成確認
- **実行結果**:
  - `dotnet test`: 187 件全テスト PASS（失敗 0）

### 2. ビルド検証
- `dotnet build`: 0 警告、0 エラーで正常終了。
