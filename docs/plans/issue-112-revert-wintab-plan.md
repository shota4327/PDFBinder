# 実装計画: WinTab API連携（PR #113）の取り消し・ロールバック

## 1. 概要
PR #113（Issue #112: WinTab API連携による外付けペンタブレット筆圧感知対応）の作業を取り消し、`1ba34ee76407eacdbcfc4b5541951742a4e477f9`（PR #109マージ直後）の状態まで戻します。
クローズ済みの Issue #112 を再オープンし、経緯コメントを記録した上で、安全なブランチ・PR運用手順に則ってロールバックを実施します。

## 2. 方針と決定事項（/grill-me ヒアリング結果）
- **ロールバック方式**: コミット履歴を改変（force push）せず、作業ブランチ `revert/issue-112-wintab` にてマージコミットに対する revert コミット（`git revert -m 1 e20e860`）を作成する。
- **Issue #112 の取り扱い**:
  - `gh issue reopen 112` により再オープン。
  - 「PR #113 のマージを取り消し、再検討のため再オープン」のコメントを記録。
  - 本リバート作業ブランチおよびPRを Issue #112 に紐付ける。
- **ドキュメント・計画履歴の取り扱い**:
  - コード、単体テスト、設計書（`docs/basic_design.md`、`README.md`、`docs/PROJECT.md`）は `1ba34ee` の状態へ完全復帰。
  - 設計・検証経緯を保持するため、Issue #112 の `docs/plans/issue-112-wintab-pen-pressure-plan.md` および `docs/plans/issue-112-wintab-pen-pressure-walkthrough.md` は履歴として保持。
  - 本リバート作業用の計画（`docs/plans/issue-112-revert-wintab-plan.md`）と検証報告（`docs/plans/issue-112-revert-wintab-walkthrough.md`）を新設。

## 3. 作業手順
1. **Issue #112 の再オープンとコメント記録**:
   - `gh issue reopen 112` を実行。
   - `gh issue comment 112` で経緯コメントを投稿。
2. **作業ブランチの作成**:
   - `master` から `revert/issue-112-wintab` ブランチを作成・チェックアウト。
3. **リバートの適用とドキュメント調整**:
   - `git revert --no-commit -m 1 e20e860` を実行。
   - Issue #112 の過去 Plan/Walkthrough ファイルの削除をキャンセル（ステージングから除外または復元）。
   - 本リバートの Plan ファイル（`docs/plans/issue-112-revert-wintab-plan.md`）および Walkthrough ファイル（`docs/plans/issue-112-revert-wintab-walkthrough.md`）を追加。
4. **ビルドおよびテスト検証**:
   - `dotnet build` によるビルド検証（エラー・警告 0）。
   - `dotnet test` による全単体テスト実行（全件 PASS）。
5. **コミットおよびリモートプッシュ**:
   - 作業ブランチ上でコミットを作成し、リモートへプッシュ。
6. **ユーザー報告とレビュー待ち**:
   - 変更内容と検証結果を報告し、PR作成指示を仰ぐ。

## 4. 検証項目
- [ ] Issue #112 が `OPEN` 状態になっており、コメントが記録されていること。
- [ ] `WinTab32.cs`, `WinTabPacketHelper.cs`, `WinTabPacketHelperTests.cs` が削除され、`EditorInkCanvas.cs`, `MainWindow.xaml.cs` 等が `1ba34ee` 時点のコードに戻っていること。
- [ ] `docs/basic_design.md`, `README.md`, `docs/PROJECT.md` の記述が `1ba34ee` 時点の状態に戻っていること。
- [ ] `dotnet test` が 100% PASS すること（Issue #112 追加前のテスト総数）。
- [ ] `dotnet build` が警告・エラーなく成功すること。
