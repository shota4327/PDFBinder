# Issue #51: 複数ファイルを開いて切り替える機能の実装計画

## 概要
PDF Binder にて、複数のPDFファイルを同時に開き、タイトルバー中央のプルダウンメニューから閲覧・編集対象のファイルを切り替える機能を実装します。
これにより、複数のPDFを開きながらの作業や比較、個別保存・編集が1つのウィンドウ内で快適に行えるようになります。

---

## 主な仕様と振る舞い（ヒアリング結果に基づく合意事項）

1. **ドキュメント管理モデル（`DocumentSession`）**:
   - 各ドキュメントごとに独立した `PdfDocumentModel`、`IUndoRedoService`、表示状態（詳細ビュー/グリッドビュー、現在のページ番号、ズーム率）を保持する `DocumentSession` クラス（またはそれに準ずるViewModel）を導入。
   - ファイルを切り替えても、元のファイルのUndo/Redo履歴や表示位置・ズーム状態が完全に保持・復元される。
2. **タイトルバー中央のプルダウンUI**:
   - タイトルバー中央のファイル名表示エリアをフラットなボタンスタイル（ファイル名＋下向き矢印▼）に変更。
   - クリックするとドロップダウンポップアップが開き、開いている全ファイル名、フルパスのTooltip、変更ありマーク（`*`）、右端の「×（閉じる）」ボタンを表示。
   - 項目選択で対象ドキュメントへ切り替え。
3. **個別ファイルおよび全ファイルの終了処理**:
   - 各項目の「×」ボタンを押した際、対象ドキュメントに変更（`IsModified == true`）があれば保存確認プロンプト（はい／いいえ／キャンセル）を表示。
   - アクティブなドキュメントを閉じた場合は隣接するドキュメントを自動的にアクティブ化。
   - すべてのファイルを閉じた場合は「ドキュメント未読み込み状態」（0件）となり、初期画面（ドラッグ＆ドロップ案内）へ遷移。
   - ウィンドウ全体の閉じるボタン（×）またはAlt+F4押下時は、変更のある各ドキュメントについて順次保存確認を行い、キャンセルされた場合は終了を中断。
4. **ファイルを開く処理の挙動**:
   - 「開く」ボタン、ドラッグ＆ドロップ、アプリ起動時のコマンドライン引数において、新規ファイルは常に「別ファイルとして追加」し、プルダウンの選択肢を増やす。
   - 既に開かれている同一ファイルパスの場合は、二重に開かず既存の該当ドキュメントをアクティブ化。
   - アプリ起動時に複数ファイルが渡された場合、プロセスを多重起動せず単一ウィンドウ内で全ファイルを別ドキュメントとして開く。
   - 「追加」ボタンのみが、現在アクティブなドキュメントへのページ結合を行う。
5. **「白紙追加」および「名称未設定.pdf」の扱い**:
   - ドキュメント未読み込み状態（0件）で「白紙追加」を押下した場合、新規ドキュメント「名称未設定.pdf」（白紙1ページ）を作成し、プルダウンに表示してアクティブ化。
   - 空（0ページ）の未編集ドキュメントがある状態でファイルを開いた場合のみそれを置き換え、1ページ以上ある場合は別ドキュメントとして追加。

---

## 変更対象ファイルと詳細設計

### 1. Model / ViewModel レイヤー
- `src/PDFBinder.App/Models/DocumentSession.cs` [NEW]
  - 1つの開いているPDFファイルのセッション状態をカプセル化するクラス。
  - プロパティ:
    - `PdfDocumentModel Document`: ドキュメントデータ
    - `IUndoRedoService UndoRedoService`: ドキュメント専用のアンドゥ・リドゥ履歴
    - `bool IsDetailViewActive`: 詳細ビューまたはグリッドビューの状態
    - `int CurrentPageNumber`: 現在表示中のページ番号
    - `double ZoomFactor`: ズーム倍率
    - `int SelectedRibbonTabIndex`: 選択中のリボンタブ（編集/手書き/表示）
    - `string DisplayFileName`: 表示名（変更フラグ付き、例: `ファイル名.pdf *`）
