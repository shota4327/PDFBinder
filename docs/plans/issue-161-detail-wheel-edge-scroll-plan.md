# 実装計画: Issue #161 詳細ビューの境界でマウスホイール操作時にページ反対側へスクロールしてしまう不具合の修正

## 1. 概要・背景
詳細ビューで垂直スクロールバーが表示されている（ズーム等でページ全体がビューポートに収まらない）状態で、以下の操作を行うと、ページはそのままで同一ページの反対側へ強制スクロールしてしまう不具合が発生する。
- 最初のページの上端で上スクロールした際に、ページ下端へスクロールしてしまう
- 最後のページの下端で下スクロールした際に、ページ上端へスクロールしてしまう

### 原因
[`DetailEditorView.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml.cs#L265) の `ExecutePageTurns` において、`ViewModel.CanGoToPreviousPage` または `ViewModel.CanGoToNextPage` が `false` により実際のページ遷移が1度も行われなかった場合でも、無条件に `DetailScrollViewer.ScrollToBottom()` または `ScrollToTop()` がディスパッチされていたため。

---

## 2. 改修方針
多重防御として以下の2箇所の修正を実施する。

1. **`HandleSinglePageWheelTurn` における境界判定（`isAtEdge`）の厳格化**:
   - `e.Delta > 0`（上スクロール）時は `ViewModel?.CanGoToPreviousPage == true` を必須とする。
   - `e.Delta < 0`（下スクロール）時は `ViewModel?.CanGoToNextPage == true` を必須とする。
   - 前後ページが存在しない方向へのスクロールは境界（`isAtEdge`）と判定せず、余計な `WheelPageTurnTracker` へのDelta累積やイベント消費を行わない。
2. **`ExecutePageTurns` における実際の遷移実績の検証**:
   - コマンド実行ループ内で、実際にページ遷移が成功した回数（`executedTurns`）をカウント。
   - `executedTurns > 0` の場合のみ `ScrollToBottom()` / `ScrollToTop()` をディスパッチし、遷移しなかった場合はスクロール位置をそのまま維持する。
3. **単体テストの追加**:
   - 先頭ページ・末尾ページにおけるナビゲーション可否判定と、境界ホイール動作に関する検証テストを追加。

---

## 3. 変更対象ファイル
- [`src/PDFBinder.App/Views/DetailEditorView.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml.cs)
  - `HandleSinglePageWheelTurn`
  - `ExecutePageTurns`
- [`tests/PDFBinder.Tests/PageNavigationAndViewOptionsTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PageNavigationAndViewOptionsTests.cs) (または適切なテストファイル)
  - 境界遷移制御に関するテストの追加
- [`Directory.Build.props`](file:///c:/Git/PDFBinder/Directory.Build.props)
  - パッチバージョンのインクリメント（`0.1.13` → `0.1.14`）
- [`CHANGELOG.md`](file:///c:/Git/PDFBinder/CHANGELOG.md)
  - リリースノートの追記（エンドユーザー向け記述）

---

## 4. 検証手順
1. `dotnet test`: 全単体テストがパスすることを確認
2. `dotnet build`: 警告・エラーなくビルドが成功することを確認
