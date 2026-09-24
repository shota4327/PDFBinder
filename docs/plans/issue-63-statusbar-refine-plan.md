# 実装計画書: ステータスバー表示内容の厳選およびエラーダイアログ昇格 (Issue #63)

## 1. 概要
ステータスバー中央のステータスメッセージ（`StatusMessage`）において、既存の左下ページ表示（`< ページ 1 / 10 >`）と重複していた不要なページ通知を削除し、保存エラーや読み込みエラーなどの各種例外・操作警告を控えめなステータス文字列から統一された「アプリ内モーダルエラーダイアログ」へ昇格します。
また、エラー発生時にもステータスバーには技術的な例外詳細文字列の代わりに「保存に失敗しました。」等の簡潔なステータスを残すことで、視認性と信頼性を向上させます。

---

## 2. 変更方針と設計

### 2.1 重複表示メッセージの削除
以下の3箇所における不要・重複な `StatusMessage` 代入を削除します。
1. **詳細エディタのページスクロール時**: `StatusMessage = $"ページ {DetailEditor.CurrentPage.PageNumber} / {Document.PageCount}";`（左下のページナビゲーションと完全重複）
2. **詳細エディタへの遷移時**: `StatusMessage = $"ページ {page.PageNumber} を編集しています。";`
3. **サムネイル生成完了後のグリッド表示時**: `StatusMessage = $"グリッド表示（全 {Document.PageCount} ページ）";`

### 2.2 エラー・警告メッセージのダイアログ昇格
以下のエラーおよびバリデーション警告を、アプリ内モーダルエラーダイアログで表示します。
1. 起動時引数のファイル不在 (`App.xaml.cs`)
2. ファイル読み込みエラー (`OpenDocumentInternalAsync`)
3. PDF結合エラー (`AppendDocumentInternalAsync`)
4. PDF挿入エラー (`InsertPdfFilesInternalAsync`)
5. 保存エラー (`ExecuteSaveForDocumentAsync`)
6. ページ未選択警告 (`ExportSelectedPagesAsync`)
7. エクスポートエラー (`ExportSelectedPagesAsync`)
8. 全分割エラー (`SplitAllPagesAsync`)
9. ページ2分割エラー (`SplitPagesHalfAsync`)
10. サムネイル生成エラー (`GenerateThumbnailsForDocumentAsync`)

### 2.3 エラーダイアログ UI & 操作性
- **UI形式**: 既存の保存確認ダイアログ・印刷ダイアログと統一されたダークテーマのアプリ内モーダルカード（`ModalCardBorderStyle`）。
- **構成**: エラーアイコン（赤系ヘッダー）、タイトル、詳細メッセージ、および「OK」ボタン。
- **閉じ操作**: 「OK」ボタン押下、`Enter` キー、`Esc` キーのみで閉じる（背景クリックでの誤閉じは抑止）。
- **キー制御**: ダイアログ表示中は、メイン画面のグローバルショートカットキー（Ctrl+S、Delete等）を無効化。
- **ステータスバー連動**: エラー発生時、ステータスバーには「保存に失敗しました。」「読み込みに失敗しました。」等の簡潔な文言を設定。

---

## 3. 具体的な実装計画

### 3.1 `PDFBinder.App/ViewModels/MainViewModel.cs`
- プロパティの追加:
  - `IsErrorDialogVisible` (bool)
  - `ErrorDialogTitle` (string)
  - `ErrorDialogMessage` (string)
- コマンド・メソッドの追加:
  - `CloseErrorDialogCommand` (RelayCommand)
  - `ShowErrorDialog(string title, string message, string? statusSummary = null)`
- 単体テスト用デリゲート:
  - `Action<string, string>? ShowErrorPrompt`（テストでモーダル呼び出しを検証可能にする）
- メッセージ整理:
  - 各エラー処理ブロックを `ShowErrorDialog` 呼び出しに移行
  - 重複ページ表示の代入を削除

### 3.2 `PDFBinder.App/MainWindow.xaml`
- エラーダイアログのインアプリ・オーバーレイを追加:
  - `ModalBackdropGridStyle` を適用したオーバーレイ Grid（`Panel.ZIndex="1005"`）
  - エラーアイコン、タイトル（`ErrorDialogTitle`）、メッセージ（`ErrorDialogMessage`）、「OK」ボタン（`CloseErrorDialogCommand`）

### 3.3 `PDFBinder.App/MainWindow.xaml.cs`
- `OnPreviewKeyDown` 内に `HandleErrorDialogKeyDown(vm, e)` を追加:
  - `IsErrorDialogVisible` が true の場合、`Enter` / `Escape` / `Space` でダイアログを閉じ、`e.Handled = true`
  - ダイアログ表示中のグローバルショートカットキー（Ctrl系、Delete等）を抑止

### 3.4 `PDFBinder.App/App.xaml.cs`
- ファイル不在時の通知を `vm.ShowErrorDialog` 経由で行うように更新

### 3.5 単体テストの追加 (`PDFBinder.Tests/StatusBarAndErrorDialogTests.cs`)
- **テスト1: 重複ページ表示の排除検証**
  - 詳細エディタでのページ切り替え時、`StatusMessage` が上書きされないこと
- **テスト2: 保存エラー時のダイアログ呼び出しとステータス表示検証**
  - 保存失敗時に `ShowErrorPrompt` が適切なタイトルとメッセージで呼ばれ、`StatusMessage` が簡潔な失敗文言になること
- **テスト3: ページ未選択バリデーションのダイアログ表示検証**
  - エクスポート時にページ未選択の場合、エラーダイアログが発火すること
- **テスト4: 各種エラー処理でのダイアログ連携検証**
  - 読み込み、結合、分割などの例外発生時に正しくダイアログプロンプトが呼ばれること

---

## 4. 検証手順
1. `dotnet test` で新規テストを含む全テストの 100% PASS を確認
2. `dotnet build` でエラーおよび警告が 0 であることを確認
3. Walkthrough（`docs/plans/issue-63-statusbar-refine-walkthrough.md`）の作成と記録
