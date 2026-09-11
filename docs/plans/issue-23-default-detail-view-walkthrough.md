# Walkthrough - 詳細ビューを基本とするUI刷新 (Issue #23)

詳細ビューをデフォルト表示とし、縦スクロールによる連続ページ表示、リボン最上段の「表示」タブ新設、ズーム操作の一本化、オンデマンドなサムネイル生成など、閲覧・手書き主体のワークフローへUIを刷新しました。

## 変更内容の概要

### 1. 詳細ビューの縦連続スクロール化
- `DetailPageItemViewModel.cs`:
  - 各ページの表示情報（ページ番号、サイズ、背景ビットマップ、カレント状態）を管理するViewModelを新設。
- `DetailEditorViewModel.cs`:
  - 単一ページ管理から `ObservableCollection<DetailPageItemViewModel> Pages` による全ページ縦連続管理へ刷新。
  - カレントページ追跡、動的レンダリング（ダブルバッファリング）、指定ページへのスクロール要求イベント（`ScrollToPageRequested`）を実装。
- `DetailEditorView.xaml` / `DetailEditorView.xaml.cs`:
  - `ScrollViewer` + `ItemsControl`（`StackPanel Orientation="Vertical"`）による全ページ縦並びUIに変更。
  - スクロール位置から中央表示されているページをリアルタイムに検知してカレントページを更新。
  - ページクリックによるアクティブ化、およびグリッドからのジャンプ時の自動スクロール制御を実装。
- `EditorInkCanvas.cs`:
  - 親の `DetailEditorViewModel` を階層探索するロジックを導入し、複数ページ配置時にも各ページのインク操作を共通ViewModelに正しくバインド可能に改修。

### 2. リボンUIの再編とズーム機能の一本化
- `MainWindow.xaml`:
  - 最上段タブに「表示」タブ（Index 2）を追加（`[PDF編集]`=0, `[手書き]`=1, `[表示]`=2）。
  - 「表示」タブ内に「詳細」および「グリッド」の相互排他切り替えボタングループを配置（アクティブ側を青強調）。
  - 「PDF編集」タブおよび「手書き」タブのズームボタンを廃止し、「表示」タブに一本化。
  - 「手書き」タブから「一覧に戻る」ボタン、ページ送りボタン、ページ番号表示を撤去。
  - グリッド表示中は「手書き」タブを無効化（`IsEnabled=false`）。
- `MainViewModel.cs`:
  - デフォルト表示を詳細ビュー（`IsDetailViewActive = true`）に変更。
  - 詳細ビュー（50%〜300%）とグリッドビュー（220px基準/140px〜360px）で独立したズーム倍率を保持しつつ、UI向けの一本化プロパティ（`CurrentZoomText`, `CanZoomIn`, `CanZoomOut`）とコマンド（`ZoomInCommand`, `ZoomOutCommand`, `ZoomResetCommand`）を提供。
  - 手書きタブ選択中にグリッドへ切り替えた場合の「表示」タブへの自動フォーカス遷移を実装。
  - 詳細ビュー表示時、特定選択がない状態での「回転」「削除」「白紙追加」操作をカレントページ対象として実行するよう調整。
  - サムネイルの即時生成を廃止し、グリッドボタン押下時に初めてオンデマンド非同期生成するよう改修。

### 3. ドキュメントの同期更新
- `docs/basic_design.md`: 画面構成、リボンタブ構成、ズーム仕様、サムネイル遅延生成仕様を最新化。
- `README.md`: 機能一覧およびショートカットキー（Ctrl+0 によるズームリセット追加）を最新化。
- `docs/PROJECT.md`: Feature Inventory のステータスを更新。

---

## 検証結果

### 1. 自動単体テスト
Issue #23の新規要件を検証するテストクラス `DefaultDetailViewTests.cs` を新設し、既存テストスイートも含め全96件のテストがパスすることを確認しました。

```powershell
dotnet test
```

- **結果**: 成功 (96 / 96 テスト PASS)
  - `DefaultDetailView_IsActiveByDefault`: 初期状態が詳細ビューであること
  - `GridSelection_DoubleTap_NavigatesToDetailAndScrolls`: グリッドダブルクリックで詳細ビュー遷移＆スクロール要求が発行されること
  - `DetailView_TargetCurrentPage_WhenNoSelection`: 選択なし時にカレントページが回転・削除・白紙追加の対象となること
  - `Zoom_OperatesOnCurrentView`: 表示中のビューに応じてズーム倍率が適切に制御されること
  - `SwitchingToGrid_WhenHandwritingTabSelected_SelectsViewTab`: 手書きタブ表示中にグリッドへ切り替えた際の自動タブ遷移
  - `Thumbnails_GeneratedOnDemand_WhenGridOpened`: サムネイルがグリッド切替時にオンデマンド生成されること
  - `DetailEditorViewModel_PagesCollection_InitializesCorrectly`: ページコレクションおよびレンダリングの正常性

### 2. ソリューションビルド
```powershell
dotnet build
```

- **結果**: 成功（0 警告, 0 エラー）
