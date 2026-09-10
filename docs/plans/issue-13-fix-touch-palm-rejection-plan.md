# 手書きエディタのタッチ操作時のゴースト描画・入力フリーズ不具合の修正計画 (Issue #13)

ペン選択中に手指で画面をタッチ・スライドした際に、仮のペン軌跡（ゴースト線）が表示され、指を離すと軌跡が消えた後にカーソルが消失して操作不能（UIフリーズ・終了不能）になる不具合を解消し、手指によるパン・ピンチズーム操作およびパームリジェクションを正常に機能させます。

## 決定事項（`/grill-me` にて合意済み）
1. **`PenOnlyDynamicRenderer` の導入**:
   - `DynamicRenderer` を継承した `PenOnlyDynamicRenderer` を実装。
   - ペンスレッド（`OnStylusDown`, `OnStylusMove`, `OnStylusUp`）において、`TabletDeviceType.Touch` による入力を検知した場合は `base` 呼び出しを行わず破棄する。
   - これにより、手指タッチ時にペンスレッドでリアルタイム描画されるゴーストストロークを完全に排除する。
2. **タッチ中の安全な `EditingMode` 切り替え**:
   - 手指タッチ開始時（`PreviewTouchDown`）に、パームリジェクション対象外であれば一時的に `EditingMode = InkCanvasEditingMode.None` に退避・切り替えを行う。
   - これにより、`InkCanvas` のインク収集エンジン（`InkCollectionBehavior`）がマウスやスタイラスを誤捕捉（Capture）するのを防止する。
   - すべての指が離れた際（`_activeTouchPoints.Count == 0`）に `UpdateEditingMode()` を呼び出して本来の描画モード（`Ink`, `EraseByStroke` 等）へ復元する。
3. **`PreviewStylus` イベントの正常化**:
   - `OnPreviewStylusDown` / `OnPreviewStylusMove` / `OnPreviewStylusUp` 内の `e.Handled = true`（入力昇格の中断）を撤廃。
   - WPF の Stylus/Mouse イベント伝播パイプラインを健全に保ち、カーソル消失や入力デッドロックを防止する。
4. **パームリジェクションとマルチタッチジェスチャーの協調**:
   - スタイラスの接地中および近接（ホバー）中は引き続き手指タッチを抑止（`e.Handled = true`）。
   - スタイラス非使用時は、1本指でのスクロール（パン）および2本指での中心点追従ピンチズームが確実に動作するようにする。

---

## 提案される変更

### コントロール層 (`PDFBinder.App.Controls`)

#### [NEW] [`PenOnlyDynamicRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/PenOnlyDynamicRenderer.cs)
- `DynamicRenderer` の派生クラス。
- `rawStylusInput.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Touch` の場合に処理をスキップするペンスレッドフィルタを実装。

#### [MODIFY] [`EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- コンストラクタで `DynamicRenderer = new PenOnlyDynamicRenderer();` を設定。
- `OnPreviewStylusDown` / `Move` / `Up` から `IsTouchDevice(e)` に対する `e.Handled = true` を削除し、純粋なスタイラス状態のトラッキングのみを行うように整理。
- `OnPreviewTouchDown`:
  - `IsStylusSuppressed()` が `true` の場合は従来通りイベントを処理済みにする（パームリジェクション）。
  - `false` の場合は `EditingMode = InkCanvasEditingMode.None;` に切り替えてからタッチトラッキングを開始。
- `OnPreviewTouchUp` / `OnTouchLeave`:
  - 指が離れて `_activeTouchPoints.Count == 0` になった際に、`UpdateEditingMode()` を呼び出してツールモードに応じた `EditingMode` を復元。
  - 残存キャプチャの安全な解放。

---

### テスト層 (`tests/PDFBinder.Tests`)

#### [NEW] [`EditorInkCanvasTouchTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/EditorInkCanvasTouchTests.cs)
- `EditorInkCanvas` のタッチおよびパームリジェクション制御の単体テスト:
  - `DynamicRenderer` が `PenOnlyDynamicRenderer` に置き換わっていることの検証。
  - タッチ開始時に `EditingMode` が `None` に切り替わり、タッチ終了時に元のツールモードの `EditingMode` に復帰することの検証。
  - スタイラス近接時（パームリジェクション）のタッチ抑止検証。

---

## 検証計画

### 自動テスト
- `dotnet test`: 既存の全43件および新規テストが 100% PASS することを確認。
- `dotnet build`: 警告・エラー 0 件でビルドが成功することを確認。

### 手動検証確認項目
- アプリを起動し、詳細エディタを表示。
- ペン選択時に画面を指でタッチ・スライドし、ペンの軌跡が描画されないこと（ゴースト描画の防止）。
- 指を離した後、カーソルが正常に表示され、ボタンのクリックやウィンドウの操作・終了が問題なく行えること（入力ロックの解消）。
- 1本指でのスクロール、2本指でのピンチズームが軽快に動作すること。
- スタイラスペンでの筆記時、手首が画面に触れても描画や拡大縮小が乱れないこと（パームリジェクションの維持）。
