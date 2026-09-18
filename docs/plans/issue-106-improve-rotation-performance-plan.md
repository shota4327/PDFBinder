# 実装計画書 - Issue #106: 回転時のパフォーマンス向上および拡大率追従

## 1. 概要・背景
PDFのページ回転操作において、以下の2つの問題が発生しています：
1. **手書きインクが多い場合の回転パフォーマンス低下**:
   - `InkTransformHelper.RotateStrokes` において、全ストロークおよび全ストローク内の全スタイラスポイントを個別に `new StylusPoint` / `new Stroke` で再生成し、`StrokeCollection.Clear()` および `Add()` を1本ずつ繰り返しているため、大量のアロケーションとGCプレッシャーが発生している。
   - さらに、詳細エディタ表示中はストローク追加ごとに `StrokesChanged` イベントが発火し、`RequestCacheUpdate()` 経由で `RenderTargetBitmap` の再描画（全ストロークの再ラスタライズ）がストローク数分（数百〜数千回）多重実行されて画面がフリーズする。
2. **回転時の拡大率（FitMode）のはみ出し問題**:
   - 縦長のウィンドウで縦長の用紙を「ウィンドウに合わせる」または「幅に合わせる」で表示している状態でページを90度回転すると、用紙の縦横比が逆転して横長になるにもかかわらず、拡大率（ズーム倍率）が更新されないため、画面横幅を大きくはみ出してしまう。

本実装では、WPFのネイティブ一括アフィン変換 `StrokeCollection.Transform(Matrix, false)` を活用して手書きインクの回転を劇的に高速化するとともに、回転操作およびUndo/Redo時に現在のFitModeに応じた拡大率の自動再計算を行い、快適な操作性を実現します。

---

## 2. ユーザー合意事項（/grill-me による決定事項）
1. **手動ズーム時（FitMode=None）の挙動**:
   - 手動で拡大縮小している場合（FitMode == None）は、現在の拡大率を維持し、勝手にズーム倍率を変更しない。
   - FitMode（「ウィンドウに合わせる」「幅に合わせる」）が有効な場合のみ、新しい用紙寸法に合わせて自動再計算する。
2. **連続表示モード（Vertical Continuous）での回転時挙動**:
   - 連続表示モードであっても、回転されたページが現在アクティブなページ（または可視ページ）であれば、FitModeを自動再計算してウィンドウからのはみ出しを防止する。
3. **Undo / Redo 時の挙動**:
   - Undo/Redo で回転が取り消し・やり直しされた場合も、回転操作時と同様に FitMode を自動再計算して画面内に収める。

---

## 3. 変更計画と設計詳細

### 3.1 `PDFBinder.Core` (コアライブラリ)

#### [MODIFY] [InkTransformHelper.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Helpers/InkTransformHelper.cs)
- **ネイティブ一括アフィン変換への刷新**:
  - 各ストローク・点を `new` して `Clear()` / `Add()` を繰り返す既存ロジックを廃止。
  - WPF の `Matrix` を用いて、回転差分角度（90度、180度、270度）と回転前寸法（`currentDisplayWidth`, `currentDisplayHeight`）に基づく変換行列を構築。
    - 90度（時計回り）: `(x, y) -> (currentDisplayHeight - y, x)`
      - 行列: `M11=0, M12=1, M21=-1, M22=0, OffsetX=currentDisplayHeight, OffsetY=0`
    - 180度: `(x, y) -> (currentDisplayWidth - x, currentDisplayHeight - y)`
      - 行列: `M11=-1, M12=0, M21=0, M22=-1, OffsetX=currentDisplayWidth, OffsetY=currentDisplayHeight`
    - 270度（反時計回り90度）: `(x, y) -> (y, currentDisplayWidth - x)`
      - 行列: `M11=0, M12=-1, M21=1, M22=0, OffsetX=0, OffsetY=currentDisplayWidth`
  - `strokes.Transform(matrix, false)` を1回呼び出し、全ストロークを一括インプレース変換。
  - 90度および270度回転時は、各ストロークのペン先寸法（`DrawingAttributes.Width` と `DrawingAttributes.Height`）を反転（縦横入れ替え）。
  - これにより、大量オブジェクト生成・GC負荷およびコレクション変更通知の乱発を完全に解消。

---

### 3.2 `PDFBinder.App` (WPF UI層)

#### [MODIFY] [EditorInkCanvas.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- **ストローク変更時のキャッシュ更新多重呼び出しの抑制**:
  - `_isInternalStrokeSync` または外部からの一括更新時（回転時など）に、中間状態での `RequestCacheUpdate()` が多重に走らないよう安全ガードを強化。
  - 消しゴム・選択モード（`IsEraserOrSelectMode`）中にページマスターが回転された場合も、InkCanvas内部ストロークが整合性を保ってスムーズに追従するように同期処理を調整。

#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- **回転発生時の FitMode 自動再計算**:
  - ページの回転完了時、現在の `FitMode != DetailViewFitMode.None` であれば、`ApplyFitMode()` を呼び出して新しい寸法に最適な `Zoom` を即座に再計算・適用。
  - 単一ページ表示時だけでなく、連続表示モード時にもアクティブページの回転であれば `ApplyFitMode()` を適用。

#### [MODIFY] [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- **回転コマンドおよび Undo / Redo 時の連携**:
  - `RotateClockwise()`, `RotateCounterClockwise()`, `Undo()`, `Redo()` 実行時、詳細エディタが表示中（`IsDetailViewActive`）であれば、動的レンダリング要求前に `DetailEditor.ApplyFitMode()` を確実に呼び出す。
  - これにより、回転実行時および Undo/Redo のどちらでも、用紙の向きに合わせてウィンドウ内に綺麗に収まり、はみ出しを防止。

---

### 3.3 `PDFBinder.Tests` (単体テスト)

#### [MODIFY/NEW] [InkTransformHelperTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/Helpers/InkTransformHelperTests.cs)
- `StrokeCollection.Transform` による回転結果が、従来の座標変換結果と完全に一致することを確認するテスト。
- 90度、180度、270度、360度（4回連続）回転させたときに元の座標・ペン先属性に戻ることの検証。
- 大量ストローク（数千本）の回転がミリ秒単位で高速実行されることのパフォーマンステスト。

#### [MODIFY/NEW] [DetailEditorViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ViewModels/DetailEditorViewModelTests.cs)
- ページ回転時に FitMode が `FitToWindow` / `FitToWidth` であればズーム倍率が自動更新されることの検証。
- 手動ズーム（`FitMode.None`）時にはズーム倍率が変更されずに保持されることの検証。

---

## 4. 検証計画

### 4.1 自動テスト
- `dotnet test`: 全単体テストが 100% PASS することを確認。

### 4.2 ビルド確認
- `dotnet build`: エラーおよび警告がないことを確認。
