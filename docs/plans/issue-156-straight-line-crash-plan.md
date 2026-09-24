# 直線描画時のクラッシュ修正 実装計画 (Issue #156)

詳細エディタで「直線」ツールを有効にした状態でキャンバス上に直線を引いた際にアプリが異常終了（クラッシュ）する問題を解決するための実装計画です。

---

## 1. 課題と原因の整理

### 1.1 現象
- 詳細エディタのツールバーで「直線」トグルボタンをオンにする。
- ペンまたは蛍光ペンでキャンバス上をドラッグまたはクリックして直線を引く。
- マウスボタンを離した（`PreviewMouseUp`）瞬間にアプリが強制終了する。

### 1.2 根本原因
[`src/PDFBinder.App/Controls/EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs) の `OnPreviewMouseUp` 実装において、以下のように処理が行われていました。

```csharp
ReleaseMouseCapture();
_isDrawingLine = false;

CommitStraightLine(_lineStartPoint.Value, _currentLinePoint.Value);
```

1. `ReleaseMouseCapture()` の呼び出しにより、WPF 内部で `LostMouseCaptureEvent` が同期的に発行されます。
2. 同一スレッド内で即座に [`OnLostMouseCapture`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs#L478-L494) が実行されます。
3. `OnLostMouseCapture` では、`if (_isDrawingLine)` を判定して `_isDrawingLine = false; _lineStartPoint = null; _currentLinePoint = null;` を実行します。
4. `ReleaseMouseCapture()` の処理から復帰した直後、`_lineStartPoint` および `_currentLinePoint` は既に `null` になっています。
5. `_lineStartPoint.Value` および `_currentLinePoint.Value` を呼び出した瞬間に `System.InvalidOperationException: Nullable object must have a value.` がスローされ、未捕捉の例外としてアプリ全体がクラッシュします。

---

## 2. 修正方針

### 2.1 `EditorInkCanvas.cs` の安全なマウスキャプチャ解放と座標コミット
1. `_lineStartPoint` および `_currentLinePoint` に値が存在することを確認後、直ちにローカル変数 `start` と `end` へ座標値を退避します。
2. `_isDrawingLine` を `false` に設定し、`_lineStartPoint` / `_currentLinePoint` を安全に `null` クリアします。
3. `IsMouseCaptured` が `true` の場合のみ `ReleaseMouseCapture()` を呼び出します（この時 `_isDrawingLine` は既に `false` のため、`OnLostMouseCapture` による二重処理や競合は発生しません）。
4. 退避した `start` と `end` を用いて `CommitStraightLine(start, end)` を実行します。
5. ヒアリング結果に基づき、クリックのみ（`start == end` または微小移動）であってもドット（点）としてコミットします。

### 2.2 単体テストの追加・強化
[`tests/PDFBinder.Tests/DetailEditorStraightLineTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorStraightLineTests.cs) に以下のテストケースを追加します：
1. **マウスイベントシミュレーションによる直線描画の正常完了テスト**:
   - `IsStraightLine = true` の状態で `PreviewMouseDown` → `PreviewMouseMove` → `PreviewMouseUp` を発生させた際に、クラッシュせず `Page.InkStrokes` に正しく直線ストロークが追加されることを検証。
2. **クリックのみ（同一座標）の場合のドット描画テスト**:
   - 移動なし（`start == end`）のクリックでストロークが正常にコミットされることを検証。
3. **キャプチャ喪失（Alt+Tab 等）時の安全なキャンセルテスト**:
   - `_isDrawingLine = true` の描画途中で `OnLostMouseCapture` が発生した場合、クラッシュせずストロークがコミットされずに描画状態が初期化されることを検証。

---

## 3. 影響範囲
- [`src/PDFBinder.App/Controls/EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs): `OnPreviewMouseUp` メソッド内の局所的な修正（最小差分原則に準拠）。
- [`tests/PDFBinder.Tests/DetailEditorStraightLineTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorStraightLineTests.cs): 直線描画ライフサイクルのテスト拡充。

---

## 4. 検証手順
1. `dotnet test` により、追加したテストを含む全単体テストが 100% PASS することを確認。
2. `dotnet build` による警告・エラーのないビルド成功を確認。
