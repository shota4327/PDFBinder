# Issue #65: ウィンドウ合わせ・幅合わせのスクロールバー表示不具合修正 実装計画

## 概要
詳細エディタ（DetailEditorView）において、「単一ページ表示でウィンドウに合わせたとき（FitToWindow）にもスクロールバーが表示されている」「幅合わせ（FitToWidth）時に水平スクロールバーが誘発される」問題を解決します。

## 原因分析
1. **ページテンプレートのマージンと余白の二重適用**:
   - `DetailPageItemTemplate` のルート要素に `Margin="0,15"` が指定されており、`ScaleTransform`（ズーム）の内側に位置している。
   - `DetailScrollViewer` に `Padding="30"` が設定されているため、単一ページ表示時は外側Padding（上下60px、左右60px）に加えてズーム拡大されるマージン（上下30px × Zoom）が加算されていた。
   - ViewModel の `ApplyFitMode()` ではこのマージンが考慮されておらず、`FitToWindow` 時に高さが必ずビューポートを超過して垂直スクロールバーが表示されていた。
2. **スクロールバー幅の考慮不足**:
   - `FitToWidth` 時に縦スクロールが発生する場合、垂直スクロールバー（約17〜18px）の出現により利用可能幅が狭まるが、それを差し引かずに幅を計算していたため、水平スクロールバーが表示されていた。
3. **WPFレイアウト端数・丸め誤差**:
   - 小数点以下の微小な端数（0.001pxでも超過すると `ScrollBarVisibility="Auto"` によりスクロールバーが出現）に対するセーフティバッファがなかった。

---

## 提案する変更内容

### 1. View（XAML）の改修
#### [MODIFY] [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- `DetailPageItemTemplate` 内のルート `Grid` から `Margin="0,15"` を削除。
- 連続表示（`PagesItemsControl`）でのみページ間に上下マージン（`Margin="0,15"`）が適用されるよう、`ItemsControl.ItemContainerStyle` 等で指定。
- これにより、単一ページ表示時は余計な内部マージンが発生せず、外側の `Padding="30"` のみで上下左右均等に配置される。

### 2. ViewModel の改修
#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- スクロールバー幅（`ScrollBarWidth = 18.0`）およびセーフティバッファ（`SafetyBuffer = 2.0`）の定数を導入。
- `ApplyFitMode()` の計算ロジックを刷新：
  - **FitToWindow（ウィンドウ合わせ）**:
    - 利用可能幅: `Math.Max(50.0, ViewportWidth - horizontalPadding - SafetyBuffer)`
    - 利用可能高さ: `Math.Max(50.0, ViewportHeight - verticalPadding - SafetyBuffer)`
    - `scale = Math.Min(scaleX, scaleY)` により、縦横ともにスクロールバーが出現しない倍率を算出。
  - **FitToWidth（幅合わせ）**:
    - 利用可能幅からセーフティバッファを引いたベース幅で仮スケールを計算。
    - 単一ページ表示時: 拡大後のページ高さが利用可能高さを超えるか判定。
    - 連続表示時: 全ページの合計高さ（+ ページ間マージン）が利用可能高さを超えるか判定。
    - 縦スクロールが発生する場合は、利用可能幅から `ScrollBarWidth` を差し引いて最終的な幅合わせ倍率を算出。
    - これにより、縦スクロールバーが出現しても水平スクロールバーは一切発生しない。

### 3. 単体テストの追加・更新
#### [MODIFY] [DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)
- `FitToWindow` 時にビューポート内に完全に収まる倍率が正しく算出されるテスト。
- `FitToWidth` 時に縦スクロールが発生するケースでスクロールバー幅分が差し引かれて計算されるテスト。
- 単一ページ表示と連続表示モードの各条件下での計算検証テストを追加・更新。

---

## 検証計画

### 自動テスト
- `dotnet test` を実行し、既存テスト（153件）および新規追加テストがすべて 100% PASS することを確認。

### ビルド確認
- `dotnet build` を実行し、警告・エラーなくビルドが成功することを確認。
