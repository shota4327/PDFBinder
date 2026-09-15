# Issue #77 ステータスバーの半透明化およびサイズ拡大 検証報告（Walkthrough）

## 変更概要
Issue #77 の要件に基づき、下部ステータスバーを半透明オーバーレイ配置（下端ドック型、半透明白 `#BFFFFFFF`（不透明度75%））とし、高さを約5px拡大（MinHeight 42px、Padding 12,5）するとともに、内部のボタン（28x28px）およびページ入力欄（高さ26px）を連動拡大しました。
また、ユーザーからのフィードバックに基づき、不透明度を75%（`#BFFFFFFF`）に設定し、単一ページ表示（FitToWindow）時にページがステータスバーと重ならず、かつスクロールバーが出現しないようレイアウトおよびフィット計算の補正を実施しました。

---

## 修正・改善内容

### 1. 不透明度75%の設定（指定値の適用）
- `MainWindow.xaml` にてステータスバーの背景色を `#BFFFFFFF`（白の75%不透明度、アルファ値 0xBF = 191/255 ≒ 74.9%）に設定。
- 上部境界線を `#CBD5E1` に設定し、ステータスバーの視認性と背後コンテンツの上品な透過を両立。

### 2. 単一ページ表示時のステータスバー重複回避＆スクロールバー誤出現解消
- **ViewModel（フィット倍率計算）**:
  - `DetailEditorViewModel.cs` に `StatusBarHeight = 42.0` および `SinglePageTotalVerticalMargin = TotalVerticalMargin + StatusBarHeight`（92px）を導入。
  - `FitToWindow`（ウィンドウに合わせる）かつ `SinglePage`（単一ページ表示）の際、ステータスバーの高さ分（42px）を差し引いた利用可能高さ（`availableHeight = ViewportHeight - SinglePageTotalVerticalMargin - SafetyBuffer`）で倍率を算出。
- **View（XAMLレイアウト）**:
  - `DetailEditorView.xaml` のキャンバス親 `Grid` に動的スタイルを追加し、`SinglePage` の場合は下部にステータスバー高さ分のオフセットを付加（`Margin="20,20,20,62"`、通常時は `Margin="20"`）。
  - これにより、単一ページ表示時はページ全体がステータスバーの上部に綺麗に収まり、**ステータスバーと絶対に重ならない配置**を実現。
  - 計算上の余白（上25px + 下67px = 92px）と実際の外側マージンが完全一致するため、**スクロールバーは一切出現せず、ウィンドウ内に完璧にフィット**します。
  - 連続表示（Continuous）時は通常マージン（`Margin="20"`）となり、全ページがステータスバーの下を通り抜けてスクロールし、半透明のオーバーレイ効果が機能します。

---

## 変更ファイル一覧
1. **[MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)**:
   - `Grid.RowDefinitions` を3行構成（Row 2: `*`）に変更し、ステータスバーを `Grid.Row="2"` `VerticalAlignment="Bottom"` `Panel.ZIndex="10"` に配置。
   - `Background="#BFFFFFFF"`（不透明度75%）、`BorderBrush="#CBD5E1"`。
   - `MinHeight="42"`、`Padding="12,5"`、前後ボタン・ズームボタン `28x28px`、ページ入力欄高さ `26px`。
2. **[DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)**:
   - `StatusBarHeight`（42.0）および `SinglePageTotalVerticalMargin`（92.0）を追加。
   - `ApplyFitMode()` にて `FitToWindow` かつ `SinglePage` の場合にステータスバー高さを差し引いた領域でフィット倍率を算出。
3. **[DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
   - `Grid.Style` による動的マージン（単一ページ時は下部オフセット62px）を設定し、ステータスバーとの重複を回避。
4. **[GridView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml)**:
   - パディング整合性を保ち、カードがステータスバー背後に潜り込んで半透明に透けるよう `Padding="20"` を維持。
5. **[DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)**:
   - 単一ページ時の `SinglePageTotalVerticalMargin` およびステータスバー非重複・スクロールバー非出現の検証テストを追加。
6. **[basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)** & **[PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)**:
   - ステータスバーの不透明度75%（#BFFFFFFF）および単一ページ非重複配置の仕様をドキュメントに反映。

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
