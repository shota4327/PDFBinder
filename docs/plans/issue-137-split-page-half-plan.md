# 実装計画 - Issue #137: 全ページを半分に分割する「ページ分割」機能の追加

全分割ボタンの右に「ページ分割」ボタンを追加し、現在のドキュメントの全ページを対象に、それぞれの表示向きにおける長辺を2等分（A3横ならA4縦2枚、A3縦ならA4横2枚）に分割して置き換える機能を実装します。

## 決定事項のまとめ
- **分割方向・基準**: 画面上の表示向き（回転後の `DisplayWidth` / `DisplayHeight`）に基づいて長辺を2等分（横長なら左右分割、縦長なら上下分割）。
- **配置順序**: 横長は左ページ→右ページ、縦長は上ページ→下ページの順。
- **対象ページ**: 常に全ページを一括分割。元のページは削除され、2つの新しいページに置き換わる。
- **Undo/Redo**: 完全対応（`IUndoRedoService` に対応したコマンドを記録し、Ctrl+Z で分割前の状態に復元可能）。
- **確認ダイアログ**: 表示せず即時実行。
- **手書きストローク（InkStrokes）**: 分割前の手書きストロークも位置に応じてそれぞれの分割後ページへ座標変換して引き継ぐ。中央の境界線を跨ぐストロークは境界線で2つに切断して各ページへ分配する。
- **UI**:
  - 位置: 「全分割」ボタンの右隣（ページ構成・抽出グループ内）
  - 表示名: 「ページ分割」
  - アイコン: `\uE14E`（Material Symbols `content_cut`: ハサミ）
  - ツールチップ: 「全ページを半分に分割 (A3→A4等)」
  - ショートカットキー: 割り当てなし

---

## ユーザーレビュー確認事項
> [!IMPORTANT]
> - 既存のPDFコンテンツは PdfSharp の `XPdfForm` または CropBox 切り出しを用いて、一時ファイルに分割後ページを生成し、`PdfPageModel` に割り当てます。
> - 手書きストロークは境界線（左右分割なら $x = \text{width} / 2$、上下分割なら $y = \text{height} / 2$）で正確に切断・座標シフトされ、分割後の各ページに保持されます。
> - 全体の操作は単一の Undo アクション（`SplitPagesAction`）として Undo スタックに積まれるため、Ctrl+Z 1回で分割前の全ページ状態に巻き戻せます。

---

## 変更対象コンポーネント

### 1. コアロジック (PDFBinder.Core)

#### [NEW] [StrokeSplitHelper.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Helpers/StrokeSplitHelper.cs)
- 手書きストローク（`StrokeCollection`）を指定された境界線（水平または垂直）で2つに切断し、それぞれのページローカル座標系に変換した2つの `StrokeCollection` を生成するヘルパークラス。
- 線分と境界線の交差判定・内分計算を行い、ストロークを滑らかに2分割。

#### [MODIFY] [IPdfService.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/IPdfService.cs)
- `Task<List<PdfPageModel>> SplitPagesHalfAsync(IEnumerable<PdfPageModel> pages, CancellationToken cancellationToken = default);` を追加。

#### [MODIFY] [PdfService.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfService.cs)
- `SplitPagesHalfAsync` の実装:
  - 白紙ページの場合は、サイズを半分にした2つの新しい白紙 `PdfPageModel` を生成。
  - 実PDFページの場合は、一時PDFファイルを作成し、PdfSharpの `XGraphics` と `XPdfForm` を用いて、左半分/右半分（または上半分/下半分）をそれぞれクリッピングして新しいページに描画・保存。
  - ストロークがある場合は `StrokeSplitHelper` を利用して分割・座標変換し、新しい `PdfPageModel` に設定。
  - 新しい `PdfPageModel`（一時PDFを参照）のリストを返却。

#### [NEW] [SplitPagesAction.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/UndoRedo/SplitPagesAction.cs)
- `IUndoableAction` の実装。
- 元のページリスト（および選択状態）を保持し、Undo 時に元のページ群へ差し戻し、Redo 時に分割後ページ群へ再置換する。

### 2. ViewModel & UI (PDFBinder.App)

#### [MODIFY] [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- `SplitPagesHalfCommand`（`IAsyncRelayCommand`）を追加。
  - ドキュメントが存在しページがある場合に実行可能。
  - `_pdfService.SplitPagesHalfAsync(Document.Pages)` を呼び出し、結果をドキュメントのページリストに置換。
  - `UndoRedoService.RecordAction(new SplitPagesAction(...))` を登録。
  - `IsModified = true` を設定し、サムネイル再生成・状態通知をトリガー。

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- 「全分割」ボタンの右隣に「ページ分割」ボタンを追加。
  - Style: `RibbonToolbarButtonStyle`
  - Command: `SplitPagesHalfCommand`
  - Icon: `\uE14E`
  - Text: `ページ分割`
  - ToolTip: `全ページを半分に分割 (A3→A4等)`

### 3. ドキュメント更新

#### [MODIFY] [basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
- ページ構成・抽出グループに「ページ分割（長辺2等分分割）」の仕様・操作を追加。

#### [MODIFY] [README.md](file:///c:/Git/PDFBinder/README.md)
- 機能一覧のページ構成・抽出項目に「ページ分割」を追加。

#### [MODIFY] [PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
- 機能インベントリに Issue #137 のステータスを反映。

---

## 検証計画

### 自動テスト
- `StrokeSplitHelperTests.cs`:
  - 水平境界線・垂直境界線でのストローク切断（跨ぐ場合、片側のみの場合、複数点の場合）。
- `PdfServiceSplitHalfTests.cs`:
  - 横長A3ページ（420x297mm）を左右2つのA4縦ページ（210x297mm）に分割できることの検証。
  - 縦長A3ページ（297x420mm）を上下2つのA4横ページ（297x210mm）に分割できることの検証。
  - 白紙ページの分割検証。
  - 回転（90度、270度）が適用されたページの向き判定と分割検証。
- `MainViewModelSplitHalfTests.cs`:
  - コマンド実行によるページ置換。
  - Undo（Ctrl+Z）による元ページ復元、Redo（Ctrl+Y）による再実行の検証。
- 実行コマンド: `dotnet test`
- ビルド確認: `dotnet build`

### 手動検証
- 実際にアプリをビルドして起動し、A3PDF（横・縦）を読み込んで「ページ分割」ボタンを押し、綺麗にA4に分割されることおよびサムネイル表示・Undo/Redoが正常に動作することを確認。
