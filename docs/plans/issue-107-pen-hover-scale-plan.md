# 拡大縮小連動ペンホバー・プレビューカーソル 実装計画 (Issue #107)

## 1. 概要・背景
詳細エディタ（`DetailEditorView`）において、スタイラスペンやマウスをホバーさせた際、ペンの太さを示すプレビューカーソルが表示されますが、画面のズーム倍率（`Zoom`）を変更してもプレビューカーソルのサイズが拡大・縮小されず、常に100%基準の固定サイズで表示されていました。
その結果、拡大表示時（例: 200%）にプレビューカーソルよりも実際の描画線が太く描画され、WYSIWYG（見た目通りの描画）が損なわれていました。

本改修では、通常ペン・蛍光ペン・部分消しゴム（ピクセル消し）において、現在のズーム倍率（`StrokeThickness * Zoom`）を忠実に反映した高品質なカスタムカーソルを動的生成・表示し、見た目通りの描画・消去体験を実現します。

---

## 2. ユーザー確認事項・決定事項（/grill-me 合意内容）
1. **対象ツール**:
   - **通常ペン（Pen）**: 現在の描画色を反映した塗りつぶし円＋視認性確保のための微細外枠
   - **蛍光ペン（Highlighter）**: 半透明の蛍光色塗りつぶし円＋微細外枠
   - **部分消しゴム（EraserPoint）**: 消去範囲を示す中抜きの輪郭線リング
   - **その他ツール（直線、ストローク消しゴム、選択、手のひら等）**: 既存のカーソル（十字、矢印、手のひら等）を維持
2. **カーソルサイズ制御**:
   - 表示直径: `StrokeThickness * Zoom`
   - クランプ制限: 最小 **3px**（極小ズーム時も見失わない中心点確保）、最大 **128px**（OSカーソル上限および描画負荷抑制）
3. **描画品質・パフォーマンス**:
   - 32-bit ARGB（完全アルファチャンネル透過）によるアンチエイリアス円描画
   - メモリ内での DIB（Device-Independent Bitmap）カーソルストリーム構築により、ネイティブリソースリークを防止
   - 色・太さ・ズーム倍率をキーとしたメモリ内カーソルキャッシュにより、ホバー中やページ切り替え時の無駄な再生成を排除

---

## 3. 作業量の概算
- **新規作成コード**: 約 120 行 (`PenCursorHelper.cs`)
- **既存修正コード**: 約 45 行 (`EditorInkCanvas.cs`, `DetailEditorView.xaml`)
- **単体テストコード**: 約 100 行 (`PenCursorHelperTests.cs`)
- **ドキュメント更新**: 基本設計書 (`docs/basic_design.md`)、機能インベントリ (`docs/PROJECT.md`)
- **想定工数**: 実装・テスト・検証を含め約 30 〜 45 分

---

## 4. 変更対象ファイルと設計詳細

### 4.1 新規作成: `src/PDFBinder.App/Helpers/PenCursorHelper.cs`
- **責務**: 色・太さ・ズーム倍率・ツール種別から、WPF `System.Windows.Input.Cursor` を動的に生成およびキャッシュ。
- **主要メソッド**:
  - `GetCursor(EditorToolMode toolMode, Color color, double strokeThickness, double zoom)`: キャッシュ検索および必要に応じた生成。
  - `CreateCircleCursor(double diameter, Color color, bool isHollow, bool isHighlighter)`: 32bit ARGB ビットマップから標準 `.cur` 形式バイナリを `MemoryStream` に構築し、`new Cursor(stream)` をインスタンス化。
  - `ClearCache()`: テスト・メモリ管理用のキャッシュクリア。

### 4.2 修正: `src/PDFBinder.App/Controls/EditorInkCanvas.cs`
- **`Zoom` 依存関係プロパティ（DependencyProperty）の追加**:
  - `public double Zoom { get; set; }`
  - 変更検知時にカーソル更新（`UpdateCursor()`）をトリガー。
- **カスタムカーソル運用の有効化**:
  - コンストラクタで `UseCustomCursor = true` を設定。
  - `UpdateEditingMode()` および `ApplyDrawingOrStraightLineMode()` 内で、`ToolMode` が Pen, Highlighter, EraserPoint の場合に `PenCursorHelper.GetCursor(...)` を呼び出して `this.Cursor` に設定。

### 4.3 修正: `src/PDFBinder.App/Views/DetailEditorView.xaml`
- `DetailPageItemTemplate` 内の `controls:EditorInkCanvas` に対し、`Zoom` プロパティのバインディングを追加:
  ```xml
  Zoom="{Binding DataContext.Zoom, RelativeSource={RelativeSource AncestorType=UserControl}}"
  ```

### 4.4 新規作成: `tests/PDFBinder.Tests/PenCursorHelperTests.cs`
- `PenCursorHelper.GetCursor` が有効な `Cursor` オブジェクトを返却することの検証。
- 同一パラメータでのキャッシュヒット確認。
- 最小値（3px）および最大値（128px）へのクランプ処理の検証。
- ツールモード（Pen, Highlighter, EraserPoint）ごとの生成検証。
- `EditorInkCanvas` の `Zoom` 変更時にカーソルが適切に追従することの検証。

---

## 5. 検証手順

### 自動テスト
```powershell
dotnet test tests/PDFBinder.Tests/PDFBinder.Tests.csproj
```
- 全単体テストが 100% PASS することを確認。

### ビルド確認
```powershell
dotnet build src/PDFBinder.App/PDFBinder.App.csproj
```
- 警告・エラー 0 件で正常ビルド完了することを確認。

### 手動機能検証
- 詳細エディタで 50%, 100%, 200%, 400% などズーム倍率を変更し、スタイラス／マウスホバー時の円形カーソルサイズが倍率通りに伸縮することを確認。
- ペン描画時に、カーソルの円の大きさと描画されるストロークの太さが完全に一致することを確認。
- 部分消しゴム時に、消去される領域の大きさとカーソル枠線の大きさが完全に一致することを確認。
