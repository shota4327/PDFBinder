# 直線描画時のクラッシュ修正 検証報告 (Issue #156)

詳細エディタで「直線」ツールを有効にした状態でキャンバス上に直線を引いた際にアプリが異常終了（クラッシュ）する不具合の修正と検証結果の報告です。

---

## 1. 修正内容の概要

### 1.1 根本原因の特定
[`src/PDFBinder.App/Controls/EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs) の `OnPreviewMouseUp` において、WPF の `ReleaseMouseCapture()` を呼び出した直後に `_lineStartPoint.Value` / `_currentLinePoint.Value` を参照していました。
WPF では `ReleaseMouseCapture()` の呼び出しにより同期的イベント `LostMouseCapture` が発生し、`OnLostMouseCapture` が即座に実行されて `_lineStartPoint` および `_currentLinePoint` が `null` に初期化されます。
その結果、`ReleaseMouseCapture()` から復帰した直後の `.Value` 呼び出しで `System.InvalidOperationException: Nullable object must have a value.` がスローされ、未捕捉の例外としてアプリが強制終了していました。

### 1.2 実装の修正
- `OnPreviewMouseUp` において、`ReleaseMouseCapture()` を呼ぶ前に始点（`start`）と終点（`end`）の座標をローカル変数に退避。
- 直線描画フラグ `_isDrawingLine` および座標フィールドを安全に先に初期化。
- `IsMouseCaptured` が true の場合のみ `ReleaseMouseCapture()` を呼び出し（すでに `_isDrawingLine` が false のため `OnLostMouseCapture` での二重処理はスキップ）。
- 退避した座標を用いて `CommitStraightLine(start, end)` を実行し、ストロークを確定。
- クリックのみ（始点と終点が同一座標）の場合も、ドット（点）として正常にコミットされるように対応。

---

## 2. 単体テストの追加と結果

[`tests/PDFBinder.Tests/DetailEditorStraightLineTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorStraightLineTests.cs) に以下の 3 つのテストケースを追加しました：

1. **`EditorInkCanvas_OnPreviewMouseUp_DoesNotThrow_AndCommitsStraightLine`**:
   - 直線描画完了（マウスアップ）時に例外が発生せず、指定座標（(10, 20) → (100, 200)）の直線ストロークがマスターコレクション `Page.InkStrokes` に正常にコミットされることを確認。
2. **`EditorInkCanvas_OnPreviewMouseUp_SingleClick_CommitsDotStroke`**:
   - マウスドラッグを伴わない単一クリック（始点＝終点）の場合に、ドットストロークとして正常にコミットされることを確認。
3. **`EditorInkCanvas_OnLostMouseCapture_CancelsDrawingWithoutCommitting`**:
   - 描画途中でフォーカス喪失やウィンドウ切り替え等のマウスキャプチャ喪失が発生した場合、ストロークをコミットせず安全に描画中状態がクリアされることを確認。

### テスト実行結果
```
成功! - 失敗: 0、合格: 429、スキップ: 0、合計: 429、期間: 5 s - PDFBinder.Tests.dll (net10.0)
```
全 429 件のテストが 100% 合格しました。

---

## 3. ビルド検証
```
dotnet build
ビルドに成功しました。
    0 個の警告
    0 エラー
```
警告およびエラーなく正常にビルドが成功することを確認しました。

---

## 4. バージョンおよびドキュメント更新
- `Directory.Build.props`: `0.4.1` → `0.4.2` へインクリメント
- `CHANGELOG.md`: `[0.4.2] - 2026-09-24` セクションの追加およびエンドユーザー向け記述の追記
