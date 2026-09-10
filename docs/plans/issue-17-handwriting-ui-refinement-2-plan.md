# 手書きツールUI調整の実装計画 (Issue #17 - 追加改修2)

手書きタブ内のUI調整として、太さ選択の自由選択ボタンを廃止して4種プリセットのみに整理し、カラーパレットを明るい4色（黒・赤・青・緑）へ変更、「全消しゴム」を「全体消し」に改名、そしてツールのアイコン（蛍光ペン、直線、部分消し、手のひら）をWindows Ink標準の正しいアイコンに修正します。

## ユーザー確認済み事項（grill-me 合意事項）
1. **太さ選択**:
   - 「自由選択」ボタンおよびインライン展開スライダーを廃止。
   - 0.5px、1.0px、2.0px、4.0px の4つのドットボタンのみとする。
2. **カラーパレット**:
   - 4色（黒: `#000000`, 鮮やかな赤: `#EF4444`, 鮮やかな青: `#2563EB`, 鮮やかな緑: `#16A34A`）に更新。
3. **消しゴムラベル**:
   - 「全消しゴム」を「全体消し」に変更。
4. **アイコンの修正**:
   - **蛍光ペン**: `&#xED64;`（Windows Ink標準マーカーペン）
   - **直線**: `&#xED5E;`（Windows Ink標準定規・直線アイコン）
   - **部分消し**: `&#xED61;`（Windows Ink標準ピンポイント消しゴム）
   - **手のひら**: `&#xE7C5;`（Windows標準PanMode手のひらアイコン）

## 変更対象ファイル

### ViewModel
- [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
  - `ColorPalette` を指定の4色（黒 `#000000`、赤 `#EF4444`、青 `#2563EB`、緑 `#16A34A`）に更新。
  - 不要となった `IsCustomThicknessOpen` および `ToggleCustomThicknessCommand` を整理。

### UI・XAML
- [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
  - 「自由選択」ボタンおよびインライン展開スライダーパネルを削除。
  - 「全消しゴム」のラベルテキストを「全体消し」に変更。
  - 「蛍光ペン」(`&#xED64;`)、「直線」(`&#xED5E;`)、「部分消し」(`&#xED61;`)、「手のひら」(`&#xE7C5;`) のアイコン文字コードを修正。

### テスト・ドキュメント更新
- [ViewModelsTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ViewModelsTests.cs)
  - `DetailEditorViewModel_PresetAndCustomThickness_WorksCorrectly` のテストを太さプリセット動作テストに調整。
- [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
  - 手書きタブ仕様（太さ4種、明るい4色、全体消し、新アイコン）を同期更新。

## 検証計画
### 自動テスト・ビルド検証
- `dotnet build`: 0エラー・0警告
- `dotnet test`: 全単体テストが 100% PASS すること

### UI目視検証
- 手書きタブの太さ選択が0.5px, 1px, 2px, 4pxの4つのドットのみになっていること。
- カラーパレットが黒・明るい赤・明るい青・明るい緑の4色になっていること。
- 「全体消し」のラベルになっていること。
- 蛍光ペン、直線、部分消し、手のひらのアイコンが意図通りのアイコンで表示されていること。
