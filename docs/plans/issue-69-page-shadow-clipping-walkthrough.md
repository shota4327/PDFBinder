# Issue #69: 単一ページ表示時の上下の影クリッピング修正 検証報告

## 概要
詳細エディタ（`DetailEditorView`）において、単一ページ表示時にページの左右の影は表示されるものの上下の影がカット（クリッピング）される問題を修正しました。あわせて、連続表示モードの端部余白の統一、およびビューポート内での中央揃えとスムーズなスクロールの両立を実現しました。

---

## 実施した変更内容

### 1. View（XAML）の改修
- **[DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
  - `DetailScrollViewer` の `Padding` を `"30"` から `"20"` へ変更（ご指定の上下左右各20px）。
  - `DetailScrollViewer` 直下の `Grid` に `VerticalAlignment="Center"` および `Margin="20"` を設定。
    - ズーム（`ScaleTransform`）の外側に `Margin="20"` を配置することで、倍率に関わらず常に一定の20pxの影用余白を担保。
    - 単一ページ表示・連続表示のいずれにおいても、端部の影（上12px、下20px、左右16px）がスクロール領域境界でクリッピングされる問題を解消。
    - 画面に余裕がある場合は上下左右中央揃えで美しく配置され、拡大時や画面サイズ超過時は上端から自然にスクロール可能。

### 2. ViewModel の改修
- **[DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)**:
  - 定数を追加:
    - `ScrollViewerPadding = 20.0`
    - `PageShadowMargin = 20.0`
    - `TotalHorizontalMargin = 80.0` (Padding左右計40px + 影マージン左右計40px)
    - `TotalVerticalMargin = 80.0` (Padding上下計40px + 影マージン上下計40px)
  - `ApplyFitMode()` において、`TotalHorizontalMargin` および `TotalVerticalMargin` を用いて利用可能幅・高さを算出。
    - ウィンドウ合わせ（`FitToWindow`）実行時に「ページ + 影」が余白内に美しく収まり、不要なスクロールバーを発生させない正確な倍率計算を実現。

### 3. 単体テストの追加・更新
- **[DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)**:
  - 新マージン（80px）を考慮した `FitToWindow` の検証テストを更新。
  - 横長ページ（Landscape）の `FitToWindow` 検証テストを追加。
  - `FitToWidth` の各種テストを新マージンに合わせて更新。
- **[PageNavigationAndViewOptionsTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PageNavigationAndViewOptionsTests.cs)**:
  - 新マージンを考慮したビューポート寸法でフィット計算の即時性・整合性を検証するテストを更新。

### 4. ドキュメントの同期更新
- **[basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)**:
  - ウィンドウ合わせ時のパディング（20px）、影用マージン（20px）、中央揃えの仕様を最新化。

---

## 検証結果

### 自動テスト（dotnet test）
全159件の単体テストがすべて 100% PASS することを確認しました。
```
成功!   -失敗:     0、合格:   159、スキップ:     0、合計:   159、期間: 889 ms - PDFBinder.Tests.dll (net10.0)
```

### ソリューションビルド（dotnet build）
警告・エラー 0 件で正常にビルドが完了することを確認しました。
```
ビルドに成功しました。
    0 個の警告
    0 エラー
```
