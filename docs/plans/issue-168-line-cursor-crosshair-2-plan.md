# 実装計画: 直線ツール選択時のカーソルを十字クロスヘア方式へ変更 (Issue #168 - 第2版)

## 1. 概要
直線ツール選択時のカーソル表示について、右上の定規バッジ方式を廃止し、円形プレビュー（ペンの色・太さ・透明度を反映）の上下左右に「＋」の字を形成する4本の黒色直線（クロスヘア／レティクル）を配置するデザインへ全面的に刷新します。

---

## 2. 決定した設計仕様（/grill-me による合意事項）

1. **定規バッジの廃止**:
   - 右上に表示していた定規アイコン（ルーラーバッジ）を完全に削除。
2. **十字線（クロスヘア）のデザイン**:
   - 色: シンプルな黒色単色（`#000000`、不透明度 100%）。
   - 太さ: 約1px（すっきりとした細線）。
   - 長さ: 各方向 約7px（「＋」の字であることが明瞭に認識できる長さ）。
3. **円形プレビューとの余白・追従挙動**:
   - 円形プレビューの外周（半径 `radius`）から **約3〜4px** の余白を空けた位置から十字線を開始。
   - 上: `(cy - radius - gap - length)` から `(cy - radius - gap)`
   - 下: `(cy + radius + gap)` から `(cy + radius + gap + length)`
   - 左: `(cx - radius - gap - length)` から `(cx - radius - gap)`
   - 右: `(cx + radius + gap)` から `(cx + radius + gap + length)`
   - ペンの太さやズーム倍率によって円が拡大・縮小しても、線が円と重なることなく常に円の外側に一定間隔を保って追従。
4. **ホットスポット（描画開始点）**:
   - 十字線と円形プレビューの幾何学的中心 `(cx, cy)` をカーソルのホットスポットとし、マウスをクリックした位置から寸分狂わず直線が描画開始される。

---

## 3. 実装ステップ

### Step 1: `PenCursorHelper.cs` の十字クロスヘア描画への更新
- `DrawRulerBadge` および関連する定規サンプリングメソッドを撤廃。
- `CreateStraightLineCursor(double diameter, Color color, bool isHighlighter)` を十字クロスヘア描画用に更新:
  - 円の半径 `radius = diameter / 2.0`
  - 余白 `gap = 3.5px`
  - 線の長さ `lineLen = 7.0px`
  - 必要なカーソルキャンバスサイズ: `hotspot = (int)Math.Ceiling(radius + gap + lineLen) + 2`、`size = hotspot * 2`
  - `RenderCirclePixels` で中心に円形プレビューを描画
  - 新設メソッド `DrawCrosshairLines(byte[] pixels, int size, int hotspot, double radius, double gap, double lineLen)` により上下左右に1px幅の黒線を描画
- キャッシュキーの整合性を維持。

### Step 2: 単体テストの追加・更新
- `PenCursorHelperTests.cs`:
  - `DrawRulerBadge_RendersExpectedPixels` を十字線の描画検証テスト `DrawCrosshairLines_RendersExpectedPixels` に更新。
  - 上下左右の4方向に黒色ピクセルが存在し、中心の円と線の間に透過（または円プレビュー）の隙間が存在することを検証。
  - 各サイズでのカーソル生成テスト、ズーム・太さ・蛍光ペン連動テストがすべて PASS することを確認。

### Step 3: ドキュメントの同期
- `CHANGELOG.md`: エンドユーザー向けリリースノートの記述を定規アイコンから十字クロスヘア表示へ更新。
- `docs/basic_design.md`: 基本設計書のカーソル仕様を十字クロスヘア表示へ更新。
- `README.md`: 直線トグルの説明を十字クロスヘア表示へ更新。
- `docs/PROJECT.md`: 機能インベントリの F67 の説明を更新。

---

## 4. 検証計画
- `dotnet test`: 既存および新設の全単体テストが 100% PASS すること。
- `dotnet build`: 警告・エラー 0件でビルド成功すること。
- 検証報告書 `docs/plans/issue-168-line-cursor-crosshair-2-walkthrough.md` を作成。
