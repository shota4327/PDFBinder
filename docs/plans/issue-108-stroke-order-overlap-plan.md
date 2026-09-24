# [実装計画] Issue #108: 手書きストロークの重なり順（Z-Order）保持および蛍光ペン重ね塗り濃色化

## 概要
現在の PDF Binder では、WPF標準の `DrawingAttributes.IsHighlighter = true` の動作仕様により、蛍光ペンが自動的に通常のペンストロークの最背面に回ってしまい、また同色の蛍光ペンを何度重ねても色が濃くならないという課題がありました。
本改修では、蛍光ペンストロークを `IsHighlighter = false` かつ半透明カラー（アルファ値 120 / 約47%）として管理・描画することで、ペンのストロークと同一の完全な時系列順（Z-Order）で重なりを保持し、重ね塗りによって色が濃くなる自然な描画挙動を実現します。

## ユーザー確認・合意事項
1. **蛍光ペンの描画方式**:
   - `DrawingAttributes.IsHighlighter = false` かつ `Color.A = 120`（半透明）を採用。
   - ストロークの描画順序はペン・蛍光ペン問わず完全な時系列追加順（Z-Order）とする。
2. **既存PDF・過去ストロークの後方互換性**:
   - 既存PDFの注釈読み込み時、過去のストローク（`IsHighlighter == true`）の属性は強制書き換えを行わずそのまま保持し、新しく描画されるストロークから新方式を適用する。
3. **ストロークレンダリング処理（最小差分）**:
   - すべての新ストロークで `IsHighlighter = false` を設定するため、WPF標準の `strokes.Draw(dc)` で自動的に時系列インデックス順（2パス目の通常ストローク処理）に描画されます。そのため `StrokeCacheService.cs` および `PdfiumRenderer.cs` は変更せず既存コードを維持します。
4. **透明度設定**:
   - 現行標準値であるアルファ値 120（不透明度 約47%）をそのまま維持。
5. **カラーパレットUI整合性**:
   - ViewModelの `SelectedColor` は不透明色（A=255）を保持し、パレットUIの一致判定・チェックマーク表示を破損させない。`EditorInkCanvas` 側で描画属性適用時に半透明化を行う。

## 変更内容の設計

### 1. エディタ描画コントロール (`PDFBinder.App`)

#### [MODIFY] [EditorInkCanvas.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- `ApplyDrawingAttributes()` メソッドにおいて、`ToolMode == EditorToolMode.Highlighter` の場合に `IsHighlighter = false` とし、`Color` にアルファ値 120 を設定。
- `OnRender()` の直線プレビュー描画において、`ToolMode == EditorToolMode.Highlighter` または `attr.Color.A < 255` に応じて正しい半透明プレビューを表示。

### 2. PDF出力・注釈保存サービス (`PDFBinder.Core`)

#### [VERIFY] [PdfBinderInkAnnotation.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PdfBinderInkAnnotation.cs)
- 既に `foreach (var stroke in strokes)` で時系列順に描画しており、`byte alpha = attr.IsHighlighter ? (byte)120 : mediaColor.A;` となっているため、新ストローク（`mediaColor.A == 120`, `IsHighlighter == false`）および旧ストローク（`IsHighlighter == true`）の双方が矛盾なくアルファ値 120 でPDFへ出力されることを確認（必要に応じて微調整）。

### 3. 単体テストの追加・更新 (`PDFBinder.Tests`)

#### [MODIFY] [DetailEditorStraightLineTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorStraightLineTests.cs)
- 蛍光ペン選択時の `canvas.DefaultDrawingAttributes.IsHighlighter` を検証していたテストを、新仕様（`IsHighlighter == false` かつ `Color.A == 120`）の検証に更新。

#### [NEW] [StrokeOrderAndOverlapTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/StrokeOrderAndOverlapTests.cs)
- 蛍光ペンの描画属性（`IsHighlighter == false`, `A == 120`）の生成検証。
- ペンストロークの上に蛍光ペンストロークを描画した際、時系列順（Z-Order）が保持されてレンダリングされることの検証。
- 蛍光ペンを複数回重ね塗りした際のピクセル色変化（濃色化）の検証。
- 既存の `IsHighlighter == true` ストロークと新規ストロークが混在した場合の後方互換性検証。

## 検証計画

### 自動テスト（Automated Tests）
- `dotnet test` を実行し、既存テストおよび新規追加テストが 100% PASS することを確認。

### ビルド確認
- `dotnet build` を実行し、警告およびエラーなく成功することを確認。
