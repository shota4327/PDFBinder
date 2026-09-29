# Issue #168 直線ツール十字線の太さ2px化・長さ自動追従化 検証報告（Walkthrough 3）

## 1. 概要
直線ツール選択時のカーソル表示（十字線付き円形プレビュー）について、ユーザーからの追加要望に基づき、十字線の太さを 2px に変更するとともに、線の長さを最低 7px としつつ中央プレビュー円の長さ（直径 `diameter`）より短くならないようプレビュー円の大きさに応じて自動伸長するよう改善しました。

---

## 2. 変更内容の詳細

### 2.1 カーソル生成ロジックの改善 ([`PenCursorHelper.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Helpers/PenCursorHelper.cs))
- **太さを 2px に変更**:
  - `const double lineThickness = 2.0;`
  - `DrawCrosshairLines` のデフォルト引数を `double thickness = 2.0` に変更。
  - `cx - halfThick`（`halfThick = 1.0`）によるサブピクセルカバレッジ計算により、端のピクセルに適切なアンチエイリアス処理を適用しつつ、くっきりと太い視認性の高い十字線を描画。
- **プレビュー円径連動の長さ自動伸長**:
  - `const double minLineLen = 7.0;`
  - `double lineLen = Math.Max(minLineLen, diameter);`
  - 細いペン（直径 3px〜6px）のときは最低保証の 7px を維持して「＋」の認識性を確保。
  - 太いペンや拡大ズーム（直径 12px, 24px, 48px 等）のときは、中央の円形プレビューの長さ（直径）に合わせて線の長さを自動伸長し、常にプレビュー円とバランスの取れた綺麗な十字形状を維持。
  - 大きなカーソルサイズでも Windows CUR フォーマットの最大仕様（256x256）を超えないよう `hotspot` を `Math.Clamp(needed, 6, 128)` で保護。

### 2.2 単体テストの追加・更新 ([`PenCursorHelperTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PenCursorHelperTests.cs))
- `DrawCrosshairLines_RendersExpectedPixels`:
  - 線の太さ 2px に連動して、`hotspot` の中心列だけでなく隣接ピクセル列（`hotspot - 1`, `hotspot + 1`）にもアンチエイリアスピクセルが正しく描画されていることを検証。
- `CreateStraightLineCursor_ScalesLineLengthWithDiameter`:
  - 直径 3.0, 6.0, 12.0, 24.0 の各サイズにおいて、線の長さが最低長（7.0）以上かつ直径以上（`Math.Max(7.0, diameter)`）に自動伸長されること、および正常に Cursor インスタンスが生成されることを検証。

### 2.3 ドキュメントの同期更新
- [`CHANGELOG.md`](file:///c:/Git/PDFBinder/CHANGELOG.md): 十字線の太さ2px化およびプレビュー円径連動自動伸長（最低長7px保証）について追記。
- [`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md): 6.6節に十字線の太さ2pxおよびプレビュー円径連動自動伸長仕様を反映。

---

## 3. 検証結果

### 3.1 ビルド検証
- コマンド: `dotnet build`
- 結果: **成功（エラー: 0、警告: 0）**

### 3.2 単体テスト検証
- コマンド: `dotnet test`
- 結果: **合計 525 件のテストがすべて合格（失敗: 0、合格: 525、スキップ: 0）**

---

## 4. 今後のステップ
- ユーザーに太さ2pxおよび長さ自動伸長の動作・外観を確認いただく。
- ユーザーからの明示的な指示を受け次第、PR作成・マージへ進む。
