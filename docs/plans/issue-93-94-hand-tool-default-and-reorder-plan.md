# 手書きツールのデフォルトを移動に変更＆ボタン並び替え（Issue #93, #94）実装計画

手書き詳細エディタにおいて、デフォルトで選択される描画ツールを「ペン」から「移動（手のひら / Hand）」に変更し、手書きリボンタブ内のツールボタン配置を整理・並び替えます。
また、関連する単体テストの更新、基本設計書およびプロジェクトドキュメントの同期更新を行います。

## ユーザー確認事項

> [!NOTE]
> `/grill-me` インタビューおよびフィードバックに基づく合意事項：
> 1. **ツールボタン並び順**: `[移動] [文字選択] [ペン] [蛍光ペン] [全体消し] [部分消し] [選択]`
>    - 閲覧・移動・テキスト選択系（移動、文字選択）を左側に配置
>    - 筆記・消去系（ペン、蛍光ペン、全体消し、部分消し）を中央に配置
>    - ストローク編集系（選択）を部分消しの右側に配置
> 2. **デフォルトツールの適用タイミング**: アプリ起動時（ViewModel初期化時）に「移動」を初期値とし、ファイルを開き直した際や作業中はユーザーが選択したツール状態を維持
> 3. **ブランチ名**: `issue-93-94-hand-tool-default-and-reorder`

## 変更内容

### 1. View / ViewModel (PDFBinder.App)

---

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- 手書きタブ（`RibbonHandwritingToolGroup`）のツールボタン配置を以下の順序に並び替え:
  1. 移動 (`EditorToolMode.Hand`)
  2. 文字選択 (`EditorToolMode.TextSelect`)
  3. ペン (`EditorToolMode.Pen`)
  4. 蛍光ペン (`EditorToolMode.Highlighter`)
  5. 全体消し (`EditorToolMode.EraserStroke`)
  6. 部分消し (`EditorToolMode.EraserPoint`)
  7. 選択 (`EditorToolMode.Select`)

#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- `_selectedTool` の初期値を `EditorToolMode.Pen` から `EditorToolMode.Hand` に変更。
- 初期化時、移動ツール選択状態に合わせて各種ツールプロパティ（`CanToggleStraightLine`、`CanTogglePenPressure`、`CanChangeThickness`、`CanChangeColor` 等）が適切に無効化されることを担保。

---

### 2. テストコード (PDFBinder.Tests)

---

#### [MODIFY] [DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)
- 初期状態の `SelectedTool` 検証（`Assert.Equal(EditorToolMode.Pen, vm.SelectedTool)`）を `EditorToolMode.Hand` に更新。
- 初期化直後の `CanChangeThickness`、`CanChangeColor` が `false` であることを確認する検証の整合性を確保。

#### [MODIFY] [ViewModelsTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ViewModelsTests.cs)
- 初期状態の `SelectedTool` 検証を `EditorToolMode.Hand` に更新。

---

### 3. ドキュメント (docs/)

---

#### [MODIFY] [basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
- 詳細手書きエディタのツールバー構成およびデフォルトツールの記載を最新仕様に更新。

#### [MODIFY] [PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
- Issue #93、#94 に対応する機能進捗ステータスを更新。

---

## 検証計画

### 自動テスト
- `dotnet test` を実行し、全単体テストが 100% 成功することを確認。

### ビルド確認
- `dotnet build` を実行し、警告・エラーなく成功することを確認。

### 手動検証
- アプリを起動し、手書きタブを開いた時点で「移動」ツールが選択状態（アクティブ）になっていることを確認。
- ツールボタンの並び順が `[移動] [文字選択] [ペン] [蛍光ペン] [全体消し] [部分消し] [選択]` となっていることを確認。
- 移動ツール選択時に、マウスドラッグによる画面スクロール（パン）やリンククリックが正常に機能することを確認。
- 各ツール（文字選択、ペン、蛍光ペン、消しゴム、選択）への切り替えが正常に行えることを確認。
