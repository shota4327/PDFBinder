# Issue #52 Walkthrough: ページ移動ボタン・表示オプションの追加

## 概要
Issue #52 の要件に基づき、下部ステータスバーを約1.5倍に拡大して左端にページ表示・移動ボタン（`< ページ [ 1 ] / 2 >`）、右端にズームコントロール（`[-] 100% [+]`）を追加しました。
また、リボンの表示タブ内に「100%」「ウィンドウにあわせる（デフォルト）」「幅にあわせる」の表示オプションボタングループを新設し、ウィンドウリサイズやカレントページ変更時に最適な拡大率を自動維持するモード追従型のフィッティング表示を実現しました。

---

## 主な変更内容

### 1. 表示フィットモード（表示オプション）の実装
- **列挙型 `DetailViewFitMode` の追加**:
  - `FitToWindow`（ウィンドウに合わせる・デフォルト）: 画面の幅と高さの両方に1ページが綺麗に収まるよう自動調整
  - `FitToWidth`（幅に合わせる）: 画面の横幅いっぱいにページ幅が収まるよう自動調整
  - `ActualSize`（100%・等倍）: 等倍表示
  - `None`（カスタム倍率）: ユーザーが拡大・縮小ボタン等で手動ズームした際に移行
- **動的リサイズ追従（モード追従型）**:
  - `DetailScrollViewer` の `SizeChanged` を購読し、ウィンドウのリサイズ時やページ切り替え時に ViewModel へビューポート寸法を通知して最新の最適倍率を自動再計算・適用。
  - 手動でズームイン／ズームアウトを行った場合は即座に固定倍率モード（`None`）へ切り替わり、意図しない自動リサイズを防止。

### 2. ページナビゲーション機能の実装
- **`DetailEditorViewModel` / `MainViewModel`**:
  - `CanGoToPreviousPage`, `CanGoToNextPage`: 先頭・末尾ページおよびグリッドビュー状態に応じた活性/非活性判定。
  - `GoToPreviousPageCommand`, `GoToNextPageCommand`: 前後ページへのワンクリック移動。
  - `CurrentPageNumber`: 直接入力によるページ番号指定ジャンプ。TextBox内での Enter キー押下により即座に対象ページへスクロール移動。

### 3. UI・ステータスバーの刷新（`MainWindow.xaml`）
- **ステータスバー**:
  - `MinHeight="36"` に拡大（約1.5倍）し、タッチ・マウスクリックが快適に行えるモダンなバーを構成。
  - **左端**: `<Button (<)>` + `ページ` + `<TextBox (ページ番号)>` + `/ X` + `<Button (>)>`（詳細ビュー時のみ有効）。
  - **中央**: `StatusMessage`。
  - **右端**: `ProgressBar` + `<Button (-)>` + `<Button (倍率表示/リセット)>` + `<Button (+)>`（詳細・グリッド両ビュー対応）。
- **「表示」タブ（リボン）**:
  - 詳細・グリッド表示切り替えボタンとズームボタンの間に、3つの独立したラジオボタングループ（「1:1 100%」「ウィンドウ」「幅」）を新設（詳細ビュー時のみ有効）。

### 4. 起動時例外（XamlParseException）の修正と再発防止
- **不具合原因**: 表示タブ内「100%」ボタンのアイコン用 TextBlock に誤って `FontFamily="{StaticResource BaseFont}"`（Styleリソース）が指定されており、起動時のBAMLパース時に `ArgumentException` が発生していた。
- **対応**: 該当プロパティ指定を削除し、正常起動を確認。
- **再発防止テストの追加**: `App.xaml` および `MainWindow.xaml` の全XAMLパース・テンプレート・スタイル・リソース解決が正常に完了することをSTAスレッドで自動検証する `MainWindowInitializationTests.cs` を追加。

---

## 検証結果

### 1. 単体テスト（xUnit）
新規テストファイル `PageNavigationAndViewOptionsTests.cs` および `MainWindowInitializationTests.cs` を作成し、以下を含む全144件のテストが100%合格することを確認しました。

- `MainWindow_ShouldInitializeWithoutXamlParseException`: MainWindowとAppリソースの初期化が例外なく正常に完了すること
- `InitialFitMode_ShouldBeFitToWindow`: 初期状態のフィットモードが `FitToWindow` であること
- `SetFitModeCommand_ShouldUpdateFitModeAndRecalculate`: フィットモード切り替え時の倍率再計算
- `ManualZoom_ShouldSwitchFitModeToNone`: 手動ズーム時にフィットモードが解除されること
- `ZoomReset_ShouldSetActualSizeFitMode`: ズームリセット時に `ActualSize` になること
- `UpdateViewportSize_ShouldDynamicallyRecalculateWhenFitModeActive`: ビューポートリサイズ時の自動追従
- `PageNavigation_CommandsAndCanExecute_ShouldWorkCorrectly`: ページ移動コマンドとCanExecute判定
- `CurrentPageNumber_Setter_ShouldNavigateToSpecifiedPage`: ページ番号直接指定によるジャンプ
- `MainViewModel_PageNavigationAndFitMode_Integration`: MainViewModelとの連動およびビュー切り替え時の無効化
- `EqualityToBooleanConverter_ShouldHandleDetailViewFitModeEnum`: Enum型のTwoWayバインディング

```text
成功!   -失敗:     0、合格:   144、スキップ:     0、合計:   144、期間: 845 ms - PDFBinder.Tests.dll (net10.0)
```

### 2. ビルド検証
```text
dotnet build
ビルドに成功しました。
    0 個の警告
    0 エラー
```
