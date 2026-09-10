# リボンスタイルタブ化と手書きツールヘッダー統合の実装計画 (Issue #17)

上部ヘッダーにWindows標準リボンスタイルのタブ構造（「PDF編集」タブ・「手書き」タブ）を導入し、手書きエディタのツールバーを「手書き」タブ内に統合します。太さ選択はドット選択式＋インライン自由展開スライダーに刷新します。

## ユーザー確認済み事項（grill-me 合意事項）
1. **リボンタブ構造**:
   - ツールバー上部に「PDF編集」「手書き」のタブバーを配置し、選択中タブがツールバー領域とシームレスにつながるOfficeリボンスタイルとする。
   - グリッド俯瞰表示時は「手書き」タブを無効化（選択不可）。
   - ページ編集画面（エディタ）表示時は自動で「手書き」タブを選択し、一覧に戻る操作時に自動で「PDF編集」タブに戻す。
2. **エディタ側ツールバーの統合**:
   - 現在 `DetailEditorView` 上部にあるツールバーは廃止し、上部ヘッダーの「手書き」リボンタブ内に完全統合してキャンバス領域をフル活用する。
3. **「手書き」タブ内のボタングループ構成**:
   - ①【戻る】: 一覧に戻る
   - ②【ツール】: 選択、ペン、蛍光ペン、直線、全消しゴム、部分消し、手のひら
   - ③【太さ & 色】: 太さ選択（0.5px, 1px, 2px, 4px の●ドット選択 + 自由選択トグル + インラインスライダー展開）、カラーパレット
   - ④【表示・移動】: ズーム（縮小、100%、拡大）、ページ移動（前ページ、P.X、次ページ）
4. **太さ選択UI**:
   - 0.5px、1px、2px、4px は円（●）の大きさとツールチップで表現。
   - 「自由選択」ボタンをクリック（トグル）すると、右隣にインラインでスライダーと現在px表示が展開・折りたたみ可能。

## 変更対象ファイル

### ViewModel
- [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
  - `SelectedRibbonTabIndex`（0: PDF編集, 1: 手書き）の追加。
  - ページ編集開始時のタブ自動切り替え（`SelectedRibbonTabIndex = 1`）および一覧復帰時の切り替え（`SelectedRibbonTabIndex = 0`）。
- [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
  - `IsCustomThicknessOpen` プロパティの追加（自由選択スライダーの展開状態管理）。
  - 各プリセット太さ設定コマンド（`SetPresetThicknessCommand`）および自由選択トグルコマンドの追加。

### UI・XAML
- [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
  - ヘッダー部に「PDF編集」「手書き」タブヘッダーを追加。
  - タブ選択に応じて表示を切り替えるコンテンツ領域を構成。
  - 「手書き」タブ内にリボンスタイルボタン（戻る、ツール群、太さ・色群、表示・移動群）を実装。
- [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
  - 画面上部の個別ツールバー（Row 0）を削除し、キャンバス領域が全画面で表示されるように調整。

### ドキュメント更新
- [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
  - 画面構成およびリボンタブ構成（PDF編集タブ・手書きタブ）の説明を同期。
- [docs/PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
  - 機能インベントリ（F35/F40）を更新。

## 検証計画
### 自動テスト・ビルド検証
- `dotnet build` による警告・エラーのゼロ確認。
- `dotnet test` による全単体テスト（45件）の 100% PASS 確認。

### 画面・動作確認
- グリッド俯瞰時:
  - 「PDF編集」タブが選択されており、全ファイル・ページ操作ボタンが正常に利用できること。
  - 「手書き」タブが非活性（クリック不可）であること。
- 詳細エディタ遷移時:
  - ページダブルクリックで自動的に「手書き」タブに切り替わること。
  - 「手書き」タブ内の各ツール（ペン、蛍光ペン、直線、消しゴム等）が正常に切り替わること。
  - 太さ選択で 0.5px / 1px / 2px / 4px が反映され、「自由選択」でインラインスライダーが展開し微調整できること。
  - カラーパレットで色が変更できること。
  - 「一覧に戻る」で自動的にグリッド画面および「PDF編集」タブに復帰すること。
