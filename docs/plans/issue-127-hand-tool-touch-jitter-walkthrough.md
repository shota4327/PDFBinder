# 検証報告: 移動ツール選択時の指操作によるスクロールのガタつき・振動の解消

- **Issue**: [#127 移動ツール選択時の指操作によるスクロールのガタつき・振動の解消](https://github.com/shota4327/PDFBinder/issues/127)
- **作成日**: 2026-09-18
- **ブランチ**: `fix/issue-127-hand-tool-touch-jitter`

---

## 1. 実施内容の概要
詳細手書きエディタ（`EditorInkCanvas`）において、移動ツール（手のひらツール）選択時にタッチスクリーンを指でドラッグ操作すると画面スクロールが激しくガタつく不具合について、原因の特定・修正および単体テストの拡充を行いました。

### 主な変更点
1. **タッチ／スタイラス昇格マウスイベントの遮断**:
   - `EditorInkCanvas.cs` に `IsTouchPromotedMouseEvent` メソッドを追加。手指タッチ中（`_activeTouchPoints.Count > 0` または `_capturedTouchDevices.Count > 0`）またはタッチから昇格されたマウスイベント（`e.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Touch`）を判定。
   - `OnPreviewMouseDown`、`OnPreviewMouseMove`、`OnPreviewMouseUp` において、上記タッチ由来イベントをマウスパン処理および直線描画から除外。
   - 指操作時はペンツール選択時と同様に、既存の `HandleOneFingerPan` / `HandleTwoFingerPinchZoom` のみが排他的に実行されるよう整理。
2. **物理マウスパン処理の座標系・変位計算の是正**:
   - `StartMousePan`、`ProcessMousePan`、`EndMousePan`、`OnLostMouseCapture` に責務を分離。
   - マウスドラッグ時の座標系を移動するキャンバス自身から固定ビューポート（`_parentScrollViewer`）基準に変更。
   - 変位計算後に `_panStartPoint = currentPos` で基準点を毎フレーム更新し、累積加算による急加速や振動を解消。
3. **単体テストの追加**:
   - `EditorInkCanvasTouchTests.cs` にタッチ昇格イベント判定およびマウスパン差分追従の単体テストを追加。
4. **プロジェクト管理ドキュメントの更新**:
   - `docs/PROJECT.md` の F47（Issue #127 追記）および F50（テスト件数を 330 件に更新）を最新化。

---

## 2. 検証結果

### 自動テスト（`dotnet test`）
```
成功!   -失敗:     0、合格:   330、スキップ:     0、合計:   330、期間: 6 s - PDFBinder.Tests.dll (net10.0)
```
新規追加したテストを含む全 330 件の単体テストがすべて PASS することを確認しました。

### プロジェクトビルド（`dotnet build`）
```
ビルドに成功しました。
    0 個の警告
    0 エラー
```
警告およびエラーなく正常にビルドが完了することを確認しました。
