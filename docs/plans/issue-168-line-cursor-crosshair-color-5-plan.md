# Issue #168 直線ツール十字線の選択色描画 実装計画（Plan 5）

## 1. 概要・要件
直線ツール選択時のカーソル表示（十字線付き円形プレビュー）について、十字線の色を従来の固定黒色から、現在選択されている描画色（`color`）に動的に連動させて描画するよう変更する。
- ユーザーからの「すぐ戻すかもしれない」という要望に対応するため、`lineColor` 引数（未指定時は `Colors.Black`）として柔軟に設計し、黒色固定への再切り替えも容易に行える設計とする。
- 線の太さ（2px）および長さ（プレビュー円径の2/3倍、最低長7px保証）は維持する。

---

## 2. 変更対象ファイルと方針

### 2.1 `src/PDFBinder.App/Helpers/PenCursorHelper.cs`
- `CreateStraightLineCursor`:
  - `DrawCrosshairLines` 呼び出し時に第8引数として `color` を渡す。
- `DrawCrosshairLines`:
  - 引数に `Color? lineColor = null` を追加（デフォルトは `Colors.Black`）。
  - `DrawLineRect` へ `lineColor ?? Colors.Black` を渡す。
- `DrawLineRect` / `ApplyLinePixel`:
  - `ApplyBlackLinePixel` を `ApplyLinePixel` に汎用化し、引数で渡された `Color` の RGB 値を用いてサブピクセルアルファ合成を行う。

### 2.2 `tests/PDFBinder.Tests/PenCursorHelperTests.cs`
- `DrawCrosshairLines_RendersExpectedPixels`:
  - 選択色（例: `Colors.Red`）を指定して呼び出し、十字線が赤色（R=255, G=0, B=0, A>0）で描画されることを検証。
  - デフォルト引数（未指定）時は黒色で描画されることも併せて検証。

### 2.3 ドキュメント同期
- `CHANGELOG.md`: 十字線の色が選択色に連動する旨を更新。
- `docs/basic_design.md`: 6.6節の十字線仕様（選択色連動描画）を反映。

---

## 3. 検証方針
- `dotnet build`: ビルドエラー・警告ゼロの確認。
- `dotnet test`: 全単体テストが 100% PASS することの確認。
