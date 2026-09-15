# Issue #71: 詳細エディタ外側余白削減 検証報告

## 概要
詳細エディタ（`DetailEditorView`）において、外側の余白（`ScrollViewer.Padding`）とページの影用マージン（`Grid.Margin`）の重複による作業スペースの圧迫を解消するため、影の欠け防止マージン（20px）を保持したまま、外側の余白を 20px から 5px へ削減しました。

これにより、片側の余白が 40px から 25px（両側合計 80px から 50px）へと 30px 分コンパクト化され、エディタ内の有効作業スペースが拡大されました。

---

## 実施した変更内容

### 1. View（XAML）の改修
- **[DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
  - `DetailScrollViewer` の `Padding` を `"20"` から `"5"` に変更。
  - 直下の `Grid` の `Margin="20"` は維持し、ドロップシャドウのボケ足・オフセットのクリッピング防止を担保。

### 2. ViewModel の改修
- **[DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)**:
  - 定数 `ScrollViewerPadding` を `20.0` から `5.0` に変更。
  - 水平・垂直合計余白定数 `TotalHorizontalMargin`、`TotalVerticalMargin`（各 50.0px）および関連コメントを最新化。
  - `ApplyFitMode()` におけるウィンドウ合わせ（`FitToWindow`）および幅合わせ（`FitToWidth`）の利用可能領域計算が、新マージン（50px）に基づいて正しく動作することを確認。

### 3. 単体テストの更新
- **[DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)**:
  - 新余白（片側 25px、両側 50px）を反映した `FitToWidth` および `FitToWindow`（縦長・横長ページ）の検証アサーションを更新。
- **[PageNavigationAndViewOptionsTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PageNavigationAndViewOptionsTests.cs)**:
  - 新余白に対応したビューポート寸法で `FitMode` 適用・ウィンドウリサイズ時の拡大率再計算テストを更新。

---

## 検証結果

### 1. 単体テスト（dotnet test）
全180件のテストがすべて 100% PASS することを確認しました。
```
成功!   -失敗:     0、合格:   180、スキップ:     0、合計:   180、期間: 1 s - PDFBinder.Tests.dll (net10.0)
```

### 2. ソリューションビルド（dotnet build）
警告およびエラー 0 件でビルドが正常に完了することを確認しました。
```
ビルドに成功しました。
    0 個の警告
    0 エラー
```
