# 実装計画: 詳細エディタのカラーパレットに黄色を追加 (Issue #19)

手書き詳細エディタのカラーパレットに「黄色」を追加し、蛍光ペンのデフォルト色としても統一適用します。

## 確定仕様
- **色合い**: Tailwind Yellow 500 (`#EAB308` / R:234, G:179, B:8)
- **パレット配置**: 末尾（黒 → 赤 → 青 → 緑 → 黄）の計5色
- **蛍光ペンデフォルト色**: 新しい黄色（`#EAB308`）
- **ツール切替挙動**: 蛍光ペンからペンへの切替時、黄色が選択されていた場合は自動的に黒（`#000000`）へ復帰

---

## 変更対象ファイル

### 1. `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- `ColorPalette` に 5番目のプリセット色として `Color.FromRgb(0xEA, 0xB3, 0x08)` を追加。
- `OnSelectedToolChanged` において：
  - 蛍光ペン選択時に `SelectedColor == Colors.Black` であれば新しい黄色（`#EAB308`）を設定。
  - ペン選択時に `SelectedColor == Color.FromRgb(0xEA, 0xB3, 0x08)` であれば黒（`Colors.Black`）へリセット。
- コメントの更新（「黒・赤・青・緑・黄」）。

### 2. `tests/PDFBinder.Tests/ViewModelsTests.cs`
- `DetailEditorViewModel_ColorPalette_ContainsFourModernColors` を `DetailEditorViewModel_ColorPalette_ContainsFiveModernColors` へ更新し、要素数 5 および 5番目の色が `#EAB308` であることを検証。
- `DetailEditorViewModel_SelectedTool_Changed_UpdatesColorAndThickness` の期待値を `Colors.Yellow` から `Color.FromRgb(0xEA, 0xB3, 0x08)` へ更新。

### 3. ドキュメント類の更新
- `docs/basic_design.md`: カラーパレットの記載（5色: 黒・赤・青・緑・黄）を最新化。

---

## 検証手順
1. `dotnet build` でビルドエラー・警告がないことを確認。
2. `dotnet test` で単体テストがすべて PASS することを確認。
