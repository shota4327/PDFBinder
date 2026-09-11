# Issue #25 実装計画: 直線ボタンのトグル化とペン・蛍光ペン連動

## 概要
現在、手書き詳細エディタにおいて「直線」は独立したツール（ラジオボタン）として実装されていますが、Issue #25 の要件に基づき、**「ペン」または「蛍光ペン」使用時にオン・オフできる独立したトグルボタン**へと変更します。
これにより、通常のペン色・太さでの直線描画だけでなく、蛍光ペンの半透明直線（マーカー引き）も自然に行えるようにします。また、ツール切り替え時には直線状態が自動的にリセット（オフ）されるようにします。

---

## ヒアリング（/grill-me）で確定した設計方針
1. **データモデル**:
   - `DetailEditorViewModel` に `IsStraightLine`（`bool`）プロパティを新設。
   - `CanToggleStraightLine`（`bool`）により、`SelectedTool` が `Pen` または `Highlighter` の場合のみ直線トグルを有効化（`IsEnabled=true`）。
   - `SelectedTool` の変更時に、`IsStraightLine` を自動的に `false` にリセット。
2. **UI配置・リボンデザイン**:
   - 主ツールラジオボタングループから「直線」を外し（選択・ペン・蛍光ペン・全体消し・部分消し・移動）、太さやカラーパレットが並ぶ**「描画オプション」グループへ「直線」トグルボタンを移動**。
   - トグルボタン専用スタイル（`RibbonToolToggleStyle`）を用意し、ON時に青色背景ハイライト表示。
3. **描画動作 & カーソル**:
   - 直線トグルがONのときは十字カーソル（`Cursors.Cross`）を表示。
   - 直線を1本引いた後もトグルはONを維持し、連続して直線を引くことが可能。
   - マウスおよびスタイラスペンでの直線プレビュー・コミットに対応。
   - ペン時はペンの色・太さ、蛍光ペン時は蛍光ペンの色・太さ＋半透明ブレンド（`IsHighlighter = true`）を継承して直線ストロークを生成。

---

## 変更対象ファイル

### 1. Model / ViewModel
#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- `[ObservableProperty] private bool _isStraightLine;` を追加。
- `CanToggleStraightLine => SelectedTool == EditorToolMode.Pen || SelectedTool == EditorToolMode.Highlighter;` プロパティを追加。
- `OnSelectedToolChanged` 内で `IsStraightLine = false;` および `OnPropertyChanged(nameof(CanToggleStraightLine));` を呼び出し。

### 2. View / Control
#### [MODIFY] [EditorInkCanvas.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- `IsStraightLine` 依存関係プロパティ（`DependencyProperty`）を追加。
- `UpdateEditingMode()` の更新:
  - `IsStraightLine` が `true` かつ `ToolMode` が `Pen` または `Highlighter` の場合、`EditingMode = InkCanvasEditingMode.None` かつ `Cursor = Cursors.Cross` に設定。
- マウス／スタイラス描画イベントにおいて、`IsStraightLine` 有効時の直線プレビューおよびコミット処理（既存の `CommitStraightLine` と `OnRender` をそのまま活用し、蛍光ペン属性も完全反映）。
- 必要に応じて `EditorToolMode.StraightLine` の削除または非推奨化。

#### [MODIFY] [App.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/App.xaml)
- `RibbonToolRadioStyle` と同等のデザインを持つ `ToggleButton` 用スタイル `RibbonToolToggleStyle` を定義（ON時の背景 `#DBEAFE`、境界線 `#93C5FD`、テキスト `#1D4ED8`、無効時の淡色表示）。

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- 主ツール群から「直線」ラジオボタンを削除。
- 描画オプショングループ（太さ・カラーパレット付近）に「直線」`ToggleButton` を追加。
  - `IsChecked="{Binding DetailEditor.IsStraightLine, Mode=TwoWay}"`
  - `IsEnabled="{Binding DetailEditor.CanToggleStraightLine}"`
  - `Style="{StaticResource RibbonToolToggleStyle}"`
  - アイコン: `&#xED5E;`、ラベル: `直線`

#### [MODIFY] [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- `EditorInkCanvas` の `IsStraightLine` プロパティに `IsStraightLine="{Binding IsStraightLine}"` をバインド。

### 3. ドキュメント
#### [MODIFY] [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
- 手書きツール仕様における直線の位置づけを「独立ツール」から「ペン・蛍光ペンの描画補助トグルモード」へ更新。
#### [MODIFY] [docs/PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
- Issue #25 に対応する機能ステータスや備考の更新。

---

## 検証計画

### 自動テスト（単体テスト）
- [NEW] `DetailEditorStraightLineTests.cs` (または `DetailEditorViewModelTests.cs` に追加):
  - `CanToggleStraightLine`: ペン・蛍光ペン選択時に `true`、選択・消しゴム・移動ツール選択時に `false` となること。
  - `IsStraightLine`: ツール切り替え時（ペン→蛍光ペン、ペン→消しゴム等）に自動的に `false` にリセットされること。
  - `EditorInkCanvas`: `IsStraightLine` と `ToolMode` の組み合わせで `EditingMode` と `Cursor` が正しく設定されること。
- コマンド: `dotnet test`（全テスト 100% PASS を確認）

### 手動検証（UI・描画確認）
- 手書きタブを開く。
- ペン選択時に「直線」トグルボタンが有効（クリック可能）であることを確認。
- 「直線」をオンにしてドラッグし、ペンの色・太さで直線が描画されることを確認。
- 描画後も直線ボタンがオンのままで、連続して直線を引けることを確認。
- 蛍光ペンに切り替えた際、直線ボタンが自動的にオフになることを確認。
- 蛍光ペン選択時に「直線」をオンにしてドラッグし、半透明の直線ハイライトが描画されることを確認。
- 「全体消し」や「移動」に切り替えた際、直線ボタンが無効化（グレーアウト）されることを確認。
