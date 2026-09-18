# 実装計画書 - Issue #106: 回転時の即時プレビュー回転（歪み・引き伸ばし防止） (追加改修-2)

## 1. 概要・背景
Issue #106 の初期対応により、手書きインク回転の高速化および回転後のFitMode拡大率追従を実現しました。
しかし、回転操作を行った直後、バックグラウンドでのPDFium高解像度レンダリングが完了するまでの間、**回転前の画像（縦長）が回転後のグリッド枠（横長）に合わせて無理やり引き伸ばされて表示され、一時的に歪みが生じる**という課題が確認されました。

本追加改修では、WPFの `TransformedBitmap` を活用し、回転ボタンを押した瞬間にメモリ上で既存画像（`PageBackground` および `Page.Thumbnail`、`StrokeCache`）を 0ms で即座に幾何回転させて仮表示します。これにより、一切の引き伸ばしや歪みを発生させず、回転直後から高精細かつ正しい向きのプレビューを表示し、その後にバックグラウンドで完成した正式なレンダリング結果へシームレスに差し替える最高水準のUXを実現します。

---

## 2. ユーザー合意事項（/grill-me による決定事項）
1. **仮プレビューの表示方式**:
   - 現在表示中の高解像度画像（`PageBackground`）およびサムネイル（`Page.Thumbnail`）、ストロークキャッシュ（`StrokeCache`）をメモリ上で即時幾何回転（`TransformedBitmap`）させて仮表示し、裏でPDFium再レンダリング完了後に差し替える。
   - 回転直後も画質を落とさず、引き伸ばしを完全防止する。
2. **グリッド俯瞰ビューでの適用**:
   - 詳細エディタだけでなく、グリッド俯瞰ビューにおける回転操作（ツールバー・ショートカット・Undo/Redo）時にもサムネイルの即時幾何回転を適用し、0msでカードが回転する超高速レスポンスを実現する。

---

## 3. 変更計画と設計詳細

### 3.1 `PDFBinder.Core` (コアライブラリ)

#### [MODIFY] [PdfPageModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PdfPageModel.cs)
- `RotateTo(PageRotation newRotation)` において、手書きインクの回転に加えて、既存の `Thumbnail` が存在する場合は差分角度（`deltaDeg`）に応じた `TransformedBitmap` を即座に生成して `Thumbnail` を更新。
- これにより、グリッドビュー、詳細ビュー、Undo/Redo の全操作経路でサムネイルが 0ms で正しく回転する。

#### [NEW] [BitmapTransformHelper.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Helpers/BitmapTransformHelper.cs)
- ビットマップの即時幾何回転を行う共通ヘルパークラスを作成：
  - `BitmapSource? CreateRotatedBitmap(BitmapSource? source, int deltaDegrees)`: `RotateTransform` と `TransformedBitmap` を用いて、元のビットマップを回転させたフリーズ済み `BitmapSource` を安全に生成。

---

### 3.2 `PDFBinder.App` (WPF UI層)

#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- ページ回転検知時（`OnCurrentPagePropertyChanged` や回転実行時）において、回転対象の `DetailPageItemViewModel` が保持している既存の `PageBackground` および `StrokeCache` を `BitmapTransformHelper.CreateRotatedBitmap` で即座に幾何回転させて差し替え。
- レンダリング完了フラグ（`LastRenderedRotation`）は更新前の状態を保つことで、直後の `ScheduleDynamicRender` による正規の PDFium 高解像度レンダリングが確実に実行され、完成後にシームレスに更新されるようにする。

---

### 3.3 `PDFBinder.Tests` (単体テスト)

#### [NEW] [BitmapTransformHelperTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/Helpers/BitmapTransformHelperTests.cs)
- `CreateRotatedBitmap` による画像回転の動作検証（90度、180度、270度、null安全、寸法反転の整合性）。

#### [MODIFY] [DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs)
- ページ回転時に `PageBackground` および `Thumbnail` が即座に回転された画像へ差し替えられることの検証。

---

## 4. 検証計画

### 4.1 自動テスト
- `dotnet test`: 全単体テストが 100% PASS することを確認。

### 4.2 ビルド確認
- `dotnet build`: エラーおよび警告がないことを確認。
