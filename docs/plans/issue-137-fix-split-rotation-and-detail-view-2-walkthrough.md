# Issue #137: ページ分割の回転・向き不具合修正および詳細ビュー即時更新 検証報告 (Walkthrough - 2)

本ドキュメントは、Issue #137「ページ分割（A3→A4分割）」における追加改修課題である、以下2点の不具合修正および検証結果をまとめたものです。

1. **A3横PDF分割時の回転・向き崩れ**（横向きPDFが縦向きに倒れて分割される問題）
2. **詳細ビュー（手書きエディタ）表示中の分割・Undo/Redoにおける即時再描画・同期漏れ**（分割後も画面が古いA3表示のまま更新されない問題）

---

## 変更内容の概要

### 1. ページ分割ロジックの刷新 (`src/PDFBinder.Core/Services/PdfService.cs`)
- **従来の課題**:
  - `XPdfForm.FromFile` を用いて元PDFページを新規ページのグラフィックスに再描画する方式をとっていたため、元PDFのメタデータ `/Rotate 90` と手動回転変換が二重適用され、コンテンツが90度横倒し（縦長）になってしまっていた。
  - 再描画に伴いフォントやベクター品質の劣化、処理コストの懸念があった。
- **改修内容**:
  - `XPdfForm` による再描画方式を完全廃止。
  - 元PDFの生ページを `splitDoc.AddPage(srcPage)` で直接複製・追加し、`MediaBox` および `CropBox` を左右／上下の半分領域に直接設定するクリッピング方式へ刷新。
  - これにより、元PDFのベクターデータ、フォント、メタデータ（`/Rotate` 含む）が100%完全な品質で保持される。
  - `CalculateCropBoxes` ヘルパーメソッドを導入し、元ページの回転属性（0°, 90°, 180°, 270°）を考慮して、ユーザーの視覚的な表示向き（横長なら左右分割、縦長なら上下分割）に厳密に対応する物理座標領域（MediaBox/CropBox）を正確に計算する幾何学マッピングを実装。

### 2. 詳細エディタ（手書きビュー）の同期と即時更新 (`src/PDFBinder.App/ViewModels/MainViewModel.cs`)
- **従来の課題**:
  - 分割実行時や Undo/Redo 時に `Document.Pages` は正しく更新されていたが、詳細エディタ画面（`DetailEditor`）が表示中の場合、エディタ内部のページコレクション（`DetailEditor.Pages`）が再初期化されておらず、画面上のキャンバスが古いA3ページのまま再描画されていなかった。
- **改修内容**:
  - `SplitPagesHalfAsync` において、分割完了後に `DetailEditor?.InitializeDocument(Document)` を呼び出し、詳細ビューアクティブ時は `DetailEditor.ScheduleDynamicRender(immediate: true)` を実行して即座に画面を再描画。
  - `Undo` および `Redo` においても、`DetailEditor?.InitializeDocument(Document)` を追加し、履歴操作時も詳細エディタが最新状態に同期されるよう保証。
  - `OpenPageDetail` において、`DetailEditor.Pages.Count != Document.Pages.Count` の場合の防御的再同期処理を追加。

---

## 自動テスト・検証結果

### 1. 単体テスト結果 (`dotnet test`)
全 294 件の単体テストがすべて PASS しました。

- **`MainViewModelSplitHalfTests.cs`**:
  - `SplitPagesHalfCommand_InDetailView_SynchronizesDetailEditor`: 詳細ビュー表示中にページ分割を実行した場合、および Undo / Redo を実行した場合に、`DetailEditor.Pages` が正しく同期され即時再描画が要求されることを検証。
- **`SplitInvestigationTests.cs`**:
  - `SplitPagesHalfAsync_AllRotations_ProducesExpectedPages`: 0°, 90°, 180°, 270° の全回転角度を持つA3横／縦PDFにおいて、分割後の全ページが正しい寸法・向き・アスペクト比でレンダリングされ、黒画像化や横倒しが発生しないことを検証。
- **`PdfServiceSplitHalfTests.cs`**:
  - 新方式（CropBox直接クリッピング方式）に対応した寸法・回転属性の保持を検証。

```
成功!   -失敗:     0、合格:   294、スキップ:     0、合計:   294、期間: 973 ms - PDFBinder.Tests.dll (net10.0)
```

### 2. ビルド結果 (`dotnet build`)
警告 0、エラー 0 で正常にビルドが完了しました。

```
ビルドに成功しました。
    0 個の警告
    0 エラー
```
