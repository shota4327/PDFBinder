# Issue #77 ステータスバーの半透明化およびサイズ拡大 検証報告（Walkthrough）

## 変更概要
Issue #77 の要件に基づき、下部ステータスバーを半透明オーバーレイ配置（下端ドック型、半透明白 `#D9FFFFFF`（不透明度85%））とし、高さを約5px拡大（MinHeight 42px、Padding 12,5）するとともに、内部のボタン（28x28px）およびページ入力欄（高さ26px）を連動拡大しました。
また、最新のフィードバックに基づき、不透明度を85%（`#D9FFFFFF`）に設定し、単一ページ表示（FitToWindow）のみならず連続ページ表示（Continuous）およびグリッド俯瞰表示においても、最下部スクロール時に最後のページやサムネイルカードがステータスバーと重なって隠れないよう下部オフセット（62px）を確保しました。

---

## 修正・改善内容

### 1. 不透明度85%の設定（指定値の適用）
- `MainWindow.xaml` にてステータスバーの背景色を `#D9FFFFFF`（白の85%不透明度、アルファ値 `0xD9` = 217/255 ≒ 85.1%）に設定。
- 上部境界線を `#CBD5E1` に設定し、文字・ボタンの優れた可読性と背後コンテンツの上品な透過を両立。

### 2. 単一・連続表示およびグリッド表示でのステータスバー重複回避
- **詳細エディタ（`DetailEditorView.xaml`）**:
  - `ScaleTransform` の親であるキャンバス `Grid` のマージンを `Margin="20,20,20,62"` に設定。
  - 単一ページ表示時はステータスバーの上部領域にページが綺麗に収まり、下部フッターや署名が被らない配置を実現。
  - 連続表示時も、ページスクロール中はステータスバーの下を通り抜けて半透明オーバーレイを活かしつつ、最下部までスクロールした際に下部62pxの余白によって最後のページの下端がステータスバーの上に完全に現れ、文字が被らない配置を実現。
- **ViewModel（フィット倍率計算）**:
  - `DetailEditorViewModel.cs` にて `FitToWindow` かつ `SinglePage` の場合、ステータスバーの高さ分（42px）を差し引いた利用可能高さ（`availableHeight = ViewportHeight - SinglePageTotalVerticalMargin - SafetyBuffer`）で倍率を算出。
  - 計算上の余白（上25px + 下67px = 92px）と実際の外側マージンが完全一致するため、スクロールバーを誤出現させずにウィンドウ内に完璧にフィット。
- **グリッドビュー（`GridView.xaml`）**:
  - `ScrollViewer` のパディングを `Padding="20,20,20,62"` に更新し、最下部までスクロールした際に最終行のサムネイルカードおよび回転・削除ボタンがステータスバーに隠れず快適に操作可能。

---

## 変更ファイル一覧
1. **[MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)**:
   - `Grid.RowDefinitions` を3行構成（Row 2: `*`）に変更し、ステータスバーを `Grid.Row="2"` `VerticalAlignment="Bottom"` `Panel.ZIndex="10"` に配置。
   - `Background="#D9FFFFFF"`（不透明度85%）、`BorderBrush="#CBD5E1"`。
   - `MinHeight="42"`、`Padding="12,5"`、前後ボタン・ズームボタン `28x28px`、ページ入力欄高さ `26px`。
2. **[DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)**:
   - `StatusBarHeight`（42.0）および `SinglePageTotalVerticalMargin`（92.0）を追加。
   - `ApplyFitMode()` にて `FitToWindow` かつ `SinglePage` の場合にステータスバー高さを差し引いた領域でフィット倍率を算出。
3. **[DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
   - キャンバス親 `Grid` の `Margin` を `20,20,20,62` に設定し、単一・連続表示ともに最下部スクロール時に最後のページがステータスバーと重ならないよう下部オフセットを確保。
4. **[GridView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml)**:
   - `ScrollViewer` の `Padding` を `20,20,20,62` に設定し、最下部スクロール時のカード操作ボタンの重複を回避。
5. **[DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)**:
   - 単一ページ時の `SinglePageTotalVerticalMargin` およびステータスバー非重複・スクロールバー非出現の検証テストを追加。
6. **[basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)** & **[PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)**:
   - ステータスバーの不透明度85%（#D9FFFFFF）および全ビューでの最下部スクロール重複回避の仕様をドキュメントに反映。

---

## 検証結果

### 1. ビルド検証
```pwsh
dotnet build
```
- **結果**: 警告 0 件、エラー 0 件でビルド成功。

### 2. 自動テスト検証
```pwsh
dotnet test
```
- **結果**: 全 210 件合格（パス: 210、失敗: 0、スキップ: 0）。単一ページ・連続表示・ウィンドウフィット・幅合わせを含めすべてのテストが 100% PASS。
