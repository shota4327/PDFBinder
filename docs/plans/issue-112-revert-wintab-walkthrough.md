# 検証報告: WinTab API連携（PR #113）のロールバック

PR #113（Issue #112: WinTab API連携による外付けペンタブレット筆圧感知対応）の作業を取り消し、`1ba34ee76407eacdbcfc4b5541951742a4e477f9` 時点への復帰作業を実施しました。

## 実施結果概要

### 1. Issue #112 の再オープンおよび経緯記録
- `gh issue reopen 112` を実行し、Issue #112 を `OPEN` 状態に復元しました。
- 以下のコメントを投稿し、経緯を記録しました:
  > PR #113 のマージ内容を取り消し、状態を PR #109 マージ直前/直後（コミット `1ba34ee76407eacdbcfc4b5541951742a4e477f9`）へロールバックするため、本 Issue を再オープンしました。
  > 再検討および再実装に向けて対応を進めます。

### 2. 作業ブランチおよび Revert の適用
- 作業ブランチ: `revert/issue-112-wintab`
- リバート対象コミット: `e20e860`（Merge pull request #113）
- 過去の検討・設計履歴として以下の2ファイルは保持:
  - `docs/plans/issue-112-wintab-pen-pressure-plan.md`
  - `docs/plans/issue-112-wintab-pen-pressure-walkthrough.md`
- 本リバートの計画・検証記録を追加:
  - `docs/plans/issue-112-revert-wintab-plan.md`
  - `docs/plans/issue-112-revert-wintab-walkthrough.md`

### 3. リバートされたコードおよびドキュメント
- 削除された新規ファイル:
  - `src/PDFBinder.App/Interop/WinTab/IWinTabService.cs`
  - `src/PDFBinder.App/Interop/WinTab/WinTabNativeMethods.cs`
  - `src/PDFBinder.App/Interop/WinTab/WinTabService.cs`
  - `src/PDFBinder.Core/Helpers/WinTabPressureHelper.cs`
  - `tests/PDFBinder.Tests/WinTabPressureHelperTests.cs`
  - `tests/PDFBinder.Tests/WinTabServiceTests.cs`
- 復元されたファイル（1ba34ee 時点へ復帰）:
  - `src/PDFBinder.App/Controls/EditorInkCanvas.cs`
  - `src/PDFBinder.App/MainWindow.xaml`
  - `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
  - `src/PDFBinder.App/ViewModels/MainViewModel.cs`
  - `tests/PDFBinder.Tests/DetailEditorPenPressureTests.cs`
  - `docs/basic_design.md`
  - `README.md`
  - `docs/PROJECT.md`

## 検証結果

### 1. ビルド検証
- `dotnet build`
- 結果: **成功（警告: 0, エラー: 0）**

### 2. 単体テスト検証
- `dotnet test`
- 結果: **成功（合格: 264, 失敗: 0, スキップ: 0, 合計: 264）**
- Issue #112 の WinTab 関連テスト（11件）が除外され、作業前の264件全テストが 100% PASS することを確認しました。
