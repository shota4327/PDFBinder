# Issue #70: ページ回転時の手書きインク追従回転 実装計画

## 概要
現在、PDFページを回転（右90度回転、左90度回転など）した際、PDFの背景画像やサムネイルは回転するものの、ページ上に描画された手書きインク（`InkStrokes`）が回転せず、用紙上の位置・向きと不整合が発生する問題を解決します。
本改修により、ページ回転時に手書きインクストロークを用紙と同一方向に幾何学的に追従回転させ、サムネイル表示、詳細エディタ表示、Undo/Redo（元に戻す/やり直し）、およびPDF保存（他社ビューア用アピアランスストリーム含む）のすべてにおいて整合性を維持します。

---

## ユーザー合意事項（/grill-me インタビュー結果）
1. **ストロークの回転挙動**:
   - ページ回転時、用紙と一緒に手書きインクも同方向に回転し、用紙上の相対位置・向きを維持する。
2. **Undo/Redo の連携**:
   - `RotatePageCommand` にインク回転と復元を包含し、Undo/Redo（Ctrl+Z / Ctrl+Y）時に用紙とインクが完全に同期して不可分に戻る/やり直せるようにする。
3. **Undo 時の復元方式**:
   - 幾何学的逆変換（+90°回転の取り消し時は -90°回転）を適用し、メモリ効率を高めつつ数式の可逆性を活用する。
4. **ロジック配置**:
   - `PDFBinder.Core.Helpers`（または類似名前空間）に `InkTransformHelper` を新設し、`PdfPageModel`、`RotatePageCommand`、単体テストから統一的に呼び出す。

---

## 幾何学的座標変換の数理仕様
表示座標系において、回転前のページ表示サイズを `(currentWidth, currentHeight)`、変換対象ストロークの任意の点を `(x, y)` とする。

1. **時計回り90度回転（Rotate90 / +90°）**:
   - 新しいページ表示サイズ: `(currentHeight, currentWidth)`
   - 新しい座標:
     $$x' = \text{currentHeight} - y$$
     $$y' = x$$
2. **反時計回り90度回転（Rotate270 / -90°）**:
   - 新しいページ表示サイズ: `(currentHeight, currentWidth)`
   - 新しい座標:
     $$x' = y$$
     $$y' = \text{currentWidth} - x$$
3. **180度回転（Rotate180 / +180°）**:
   - 新しいページ表示サイズ: `(currentWidth, currentHeight)`
   - 新しい座標:
     $$x' = \text{currentWidth} - x$$
     $$y' = \text{currentHeight} - y$$

※ペンの筆圧情報（`pt.PressureFactor`）はそのまま保持します。
※`DrawingAttributes` のペン先サイズ（`Width`, `Height`）が異なる場合は、90°/270°回転時に幅と高さを入れ替えます。

---

## 変更対象コンポーネントとファイル

### 1. `PDFBinder.Core`
- **[NEW] `Helpers/InkTransformHelper.cs`**:
  - 指定された差分回転角度（`PageRotation deltaRotation`）と現在の表示サイズ（`displayWidth, displayHeight`）に基づいて、`StrokeCollection` の各ストロークの座標を変換・更新するユーティリティメソッド群を提供。
  - ストロークコレクションの内容を更新する際、WPF イベントハンドラー（`StrokesChanged`）が発火するようにしてモデルやビューへの通知を保証。
- **[MODIFY] `Models/PdfPageModel.cs`**:
  - `RotateClockwise()` および `RotateCounterClockwise()` メソッドにおいて、`InkTransformHelper` を呼び出して `InkStrokes` を追従回転するよう更新。
- **[MODIFY] `Services/UndoRedoService.cs`（`RotatePageCommand`）**:
  - `RotatePageCommand` の `Execute()` で目的の回転への差分を計算してインクを回転。
  - `Undo()` で逆方向の差分回転を計算してインクを幾何学的に逆回転。

### 2. `PDFBinder.App`
- **[MODIFY] `ViewModels/MainViewModel.cs`**:
  - 回転コマンド実行後、詳細ビュー表示中であれば `DetailEditor.ScheduleDynamicRender(immediate: true)` とともに、アクティブページのストロークキャッシュ更新およびエディタキャンバスの同期を確実に行う。
- **[MODIFY] `Controls/EditorInkCanvas.cs`**:
  - 消しゴムや選択ツールモードで直接 `EditorInkCanvas.Strokes` を保持している場合でも、マスター側のストローク回転変更が正しく画面上のストロークに反映されることを確認・調整。

### 3. `PDFBinder.Tests`
- **[NEW] `InkRotationTests.cs`**:
  - 時計回り90度、反時計回り90度、180度、360度（元に戻る）の幾何変換テスト。
  - 筆圧情報、ペン先サイズ変更、Undo/Redo による完全復元のテスト。
  - 回転後のPDF保存および再読み込み時の位置整合性テスト。
  - 詳細エディタでのキャッシュ・表示連動テスト。

---

## 検証手順

### 1. 自動テスト
- `dotnet test`: 全テストが成功することを確認（既存テスト240件 + 新規テスト）。

### 2. 手動・結合動作検証
1. PDFを開き、詳細エディタで特定の位置（例: 右上隅）に目印となるペンストロークを描画。
2. ツールバーまたはショートカット（Ctrl+R）で時計回りに90度回転。
   - 背景画像とともにストロークが時計回りに90度回転し、用紙上の同一位置・向きにあることを確認。
3. サムネイル一覧（グリッド表示）に切り替え、サムネイルでもストロークが回転していることを確認。
4. Ctrl+Z（Undo）を実行し、用紙とストロークが元の向きに完全に復帰することを確認。
5. ファイルを保存（上書きまたは別名保存）し、再度読み込んでストロークの位置・向きが維持されていることを確認。
