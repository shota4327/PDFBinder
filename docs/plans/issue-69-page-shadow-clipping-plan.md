# Issue #69: 単一ページ表示時の上下の影クリッピング修正 実装計画

## 概要
詳細エディタ（`DetailEditorView`）の単一ページ表示時において、表示ページの左右の影は表示されているにもかかわらず、上下の影がカット（クリッピング）されてしまう問題を解決します。あわせて、連続表示モードの端部余白の統一およびビューポート中央揃えとスクロールの安全な両立を実現します。

## 原因の特定
1. **ScrollViewer によるクリッピング**:
   - ページの背景 Border に適用されている影エフェクトは、`BlurRadius="16" ShadowDepth="4" Direction="270"`（真下向き）であり、上側に約12px、下側に約20px、左右に約16px はみ出して描画される。
   - ウィンドウ合わせ（`FitToWindow`）の計算時、画面縦幅いっぱいにページがスケーリングされ、ページの上下端と ScrollViewer 内部描画領域（`ScrollContentPresenter`）の上下境界との隙間がわずか約1pxとなっていた。
   - その結果、外側に広がる上下の影が `ScrollContentPresenter` の境界でクリップされていた。
2. **影用マージンの欠如**:
   - 単一ページ表示コンテナ（`SinglePageContainer`）や連続表示の端部に、影を収めるためのマージンが確保されていなかった。

---

## 提案する変更内容

### 1. View（XAML / C#）の改修
#### [MODIFY] [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- **ScrollViewer パディングの縮小**:
  - `DetailScrollViewer` の `Padding` を `"30"` から `"20"`（上下左右各20px）へ変更。
- **単一ページ表示の影マージンと中央揃え**:
  - `SinglePageContainer` に `Margin="20"` を指定し、上下左右に影が完全に収まる余白を確保。
  - `VerticalAlignment="Center"` を指定。
- **連続表示モードの影マージン統一**:
  - `PagesItemsControl` に `Margin="0,20"` を指定し、先頭ページ上部および最終ページ下部にも20pxの影用マージンを確保。
- **ビューポート中央揃えとスクロールの両立**:
  - `SinglePageContainer` や `PagesItemsControl` を配置する親 Grid の `MinHeight` に `DetailScrollViewer` の `ViewportHeight` をバインド。
  - コンテンツが画面内に収まる際は上下中央揃え、拡大時やあふれた際は上端から安全にスクロールできる構成とする。

### 2. ViewModel の改修
#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- **マージン定数の定義**:
  - `ScrollViewerPadding = 20.0`
  - `PageShadowMargin = 20.0`
  - `TotalHorizontalMargin = (ScrollViewerPadding + PageShadowMargin) * 2;` // 80.0px
  - `TotalVerticalMargin = (ScrollViewerPadding + PageShadowMargin) * 2;` // 80.0px
- **フィット倍率計算の更新**:
  - `ApplyFitMode()`、`ApplyFitToWindow()`、`ApplyFitToWidth()` において、従来の 60px 固定パディングに代わり、`TotalHorizontalMargin` および `TotalVerticalMargin` を用いて利用可能幅・高さを算出。
  - これにより、ウィンドウ合わせ実行時にも「ページ + 影」が余白内に美しく収まり、スクロールバーが発生しない正確な倍率が計算される。

### 3. 単体テストの追加・更新
#### [MODIFY] [DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)
- 新しい余白（水平計80px、垂直計80px）に基づいた `FitToWindow` および `FitToWidth` の算出倍率テストを更新・追加。
- 境界値および全テストのグリーン確認。

---

## 検証計画

### 自動テスト
- `dotnet test` を実行し、既存テストおよび新規追加テストがすべて 100% PASS することを確認。

### ビルド確認
- `dotnet build` を実行し、警告・エラーなくビルドが成功することを確認。