- `src/PDFBinder.App/ViewModels/MainViewModel.cs` [MODIFY]
  - `ObservableCollection<DocumentSession> Documents` を追加。
  - `DocumentSession? ActiveSession` プロパティを追加。
  - `Document` プロパティは `ActiveSession?.Document` と連動し、既存のViewバインディングとの後方互換性を維持。
  - `OpenDocumentAsync`: 新規 `DocumentSession` を作成して追加・アクティブ化（既存パス重複時は切り替えのみ）。
  - `CloseDocumentCommand`: 指定した `DocumentSession` を保存確認付きで閉じる。
  - `CloseAllDocumentsAsync` / `ConfirmSaveAllAsync`: ウィンドウ終了時の全ドキュメント保存確認。
  - `AddBlankPage`: 0件の時は新規 `DocumentSession`（名称未設定.pdf）を作成して追加。
  - `SwitchDocumentCommand`: 指定したセッションをアクティブに切り替え、状態（UndoRedo、ビューモード、ページ、ズーム）を復元。
- `src/PDFBinder.App/App.xaml.cs` [MODIFY]
  - `OnStartup`: 複数ファイルが渡された場合、`CommandLineArgsHelper.LaunchAdditionalProcess` によるプロセス起動を行わず、同一ウィンドウの `MainViewModel.OpenDocumentAsync` にて全ファイルを順次開くように改修。
- `src/PDFBinder.App/Helpers/CommandLineArgsHelper.cs` [MODIFY / MAINTAIN]
  - 複数ファイル解析ロジックを必要に応じて調整。

### 2. View / XAML レイヤー
- `src/PDFBinder.App/MainWindow.xaml` [MODIFY]
  - タイトルバー中央の `TextBlock` を、フラットなドロップダウンUIに変更。
    - `WindowChrome.IsHitTestVisibleInChrome="True"` を設定。
    - 現在のファイル名、下向き矢印アイコン（`keyboard_arrow_down`）を表示。
    - クリック時に開く `Popup` またはカスタムドロップダウン。
    - ドロップダウンリスト内に各セッションのファイル名、変更マーク、右端の「×」ボタンを配置。
  - 0件時のプレースホルダー表示（または非表示）制御。
  - ドキュメント未読み込み時のリボンツールバーボタン（保存、回転、削除、書き出し等）の活性/非活性（IsEnabled）制御の徹底。
- `src/PDFBinder.App/MainWindow.xaml.cs` [MODIFY]
  - `OnClosing`: ウィンドウクローズ時に `MainViewModel.ConfirmSaveAllAsync()` を呼び出し、キャンセル時は `e.Cancel = true` で終了を中断。

### 3. 単体テスト
- `tests/PDFBinder.Tests/ViewModels/MainViewModelMultiFileTests.cs` [NEW]
  - 複数ドキュメントのオープン・切り替えテスト。
  - 各ドキュメント独立のUndo/Redo履歴保持の検証。
  - 同一ファイルパスオープン時の重複防止・アクティブ切り替え検証。
  - ドキュメント個別終了時の保存確認・隣接ドキュメントへのアクティブ遷移検証。
  - 全ドキュメントクローズ後の白紙追加（「名称未設定.pdf」作成）検証。
  - 複数ドキュメント変更時の終了時一括確認検証。

---

## 検証計画

### 1. 自動テスト
```pwsh
dotnet test
```
- 既存の全単体テストが PASS すること。
- 新規追加した `MainViewModelMultiFileTests` がすべて PASS すること。

### 2. 手動・ビルド検証
```pwsh
dotnet build
```
- 警告やエラーなくビルドが成功すること。
- アプリを起動し、以下の操作シナリオを確認：
  1. 起動直後に「白紙追加」を押下し、「名称未設定.pdf」が表示されること。
  2. 別のPDFファイルをドラッグ＆ドロップし、プルダウンに2つのファイルが表示され、切り替えができること。
  3. 各ファイルで個別にページ操作や手書きを行い、Ctrl+Z/Ctrl+Y で他方のファイルの履歴が汚染されないこと。
  4. プルダウンの「×」で変更のあるファイルを閉じようとした際、保存確認が表示されること。
  5. すべてのファイルを閉じた際、初期画面に戻ること。
  6. 複数ファイルを指定してアプリを起動した場合、1つのウィンドウ内にすべて開かれること。
