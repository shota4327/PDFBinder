# 実装計画: 移動ツール選択時の指操作によるスクロールのガタつき・振動の解消

- **Issue**: [#127 移動ツール選択時の指操作によるスクロールのガタつき・振動の解消](https://github.com/shota4327/PDFBinder/issues/127)
- **作成日**: 2026-09-18
- **ブランチ**: `fix/issue-127-hand-tool-touch-jitter`

---

## 1. 課題の概要
手書き詳細エディタ（`EditorInkCanvas`）において、移動ツール（手のひらツール：`EditorToolMode.Hand`）を選択している状態でタッチスクリーンを指でドラッグ操作すると、画面スクロールが激しく振動・ガタついて正常に動作しない。
一方、ペンツール（`EditorToolMode.Pen`）選択時には指でのドラッグ操作（パン）が滑らかに動作する。

## 2. 根本原因
1. **タッチからマウスイベントへの自動昇格（Touch-to-Mouse Promotion）による重複競合**:
   - WPFでは指でタッチ・ドラッグすると、タッチイベント（`TouchDown`, `TouchMove`, `TouchUp`）と互換用のマウスイベント（`PreviewMouseDown`, `PreviewMouseMove`, `PreviewMouseUp`）が同時に発火する。
   - ペンツール選択時はマウス側のパン処理（`ToolMode == Hand`）がスキップされ、タッチ専用処理（`HandleOneFingerPan`）のみが動作するため滑らかである。
   - しかし移動ツール選択時は、マウスイベント側でも `ToolMode == Hand` に合致して `_panStartPoint` の取得と `CaptureMouse()` が行われ、**毎フレーム「タッチパン」と「マウスパン」が同時に実行されてスクロール位置の奪い合い（競合）** が発生する。
2. **マウス移動処理（`OnPreviewMouseMove`）の計算不備**:
   - 座標系がスクロールによって移動する `EditorInkCanvas`（自身）基準となっており、スクロールによって座標値がシフトする。
   - `_panStartPoint` が更新されず、タッチ開始点からの変位を現在のオフセットに毎フレーム累積加算している。
   - このため、スクロールに伴って座標が急変し、正のフィードバックによる振動（ガタつき）が発生している。

## 3. 変更内容
### (1) `EditorInkCanvas.cs` の入力制御の改修
- **`OnPreviewMouseDown`**:
  - `ToolMode == EditorToolMode.Hand` 時、入力がタッチ／スタイラス由来（`e.StylusDevice != null` または `_activeTouchPoints.Count > 0`）である場合はマウスパン処理を行わずスルーする。
  - 物理マウス時は、固定ビューポートである `_parentScrollViewer` 基準の座標で `_panStartPoint = e.GetPosition(_parentScrollViewer)` を取得する。
  - `IsStraightLineActive` の直線開始処理でも同様にタッチ入力を除外し、指操作が直線誤描画にならずパン・ピンチズームに回るようにする。
- **`OnPreviewMouseMove`**:
  - タッチ／スタイラス由来のイベント、または `_activeTouchPoints.Count > 0` の場合はマウスパン処理をスキップする。
  - 物理マウス時は `_parentScrollViewer` 基準の座標で変位を計算し、`_parentScrollViewer.ScrollTo*Offset(...)` 後に `_panStartPoint = current` で現在位置を更新してフレームごとの変位を正確に追従する。
- **`OnPreviewMouseUp` / `OnLostMouseCapture`**:
  - キャプチャ解放および `_panStartPoint = null` を安全に実施する。

### (2) 単体テストの追加・拡充
- `tests/PDFBinder.Tests/EditorInkCanvasTouchTests.cs` に以下のテストケースを追加：
  - タッチ操作中（`ActiveTouchPointCount > 0` またはタッチ昇格イベント）におけるマウスパン処理の抑止検証
  - 物理マウスパン時の座標更新およびスクロール変位計算の整合性検証

## 4. 検証計画
1. `dotnet test`: すべての既存テストおよび新規追加テストが PASS することを確認
2. `dotnet build`: 警告やエラーなくビルドが成功することを確認
