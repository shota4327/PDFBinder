# Issue #46 修正検証レポート（Walkthrough）

詳細エディタにおいて、ペンで手書きした直後に指で画面を操作しようとするとペンの描画になってしまい、一定時間待たないとスクロールできない不具合を修正・検証しました。

---

## 修正内容の概要

### 1. タッチデバイスからのインク収集を根本遮断
- **原因**: 
  - WPFの `InkCanvas` は、タッチ入力を `StylusDevice`（`TabletDeviceType.Touch`）として受け取ってインク収集を行っていました。
  - スタイラス離脱後のクールダウン中（旧設定: 500ms）に指で触れると、`OnPreviewTouchDown` はスクロールを抑止するもののインク収集はキャンセルされずに継続していたため、指で線が引かれてしまっていました。
- **対応**: 
  - `EditorInkCanvas` の `OnPreviewStylusDown`、`OnPreviewStylusMove`、`OnPreviewStylusUp` において、`TabletDeviceType.Stylus` の真のスタイラスペンである場合のみ `base` のインク収集処理を呼び出すように変更しました。
  - タッチデバイス（`TabletDeviceType.Touch`）時は `base` のインク収集を一切呼び出さないことで、指や手のひらが `InkCanvas` 上でインクを描いてしまう現象を100%恒久的に防止しました。

### 2. パームリジェクションの維持とクールダウンの適正化（150ms）
- **パームリジェクションとの整合性**:
  - スタイラス接地中（`_isStylusTouching`）およびホバー中（`_isStylusInRange`）は、従来通りすべてのタッチによるスクロールを完全抑止（筆記中の画面揺れを防止）。
  - タッチスロップ（8.0pxの遊び）を維持し、ペン離脱瞬間に手のひらが画面に残っていても、微小な接触ブレではスクロールが発火しないよう二重保護。
- **クールダウンの短縮**:
  - ペン先が画面およびホバー範囲（約 1.5〜2cm）から完全に離脱した後のクールダウン時間を、過大な 500ms から **150ms** に短縮。
  - ペンを離した直後の手のひらの自然な浮き上がり猶予を安全に吸収しつつ、人間が意図して指でスクロールを行う動作には干渉せず、即座に快適なスクロールを開始できるよう改善しました。

---

## 変更ファイル一覧

1. [MODIFY] [`src/PDFBinder.App/Controls/EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
   - クールダウン時間（`StylusSuppressionCooldownMs`）を 150ms に変更。
   - `OnPreviewStylusDown` / `Move` / `Up` において `IsStylusDevice(e)` が true の場合のみ `base` 処理を呼び出し、タッチによるインク描画を根本遮断。
   - `IsStylusDevice` のアクセシビリティを internal に変更。
2. [MODIFY] [`tests/PDFBinder.Tests/EditorInkCanvasTouchTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/EditorInkCanvasTouchTests.cs)
   - 150ms クールダウン基準に合わせた単体テストの更新。
3. [MODIFY] [`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md)
   - 機能インベントリ（F47）の更新。

---

## 検証結果

### 自動テスト結果
```
C:\Git\PDFBinder\tests\PDFBinder.Tests\bin\Debug\net10.0-windows\PDFBinder.Tests.dll (.NETCoreApp,Version=v10.0) のテスト実行
VSTest のバージョン 18.0.1 (x64)

テスト実行を開始しています。お待ちください...
合計 1 個のテスト ファイルが指定されたパターンと一致しました。

成功!   -失敗:     0、合格:   103、スキップ:     0、合計:   103、期間: 896 ms - PDFBinder.Tests.dll (net10.0)
```
- 全103件の単体テストが 100% 合格。

### ビルド結果
```
ビルドに成功しました。
    0 個の警告
    0 エラー
```
- 警告・エラー 0 件でビルド成功。
