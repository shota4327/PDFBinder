# ペン筆記直後の手指操作による誤描画防止およびスクロール応答性改善計画 (Issue #46)

詳細エディタにおいて、ペンで手書きした直後に指で画面を操作しようとするとペンの描画になってしまい、一定時間待たないとスクロールできない不具合を修正します。

## 課題と根本原因の分析

1. **指での誤描画（ペンストローク化）の原因**:
   - WPFの `InkCanvas` は、タッチ入力を内部で `StylusDevice`（デバイス種別 `TabletDeviceType.Touch`）として受け取り、`EditingMode == Ink` の状態であれば自動的にインク収集（ストローク描画）を開始します。
   - `EditorInkCanvas` の `OnPreviewStylusDown` において、`TabletDeviceType.Touch` に対しても `base.OnPreviewStylusDown(e)` を呼び出していたため、`InkCanvas` 内部のインク収集エンジン（`InkCollectionBehavior`）がタッチに対しても描画を開始していました。
   - ペン離脱直後（クールダウン中）に指で触れると、`OnPreviewTouchDown` はスクロールを抑止（`e.Handled = true`）するものの、インク収集はキャンセルされずにそのまま継続していたため、指で線が引かれてしまっていました。
2. **スクロールまでの待機時間（遅延）の原因**:
   - Issue #41 で設定したクールダウン時間（500ms）が過大であったため、ペンを完全に画面から遠ざけた（ホバー範囲外に出した）後であっても、0.5秒間はすべてのタッチ操作がパームとして抑止され、指スクロールが受け付けられませんでした。

---

## 決定事項（`/grill-me` にて合意済み）

1. **タッチデバイスからのインク収集を根本遮断**:
   - `OnPreviewStylusDown`、`OnPreviewStylusMove`、`OnPreviewStylusUp` において、入力デバイスが真のスタイラスペン（`TabletDeviceType.Stylus`）である場合のみ `base` のインク収集処理を呼び出す。
   - タッチデバイス（`TabletDeviceType.Touch`）の場合は `base` のインク収集を呼び出さずスキップすることで、手指や手のひらが `InkCanvas` 上でインクを描くことを100%恒久的に防止する。
   - これにより、万が一クールダウン中やホバー中にタッチが接触しても、誤ってペンで線が引かれる事故が完全にゼロになります。
2. **パームリジェクションとの整合性と保護の維持**:
   - スタイラス接地中（`_isStylusTouching`）およびホバー中（`_isStylusInRange`）は、従来通りすべてのタッチによるスクロールを完全抑止（筆記中の画面揺れを防止）。
   - タッチスロップ（8.0pxの遊び）を維持し、ペン離脱瞬間に手のひらが画面に残っていても、微小な接触ブレではスクロールが発火しないように二重保護。
3. **クールダウン時間の適正化（150ms）**:
   - ペン先が画面およびホバー範囲（約 1.5〜2cm）から完全に離脱した後のクールダウン時間を、500ms から **150ms** に短縮。
   - 筆記終了後の自然な手のひら離脱猶予（約50〜100ms）を安全に吸収しつつ、人間が意図して指でスクロールを行う動作（約150〜200ms）には干渉せず、即座に快適なスクロールを開始できるようにする。

---

## 提案される変更

### コントロール層 (`PDFBinder.App.Controls`)

#### [MODIFY] [`EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- `StylusSuppressionCooldownMs` を 500 から **150** に変更。
- `OnPreviewStylusDown`:
  - `if (IsStylusDevice(e))` のブロック内でのみ `base.OnPreviewStylusDown(e)` を呼び出す（タッチデバイス時は呼び出さない）。
- `OnPreviewStylusMove`:
  - `if (IsStylusDevice(e))` のブロック内でのみ `base.OnPreviewStylusMove(e)` を呼び出す。
- `OnPreviewStylusUp`:
  - `if (IsStylusDevice(e))` のブロック内でのみ `base.OnPreviewStylusUp(e)` を呼び出す。

### テスト層 (`tests/PDFBinder.Tests`)

#### [MODIFY] [`EditorInkCanvasTouchTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/EditorInkCanvasTouchTests.cs)
- クールダウン時間（150ms）に応じた単体テストの閾値調整。
- タッチ入力時に `base.OnPreviewStylusDown` がバイパスされ、タッチによる不要なインク収集が開始されないことの検証テストを追加。

---

## 検証計画

### 自動テスト
- `dotnet test`: 既存全103件および更新・新規テストがすべて 100% PASS することを確認。
- `dotnet build`: ビルドが警告・エラー 0 件で成功することを確認。

### 手動検証確認項目
- アプリを起動し、詳細エディタを表示。
- ペンで文字や線を手書きした後、直ちに（待ち時間なく）指でスワイプして滑らかにスクロールできることを確認。
- ペンでの筆記直後に指で画面に触れても、線が勝手に描画されないことを確認。
- 手を画面に置いた状態でペンで書いている最中に、画面が勝手にスクロールしないこと（パームリジェクションが正常に機能していること）を確認。
