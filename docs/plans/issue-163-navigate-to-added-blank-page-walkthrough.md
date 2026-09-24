# Issue #163: 白紙ページ追加時の対象ページ表示（詳細ビュー）検証報告（Walkthrough）

## 1. 概要
詳細ビュー表示中に「白紙ページを追加（Ctrl+B / ツールバーボタン）」を実行した際、追加された白紙ページへ自動的に表示・スクロールが遷移するように改善しました。
また、白紙ページ追加直後に「元に戻す（Ctrl+Z）」を実行した場合も、削除された白紙ページの直前に表示していた元のページが正しく復元・再選択されるように改善しました。

## 2. 実施した変更内容

### 2.1 詳細エディタでの対象ページ決定と自動スクロールの実装
- [`DetailEditorViewModel.InitializeDocument(PdfDocumentModel document, PdfPageModel? preferredPage = null)`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs):
  - 優先してカレントページに設定すべきページ（`preferredPage`）を指定できるように引数を拡張。
  - 対象ページ決定ロジックを独立したヘルパーメソッド `ResolveTargetPage` に分離（単一責任の原則・メソッド行数制限を遵守）。
  - `preferredPage` ➔ 直前のページ（存在する場合） ➔ 直前のインデックス位置（削除等で直前のページが存在しない場合） ➔ 先頭ページの優先順位で安全にカレントページを決定。
  - 対象ページ決定後、`ScrollToPageRequested?.Invoke(targetPage)` を通知し、View層（単一ページ表示および連続スクロール表示）での適切なスクロール・描画位置合わせを実現。

### 2.2 白紙ページ追加処理における詳細ビュー連携
- [`MainViewModel.AddBlankPage()`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs):
  - 詳細ビュー表示中（`IsDetailViewActive == true`）の場合、追加された `blank` ページをターゲットとして `DetailEditor?.InitializeDocument(Document, blank)` を呼び出すように修正。
  - グリッドビュー表示中では、既存の選択状態を変更せずに白紙ページを追加。

### 2.3 単体テストの追加と既存テストの同期
- [`tests/PDFBinder.Tests/DetailEditorBlankPageNavigationTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorBlankPageNavigationTests.cs):
  - `AddBlankPage_WhenDetailViewActive_NavigatesToAddedBlankPage`: 詳細ビュー表示中に白紙ページを追加した際、追加された白紙ページへ切り替わりスクロールイベントが発火することを検証。
  - `AddBlankPage_WhenDetailViewActive_Undo_RestoresPreviousPage`: 白紙ページ追加後にUndoした際、直前の元のページが復元・表示されることを検証。
  - `AddBlankPage_WhenGridViewActive_PreservesExistingSelection`: グリッドビュー表示中の白紙追加で既存の選択状態が維持されることを検証。
  - `DetailEditorViewModel_InitializeDocument_WithPreferredPage_SelectsPreferredPage`: `preferredPage` が指定された場合に正しく選択されることを検証。
  - `DetailEditorViewModel_InitializeDocument_WhenPageDeleted_FallsBackToPreviousValidIndex`: ページ削除時に直前の有効なインデックス位置へフォールバックすることを検証。
- [`tests/PDFBinder.Tests/DefaultDetailViewTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DefaultDetailViewTests.cs):
  - 白紙ページを2回追加した直後にカレントページが追加された2ページ目になっている新仕様をアサートし、その後1ページ目へスクロールして回転するシナリオへ整合。

### 2.4 ドキュメントおよびバージョンの更新
- `Directory.Build.props`: バージョンを `0.4.4` ➔ `0.4.5` にインクリメント。
- `CHANGELOG.md`: `## [0.4.5] - 2026-09-24` セクションを追記し、エンドユーザー向けリリースノートを記録。
- `docs/basic_design.md`: 白紙ページ追加時およびUndo時の詳細ビューナビゲーション挙動を追記。

## 3. 検証結果
- `dotnet test`: 全 435 件の単体テストがすべて PASS (100% 成功)。
- `dotnet build`: 警告 0 件、エラー 0 件でビルド成功。
