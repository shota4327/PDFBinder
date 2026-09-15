# Issue #71: 詳細エディタ外側余白削減 実装計画書

## 1. 概要
現在、詳細エディタ（`DetailEditorView`）では、外側の余白（`ScrollViewer.Padding = 20px`）と、ページの影が境界でクリッピングされるのを防止するための影用マージン（`Grid.Margin = 20px`）が重なり、上下左右に各 40px（両側合計 80px）の余白が生じています。
これにより表示・編集領域のスペースが圧迫されているため、影の欠けを防止する影用マージン（20px）を保持したまま、外側の余白（`ScrollViewer.Padding`）を 20px から 5px に削減し、エディタ内の有効作業スペースを広げます。

---

## 2. インタビュー結果・決定事項（/grill-me）
1. **対象画面**:
   - 詳細エディタ（`DetailEditorView`）のみを対象とする（グリッド俯瞰ビューは変更しない）。
2. **余白の内訳とサイズ**:
   - 外側の余白（`DetailScrollViewer` の `Padding`）: `20px` → `5px` に削減。
   - 影用マージン（直下 `Grid` の `Margin`）: `20px` を維持（ドロップシャドウのボケ足・オフセットがクリッピングされるのを確実に防ぐ）。
   - 片側合計余白: `5px + 20px = 25px`
   - 両側合計余白（水平・垂直）: `(5px + 20px) * 2 = 50px`
3. **縦連続スクロール表示（Continuous モード）のページ間余白**:
   - 既存の `30px` をそのまま維持。

---

## 3. 変更対象ファイルと改修内容

### 3.1 View（XAML）
- **[DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
  - `DetailScrollViewer` の `Padding` 属性を `"20"` から `"5"` に変更。
  - 直下の `Grid` の `Margin="20"` は維持。

### 3.2 ViewModel
- **[DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)**:
  - 定数 `ScrollViewerPadding` を `20.0` から `5.0` に変更。
  - 定数 `TotalHorizontalMargin` および `TotalVerticalMargin` の計算（`(ScrollViewerPadding + PageShadowMargin) * 2` により自動的に 50.0 になる）のドキュメントコメントを更新。
  - `ApplyFitMode()`（`FitToWindow` / `FitToWidth`）の計算が新しい合計マージン 50.0px（片側 25px）に基づいて正しく動作することを確認。

### 3.3 単体テスト
- **[DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)**:
  - 余白変更に伴う `FitToWindow` / `FitToWidth` の計算値アサーションを 50px ベースに更新。
- **[PageNavigationAndViewOptionsTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PageNavigationAndViewOptionsTests.cs)**:
  - ビューポート更新時のフィット倍率テストを新余白に合わせて更新。

### 3.4 設計ドキュメント
- **[basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)**:
  - 詳細エディタのレイアウト仕様におけるパディング・マージンの数値を最新化。

---

## 4. 検証計画

### 4.1 自動テスト
```powershell
dotnet test
```
- 全単体テストが 100% PASS することを確認。

### 4.2 ビルド確認
```powershell
dotnet build
```
- 警告・エラー 0 件でビルドが完了することを確認。
