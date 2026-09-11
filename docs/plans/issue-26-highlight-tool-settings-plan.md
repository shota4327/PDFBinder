# 実装計画: ペン・蛍光ペン・部分消しゴムの太さ・色設定の強調表示と状態保持 (#26)

## 概要
手書きエディタ（詳細編集モード）において、現在選択されている「太さ」および「色」のツールバーボタンを視覚的に強調表示します。
また、ペン・蛍光ペン・部分消しゴムの初期値設定を行い、各ツールを相互に切り替えた際にそれぞれの直前の設定（色および太さ）を独立して記憶・保持する機能を実装します。
さらに、ツール種別に応じて太さ・色設定の有効/無効（IsEnabled）をきめ細かく制御します。

---

## ユーザー確認・合意事項（/grill-me による決定事項）

1. **状態保持の範囲**:
   - ペン、蛍光ペン、部分消しゴム（EraserPoint）で、直前の状態をそれぞれ独立して記憶・保持する。
   - ペン: 「色」と「太さ」を保持
   - 蛍光ペン: 「色」と「太さ」を保持
   - 部分消しゴム: 「太さ」を保持（色は不要）
2. **太さ・色の初期値**:
   - ペン: 初期太さ `1.0px`、初期色 `黒 (#000000)`
   - 蛍光ペン: 初期太さ `12.0px`、初期色 `黄色 (#EAB308)`
   - 部分消しゴム: 初期太さ `12.0px`（形状は円形 `EllipseStylusShape`）
3. **太さプリセットの仕様**:
   - ペン選択時: `0.5px`, `1.0px`, `2.0px`, `4.0px`
   - 蛍光ペン・部分消しゴム選択時: `8.0px`, `12.0px`, `16.0px`, `24.0px`
   - ツール切り替え時にツールバーのプリセット表示（ドットサイズおよびツールチップ）が動的に切り替わる。
4. **強調表示のビジュアルデザイン**:
   - **太さボタン**: リボンタブのトグルボタンと同様に、選択中の太さボタンの背景を薄い青色（`#DBEAFE`）、枠線をアクセントブルー（`#2563EB`）で強調する。
   - **カラーパレット**: 選択中の色の円の内部にチェックマーク（✓）を表示する（明るい色は濃色文字、暗い色は白色文字で自動判別）。
5. **ツールの有効/無効制御（IsEnabled）**:
   - **太さ設定**: ペン、蛍光ペン、部分消しゴム選択時に有効（`CanChangeThickness = true`）。全体消し（EraserStroke）、選択、手のひらツール時は無効（グレーアウト）。
   - **カラーパレット**: ペン、蛍光ペン選択時のみ有効（`CanChangeColor = true`）。部分消しゴム、全体消し、選択、手のひらツール時は無効（グレーアウト）。

---

## 変更対象ファイルと設計詳細

### 1. `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- **フィールド追加**:
  - `_penColor`（初期値 `Colors.Black`）、`_penThickness`（初期値 `1.0`）
  - `_highlighterColor`（初期値 `YellowPresetColor`）、`_highlighterThickness`（初期値 `12.0`）
  - `_eraserThickness`（初期値 `12.0`）
- **プロパティ追加・更新**:
  - `StrokeThickness` の初期値を `1.0` に変更
  - `SelectedColor` の初期値を `Colors.Black` に変更
  - `CanChangeThickness => SelectedTool == EditorToolMode.Pen || SelectedTool == EditorToolMode.Highlighter || SelectedTool == EditorToolMode.EraserPoint`
  - `CanChangeColor => SelectedTool == EditorToolMode.Pen || SelectedTool == EditorToolMode.Highlighter`
  - `ActiveThicknessPresets`（ペン用と蛍光ペン/部分消し用のプリセット切り替えコレクション）
    - 太さ（`Thickness`）、アイコン円サイズ（`DotSize`）、ツールチップ（`ToolTip`）を持つ `ThicknessPresetOption`
- **ロジック更新**:
  - `OnSelectedColorChanged`: ペン選択時は `_penColor`、蛍光ペン選択時は `_highlighterColor` を更新
  - `OnStrokeThicknessChanged`: ペン時は `_penThickness`、蛍光ペン時は `_highlighterThickness`、部分消しゴム時は `_eraserThickness` を更新
  - `OnSelectedToolChanged`:
    - ツール切り替え時、記憶している色および太さを `SelectedColor` / `StrokeThickness` に復元
    - `ActiveThicknessPresets` を更新（ペン用、または蛍光ペン/部分消し用）
    - `CanChangeThickness`, `CanChangeColor`, `CanToggleStraightLine` の PropertyChanged 通知を発行

### 2. `src/PDFBinder.App/Views/DetailEditorView.xaml.cs`
- `ApplyDrawingAttributes` にて、`vm.SelectedTool == EditorToolMode.EraserPoint` の場合に `InkCanvas.EraserShape = new EllipseStylusShape(vm.StrokeThickness, vm.StrokeThickness)` を適用。

### 3. `src/PDFBinder.App/Converters/CommonConverters.cs`
- **コンバーター追加**:
  - `DoubleEqualsToBooleanConverter`: 現在の `StrokeThickness` とプリセット値の一致判定用
  - `ColorEqualsToVisibilityConverter`: 現在の `SelectedColor` とパレット色の一致を判定し、チェックマークの表示/非表示（`Visible`/`Collapsed`）を返す
  - `ColorToContrastingBrushConverter`: 色の輝度（Luminance）に基づいて白または黒の `SolidColorBrush` を返す（チェックマークの視認性確保）

### 4. `src/PDFBinder.App/App.xaml`
- コンバーターのリソース定義を追加
- `ThicknessButtonStyle` の拡張・更新: 選択中状態での背景色（`#DBEAFE`）および枠線色（`#2563EB`）のスタイル定義

### 5. `src/PDFBinder.App/MainWindow.xaml`
- 太さプリセットボタン群を `ActiveThicknessPresets` にバインドし、選択状態スタイルを適用
- 太さグループの `IsEnabled` を `DetailEditor.CanChangeThickness` にバインド
- カラーパレットのボタングリッド内にチェックマーク（`TextBlock Text="✓"`）を配置し、選択時のみ表示
- カラーパレットグループの `IsEnabled` を `DetailEditor.CanChangeColor` にバインド

### 6. `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs`
- ペン（1.0px / 黒）、蛍光ペン（12.0px / 黄）、部分消しゴム（12.0px）の初期値検証
- ペン ⇔ 蛍光ペン ⇔ 部分消しゴムのツール切り替え時の状態保持（直前の色・太さが独立して保持されること）のテスト
- `CanChangeThickness`, `CanChangeColor` の判定テスト
- 太さプリセットの切り替えテスト

---

## 検証手順
1. **自動テスト**:
   - `dotnet test`: すべての既存テストおよび新規追加テストが 100% PASS することを確認
2. **ビルド検証**:
   - `dotnet build`: 警告・エラーなく成功することを確認
