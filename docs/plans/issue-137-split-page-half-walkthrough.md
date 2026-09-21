# 検証報告 (Walkthrough) - Issue #137: 全ページを半分に分割する「ページ分割」機能の追加

Issue #137 に基づき、現在のドキュメントの全ページを対象に、画面上の表示向き（回転考慮後の `DisplayWidth` / `DisplayHeight`）における長辺を2等分（横長なら左右分割、縦長なら上下分割）する「ページ分割」機能を実装しました。

---

## 変更内容の概要

### 1. コアロジック (PDFBinder.Core)
- **`StrokeSplitHelper.cs` (新規)**:
  - 手書きストローク（`StrokeCollection`）を指定された境界線（水平または垂直）で線形補間により交点を算出して2つに切断。
  - 後半側（右または下）のページに属するストロークの頂点座標を新ページのローカル座標系（オフセット減算）へ正確に変換して引き継ぐ機能を実装。
- **`IPdfService.cs` / `PdfService.cs` (拡張)**:
  - `Task<List<PdfPageModel>> SplitPagesHalfAsync(IEnumerable<PdfPageModel> pages, CancellationToken cancellationToken = default);` を追加。
  - 白紙ページ: 寸法を半分にした新しい白紙ページを2つ生成。
  - 実PDFページ: PdfSharp の `XPdfForm` と `XGraphics` を用いて、回転やクリッピングを考慮して半分ずつ描画した一時PDFを生成。
  - 手書きストロークがある場合は `StrokeSplitHelper` により両ページに分配。
- **`UndoRedoService.cs` (拡張)**:
  - `ReplaceAllPagesCommand`: 全ページの置換およびUndo/Redo復元コマンドを実装。

### 2. ViewModel & UI (PDFBinder.App)
- **`MainViewModel.cs` (拡張)**:
  - `SplitPagesHalfCommand`: ドキュメントの全ページを一括分割し、Undo履歴に登録、サムネイル再生成をトリガー。
- **`MainWindow.xaml` (更新)**:
  - 「全分割」ボタンの右隣（ページ構成・抽出グループ内）に「ページ分割」ボタンを追加。
  - アイコン: `\uE14E` (Material Symbols `content_cut`: ハサミ)
  - ツールチップ: `全ページを半分に分割 (A3→A4等)`

### 3. ドキュメント整備
- `docs/basic_design.md`: サービス定義、ヘルパー定義、UI構成に「ページ分割」を反映。
- `README.md`: 主な機能「分割＆抽出」に「ページ分割」を追加。
- `docs/PROJECT.md`: 機能インベントリに F56（ページ分割機能）を追加、テスト件数（294件全PASS）を更新。

---

## 検証結果

### 1. 自動テスト (単体テスト)
新規テストを追加し、既存テストを含む全テストが100%パスすることを確認しました。
- `StrokeSplitHelperTests.cs`:
  - 境界線切断、片側のみ、空コレクション、オフセットシフト等の幾何計算を検証（5件 PASS）。
- `PdfServiceSplitHalfTests.cs`:
  - 白紙ページ（横長・縦長）、実PDFファイル（横長・縦長）、境界跨ぎインクストローク分配、回転適用ページの長辺分割を検証（6件 PASS）。
- `MainViewModelSplitHalfTests.cs`:
  - コマンド実行によるドキュメントページ置換、Undo（元に戻す）、Redo（やり直す）、空ドキュメント時の安全性を検証（3件 PASS）。
- `PdfSharpSplitExperimentTests.cs`:
  - 低レベル描画および全回転（0°, 90°, 180°, 270°）での分割描画を検証（5件 PASS）。

```text
成功!   -失敗:     0、合格:   294、スキップ:     0、合計:   294、期間: 971 ms - PDFBinder.Tests.dll (net10.0)
```

### 2. ビルド検証
- `dotnet build`: 警告 0、エラー 0 でビルド成功。
