# タイトルバー廃止とタブバー統合の検証報告 (Issue #28)

Windows標準タイトルバーを廃止し、最上段にタブバーと最小化・最大化・閉じるボタンを統合したモダンタイトルバーの実装と検証結果の報告です。

---

## 1. 実施した変更内容

### ① ViewModel層の拡張 (`MainViewModel.cs` / `PdfDocumentModel.cs`)
- `PdfDocumentModel._filePath` 変更時に `FileName` の PropertyChanged が発火するよう `[NotifyPropertyChangedFor(nameof(FileName))]` を付与。
- `MainViewModel` にタイトルバー中央表示用の `DisplayFileName` プロパティを追加。
- ドキュメント読み込みやファイル保存（別名保存含む）に伴い、`DisplayFileName` が自動的に同期・通知されるリアクティブな機構を実装。

### ② UIスタイルの追加・調整 (`App.xaml`)
- **キャプションボタンスタイル (`CaptionButtonStyle`, `CloseCaptionButtonStyle`)**:
  - Windows 11 Fluentスタイルの外観とホバー挙動（閉じるボタンは赤色 `#E81123` ハイライト、最小化・最大化は `#E2E8F0`）を定義。
  - Segoe Fluent Icons / Segoe MDL2 Assets による標準グリフ（最小化 `\uE921`、最大化 `\uE922`、元に戻す `\uE923`、閉じる `\uE8BB`）を採用。
- **リボンタブスタイル (`RibbonTabRadioButtonStyle`)**:
  - 最上段のタイトルバーと調和する高さ・パディングを設定。
  - アクティブタブは白背景（`#FFFFFF`）＋上部角丸（`6,6,0,0`）で下部ツールバーコンテンツ領域とシームレスに結合。非アクティブタブは透明背景＋ホバー時ハイライト。

### ③ メインウィンドウの統合タイトルバー化 (`MainWindow.xaml` / `MainWindow.xaml.cs`)
- **`WindowChrome` の導入**:
  - `CaptionHeight="38"`, `ResizeBorderThickness="6"` を設定し、OSネイティブのリサイズやスナップ（Windows 11 Snap Layouts）、ドラッグ移動を維持。
  - 最大化時の境界はみ出しを防止する `WindowState == Maximized` 時のパディング補正トリガーを追加。
- **最上段行（Row 0）のレイアウト（Chrome / Edge スタイル）**:
  - **左側**: アプリアイコン（18x18px、ドラッグ可能）＋「PDF編集」「手書き」タブ（`IsHitTestVisibleInChrome="True"`）
  - **中央**: 開いているファイル名表示（薄いグレー `#64748B`、クリック透過でウィンドウドラッグ・ダブルクリック最大化可能）
  - **右側**: 最小化・最大化/復元・閉じるボタン（`IsHitTestVisibleInChrome="True"`）
- **ウィンドウ制御ロジック**:
  - `SystemCommands`（`MinimizeWindowCommand`, `MaximizeWindowCommand`, `RestoreWindowCommand`, `CloseWindowCommand`）のバインディング登録。
  - `WindowState` の変化（通常 ⇄ 最大化）に応じて、ボタンアイコン（`\uE922` ⇄ `\uE923`）およびツールチップ（「最大化」⇄「元に戻す」）が自動的に切り替わるハンドラを実装。

### ④ ドキュメントの同期更新
- `docs/basic_design.md`: 統合タイトルバー兼タブバーの画面仕様およびUI設計を追記。
- `docs/PROJECT.md`: 機能インベントリに `F36` を完了として追加、テスト件数を58件に更新。

---

## 2. 検証結果

### 自動テスト (`dotnet test`)
- コマンド: `dotnet test`
- 結果: **全58件 PASS（成功、失敗0、スキップ0）**
  - 新規作成した `MainViewModelTitleTests`（4件）を含むすべてのテストが正常に通過。
    - `DisplayFileName_WhenInitialState_ReturnsEmpty`: 初期状態での空文字確認
    - `DisplayFileName_WhenDocumentWithFilePathAssigned_ReturnsFileName`: ファイル名抽出確認
    - `DisplayFileName_WhenFilePathUpdated_NotifiesPropertyChanged`: パス更新時のプロパティ通知確認
    - `DisplayFileName_WhenDocumentReplaced_OldDocumentDoesNotTriggerEvent`: 旧インスタンスの購読解除確認

### ビルド確認 (`dotnet build`)
- コマンド: `dotnet build`
- 結果: **成功（警告: 0, エラー: 0）**
