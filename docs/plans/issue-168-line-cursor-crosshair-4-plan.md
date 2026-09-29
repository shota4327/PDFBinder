# Issue #168 直線ツール十字線の長さ（プレビュー円の2/3倍）調整 実装計画（Plan 4）

## 1. 概要・要件
直線ツール選択時のカーソル表示（十字クロスヘア線付き円形プレビュー）について、十字線の太さ（2px）は維持したまま、線の長さの伸長比率を**中央の円形プレビューの 2/3（約66.7%）の大きさ**へと調整する。
- 最低長は現状通り 7.0px（`minLineLen = 7.0`）を保証（極小ペン・縮小時でも十字線の認識性を維持）。
- プレビュー円の長さ（直径 `diameter`）が大きくなった場合の伸長計算を `Math.Max(minLineLen, diameter * (2.0 / 3.0))` に変更する。

---

## 2. 変更対象ファイルと方針

### 2.1 `src/PDFBinder.App/Helpers/PenCursorHelper.cs`
- `CreateStraightLineCursor`:
  - `double lineLen = Math.Max(minLineLen, diameter * (2.0 / 3.0));` へ変更。
  - XMLドキュメントコメント等の同期。

### 2.2 `tests/PDFBinder.Tests/PenCursorHelperTests.cs`
- `CreateStraightLineCursor_ScalesLineLengthWithDiameter`:
  - プレビュー円の 2/3 の比率で伸長されるテストケースへと期待値を更新（例: 直径 12px → 8px、直径 24px → 16px、最低長 7px 保証）。

### 2.3 ドキュメント同期
- `CHANGELOG.md`: 十字線の長さについてプレビュー円の 2/3 の大きさ（最低長 7px）に調整した旨を更新。
- `docs/basic_design.md`: 6.6節の十字線仕様（プレビュー円径の 2/3 連動伸長）を反映。

---

## 3. 検証方針
- `dotnet build`: ビルドエラー・警告ゼロの確認。
- `dotnet test`: 全単体テストが 100% PASS することの確認。
