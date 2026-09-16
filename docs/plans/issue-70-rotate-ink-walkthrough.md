# Issue #70: ページ回転時の手書きインク追従回転 検証報告（Walkthrough）

## 概要
PDFページを回転した際、手書きインク（`InkStrokes`）を用紙の回転（時計回り90度、反時計回り90度、180度）に連動して幾何学的に追従回転させる機能を実装しました。
これにより、グリッドサムネイル一覧、詳細エディタ画面、Undo/Redo（元に戻す/やり直し）、およびPDF保存・再読込の全領域において手書きインクと用紙表示の整合性を完全に保証しました。

---

## 変更内容のサマリー

### 1. `PDFBinder.Core`
- **`Helpers/InkTransformHelper.cs` [NEW]**:
  - `RotateStrokes`: 差分回転角度（`PageRotation`）および現在の表示寸法をもとに、ストロークコレクション内の各ストロークの頂点座標（`StylusPoints`）およびペン先寸法（`DrawingAttributes`）を幾何学的に回転変換するヘルパー。
  - `TransformPoint`: $(x, y)$ 座標を指定角度（90°, 180°, 270°）で新しい表示矩形領域に合わせて変換する数理関数。筆圧（`PressureFactor`）も完全保持。
  - `strokes.Clear()` および `strokes.Add(s)` による確実な `StrokesChanged` イベント伝播。
- **`Models/PdfPageModel.cs` [MODIFY]**:
  - `RotateTo(PageRotation newRotation)` メソッドを新設。現在の回転と目的の回転の差分角度を算出し、`InkTransformHelper.RotateStrokes` でインクを回転させた後にページ回転角度を更新。
  - `RotateClockwise()` および `RotateCounterClockwise()` を `RotateTo` 呼び出しに統一。
- **`Services/UndoRedoService.cs` [MODIFY]**:
  - `RotatePageCommand.Execute()` および `Undo()` を `_page.RotateTo(...)` 経由に更新。Undo 時は幾何学的逆変換（+90°の取り消し時は -90°回転）により元の位置・向きに完全復元。

### 2. `PDFBinder.App`
- **`ViewModels/MainViewModel.cs` [MODIFY]**:
  - `Undo()` および `Redo()` 実行時に、アクティブなビューに応じてサムネイルの再生成（`EnsureThumbnailsGeneratedAsync()`）または詳細ビューの動的再レンダリング（`DetailEditor.ScheduleDynamicRender(immediate: true)`）を即時実行するように強化。
  - `OnMasterStrokesChanged` のイベント発火により、詳細エディタの消しゴム/選択モード（`EditorInkCanvas.Strokes`）およびペンモード（`StrokeCache`）の両方で回転後のストロークが直ちに同期。

### 3. ドキュメント同期
- **`docs/basic_design.md`**: `PdfPageModel` の回転メソッド・インク追従仕様および `InkTransformHelper` の仕様を追記。
- **`docs/PROJECT.md`**: 機能インベントリ F11 を手書きインク追従回転対応・完了に更新。

---

## 検証結果

### 1. 単体テスト（xUnit）
新規テストクラス `InkRotationTests.cs` を作成し、全テストの成功を確認しました。
- `TransformPoint_CalculatesCorrectCoordinates`: 90°, 180°, 270°, 0° の各座標変換の正当性
- `RotateStrokes_FourConsecutiveClockwiseRotations_RestoresOriginalCoordinates`: 4回連続の時計回り90度回転による原点座標・筆圧・ペン先寸法の完全復元
- `PdfPageModel_RotateClockwise_RotatesInkAndUpdatesRotation`: モデル層での回転・変更フラグ・サムネイルダーティフラグ・表示寸法の連動
- `RotatePageCommand_ExecuteAndUndo_RotatesAndRestoresInk`: コマンド経由での回転、Undo（元に戻す）時の完全復元、Redo（やり直し）時の再回転
- `RotatePageAndSave_ReloadsPreservedInkAtRotatedPosition`: ページ回転後のPDF保存および再読み込み時における回転角度および手書きインク位置の完全保持

```powershell
テストの実行に成功しました。
テストの合計数: 248
     成功: 248
合計時間: 2.1892 秒
ビルドに成功しました。
    0 個の警告
    0 エラー
```
既存の全240件のテストに加え、新規追加8件のテストすべてが PASS することを確認しました。
