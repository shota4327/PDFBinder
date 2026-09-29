# Issue #168 直線ツール十字線の太さ・長さ自動追従化 実装計画（Plan 3）

## 1. 概要・要件
直線ツール選択時のカーソル表示（十字クロスヘア線付き円形プレビュー）について、ユーザーからの追加フィードバックに基づき以下の2点を調整・拡張する:
1. **十字線の太さを 2px に変更**:
   - `lineThickness = 2.0`（従来は 1.0px）とし、太くして視認性を高める。
2. **十字線の長さの自動追従化**:
   - 現状の長さ（7.0px）を最低保証の長さ（`minLineLength = 7.0`）とする。
   - 中央の円形プレビューの長さ（直径 `diameter`）より短くならないよう、`Math.Max(7.0, diameter)` としてプレビュー円の大きさに応じて自動的に長く伸長させる。
   - 大きなプレビュー円（太いペンや拡大ズーム時）でも、線の長さが円の直径以上を維持し、常に「＋」の字として綺麗にバランスの取れた形状を保つ。
   - カーソル画像サイズが Windows CUR フォーマットの上限（256x256）を超えないよう `hotspot` を `Math.Clamp(needed, 6, 128)` で保護する。

---

## 2. 変更対象ファイルと方針

### 2.1 `src/PDFBinder.App/Helpers/PenCursorHelper.cs`
- `CreateStraightLineCursor`:
  - `const double lineThickness = 2.0;`
  - `const double minLineLen = 7.0;`
  - `double lineLen = Math.Max(minLineLen, diameter);`
  - `needed` 計算および `int hotspot = Math.Clamp(needed, 6, 128);`
  - `DrawCrosshairLines` への引数渡しとデフォルト値更新（`thickness = 2.0`）
  - XMLドキュメントコメントを「十字線付き円形プレビューカーソル」に修正
- `DrawCrosshairLines`:
  - デフォルト引数 `double thickness = 2.0`

### 2.2 `tests/PDFBinder.Tests/PenCursorHelperTests.cs`
- `DrawCrosshairLines_RendersExpectedPixels`:
  - 線の太さ 2px に対応した検証（`hotspot` に加え、隣接ピクセルにも黒線ピクセルが描画されていることの確認）。
- `CreateStraightLineCursor_ScalesLineLengthWithDiameter`:
  - 小さい直径（3px）のときに最低長（7px）が維持されることの検証。
  - 大きい直径（14px, 20px）のときに線の長さが直径と同等以上になることの検証。
- `CreateStraightLineCursor_ProducesValidCursor_ForVariousSizes`:
  - 極小から最大（128px）まで正常に Cursor インスタンスが生成されることの検証。

### 2.3 ドキュメント同期
- `CHANGELOG.md`: 十字線の太さ2px化およびプレビュー円径に合わせた線の長さ自動伸長について追記。
- `docs/basic_design.md`: 十字線の仕様（太さ2px、最低長7pxかつプレビュー円径連動自動伸長）を反映。

---

## 3. 検証方針
- `dotnet build`: ビルドエラー・警告ゼロの確認。
- `dotnet test`: 全単体テストが 100% PASS することの確認。
