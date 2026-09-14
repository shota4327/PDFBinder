# 拡大縮小後・移動後の手書きズレおよび自動スクロール不具合の修正計画 (Issue #41)

詳細エディタにおいて、拡大縮小後やスクロール移動後に画面に手を置いて手書きを開始した際、または手書き開始の瞬間にページが自動スクロールして描画位置がズレてしまう問題を解決します。

## 課題と根本原因の分析

1. **手書き開始瞬間の自動スクロール（位置ズレ）**:
   - WPFの `ScrollViewer` は子要素（`Border` や `EditorInkCanvas`）がフォーカス・キャプチャを取得した際、デフォルトで `RequestBringIntoView` イベントを捕捉して要素全体を表示領域内に引き込もうと自動スクロールを実行します。
   - 拡大後やユーザーが意図したスクロール位置にある状態でペンや指が接地すると、この自動引き込みが発火して画面が不意にジャンプしていました。
2. **手を置いた状態での手書き時の位置ズレ**:
   - `EditorInkCanvas` の `OnPreviewTouchMove` において `IsStylusSuppressed()` のチェックが抜けており、スタイラスでの筆記中であっても手のひら（パーム）の微細な動きで `HandleOneFingerPan` や `HandleTwoFingerPinchZoom` が呼び出され、筆記中にキャンバスがスクロール・ズームされていました。
   - スタイラスが画面に近づく前（ホバー範囲外）に手のひらが先に接地した場合、そのタッチが `_activeTouchPoints` に登録され、スタイラス接地後もタッチがパージされず `EditingMode` が `None` のままになったり、タッチ移動イベントが継続して発生していました。
   - 指パンの開始判定に遊び（タッチスロップ）がなく、手のひら接地瞬間のわずかな接触面積変動で即座にスクロールが開始されていました。

---

## ユーザー確認・合意事項（`/grill-me` にて決定済み）

1. **`DetailScrollViewer` の自動 `BringIntoView` の抑止**:
   - `DetailScrollViewer` にて `FrameworkElement.RequestBringIntoViewEvent` を捕捉し、`e.Handled = true` とすることで意図しないフォーカス時自動スクロールを完全防止。
   - サムネイルクリック時等のページジャンプ（`ScrollToPageRequested`）は、コンテナの位置を計算して `ScrollToVerticalOffset` を直接呼び出す明示的スクロール制御に変更。
2. **スタイラス検知時のタッチ完全パージ & `TouchMove` 抑止**:
   - `OnPreviewStylusDown`、`OnStylusInRange`、`OnStylusInAirMove` 発生時、既存の `_activeTouchPoints` を即時クリアし、タッチキャプチャを解放して `UpdateEditingMode()` により描画モードへ確実に復元。
   - `OnPreviewTouchMove` で `IsStylusSuppressed()` をチェックし、スタイラス活動中およびクールダウン中はタッチ移動を完全抑止。
3. **パラメータ設定**:
   - スタイラス離脱後のパーム抑制クールダウン時間: 500ms（既存の350msから延長して安全マージンを確保）。
   - タッチスクロール開始の遊び（タッチスロップ）: 8.0px（手のひら接地瞬間の微小ブレによる誤スクロールを防止）。

---

## 提案される変更

### コントロール層 (`PDFBinder.App.Controls`)

#### [MODIFY] [`EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- スタイラスクールダウン定数を 500ms に設定。
- タッチスロップ（8.0px）の判定ロジックを導入:
  - 1本指タッチ開始時点（`OnPreviewTouchDown`）の初期座標 `_touchStartPosition` を記録。
  - 移動距離が閾値を超えるまで `_parentScrollViewer` のスクロールを行わず待機。
- スタイラス検知時（`OnPreviewStylusDown`, `OnStylusInRange`, `OnStylusInAirMove`）のタッチパージ処理:
  - `PurgeActiveTouches()` ヘルパーメソッドを追加し、`_activeTouchPoints.Clear()`、`ReleaseTouchCapture()`、`UpdateEditingMode()` を実行。
- `OnPreviewTouchMove` の先頭に `if (IsStylusSuppressed()) { e.Handled = true; return; }` を追加。

### ビュー層 (`PDFBinder.App.Views`)

#### [MODIFY] [`DetailEditorView.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml.cs)
- コンストラクタにて `DetailScrollViewer.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, e) => e.Handled = true), true);` を登録し、子要素フォーカス時の不要な自動スクロールを遮断。
- `OnScrollToPageRequested`:
  - `container?.BringIntoView()` を廃止。
  - `container.TransformToVisual(DetailScrollViewer)` を用いて対象ページのビューポート内相対座標を算出し、`DetailScrollViewer.ScrollToVerticalOffset` で滑らかかつ正確に直接スクロール。

### テスト層 (`tests/PDFBinder.Tests`)

#### [MODIFY] [`EditorInkCanvasTouchTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/EditorInkCanvasTouchTests.cs)
- スタイラス検知時のタッチパージおよび `EditingMode` 復帰の検証テストを追加。
- スタイラス活動中における `OnPreviewTouchMove` の抑止動作の検証テストを追加。
- タッチスロップ（閾値未満の微小移動でのスクロール不発火）の検証テストを追加。

---

## 検証計画

### 自動テスト
- `dotnet test`: 既存テストおよび新規追加テストがすべて 100% PASS することを確認。
- `dotnet build`: ビルドが警告・エラー 0 件で成功することを確認。

### 手動検証確認項目
- 詳細エディタでページを拡大（例: 150%、200%）し、上端や中央など任意の位置にスクロール。
- ペンを置く前に手のひらを画面上にしっかりと置いた状態でペンで筆記を開始し、画面が勝手にスクロールしたり線がズレたりしないことを確認。
- 画面を指でスクロール・パンした直後にペンで素早く書き込みを行っても、一定距離の自動スクロール（ジャンプ）が発生しないことを確認。
- 手指による1本指スクロール（パン）および2本指ピンチズームが軽快・正確に動作することを確認。
