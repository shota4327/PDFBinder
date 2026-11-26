# 実装計画: Issue #251 見開き表示機能および PDF 初期表示フラグ（`/PageLayout`・`/ViewerPreferences` `/Direction`）対応

## 概要
- PDF のドキュメントカタログに含まれる **`/PageLayout`** および **`/ViewerPreferences` → `/Direction`** を読み込み時に解析し、初期表示モードへ自動反映します。
- 詳細手書きエディタに**「見開き（2ページ送り）」**および**「見開き連続（2ページずつの縦連続スクロール）」**モードと、**「表紙を単独表示」「右綴じ（左綴じ / 右綴じ切替）」**オプションを追加します。
- 見開き表示中でも、左右それぞれのページで手書き（ペン・蛍光ペン・消しゴム・直線）・テキスト選択・リンク・回転・ズーム・UI仮想化・動的メモリ解放が既存どおり動作する構成とします。

---

## 設計方針

### 1. PDF フラグの解析と初期モードへのマッピング
- **`/PageLayout`**:
  - 未指定（`Default`）→ `SinglePage`（従来既定を維持）、`ShowCoverPageSeparately = true`
  - `/SinglePage` → `SinglePage`、`ShowCoverPageSeparately = true`
  - `/OneColumn` → `Continuous`、`ShowCoverPageSeparately = true`
  - `/TwoPageLeft` → `TwoPage`、`ShowCoverPageSeparately = false`（`[1,2], [3,4]...`）
  - `/TwoPageRight` → `TwoPage`、`ShowCoverPageSeparately = true`（`[1], [2,3], [4,5]...`）
  - `/TwoColumnLeft` → `TwoPageContinuous`、`ShowCoverPageSeparately = false`
  - `/TwoColumnRight` → `TwoPageContinuous`、`ShowCoverPageSeparately = true`
- **`/ViewerPreferences` → `/Direction`**:
  - `/L2R` または未指定 → `LeftToRight`（左綴じ）
  - `/R2L` → `RightToLeft`（右綴じ）
- **適用タイミング**: ドキュメントの新規オープン時のみ PDF フラグを初期反映し、編集中の `InitializeDocument` 再同期ではユーザーが選択中の表示モードを維持する。画像ファイル（JPEG/PNG）は `SinglePage` 固定。
- **保存時の扱い**: 既存の `PdfMetadataHelper.TransferViewerPreferences` による元PDFメタデータ引き継ぎをそのまま維持し、一時的な表示切替で元ファイルのフラグを上書きしない（非破壊）。

### 2. 見開きレイアウト構造
- 見開き1行（最大2ページ）を表す `DetailSpreadRowViewModel` を導入し、左右それぞれのスロットに既存の `DetailPageItemTemplate` を配置する。
- `TwoPageContinuous` では `SpreadRows` を既存の `VirtualizingPageStackPanel` にバインドし、大規模PDFでも UI 仮想化を維持する。
- 単独ページ（表紙・最終余りページ）の水平配置:
  - `TwoPage`（単一見開き）: 画面中央に配置し、ウィンドウフィット時に見やすく大きく表示。
  - `TwoPageContinuous`（見開き連続）: 綴じ目（中央線）を揃えるため透明スペーサーにより L2R 表紙＝右・裏表紙＝左、R2L 表紙＝左・裏表紙＝右に整列。

### 3. 操作性・ナビゲーション・レンダリング連携
- 見開きモード時のページ送り（`GoToPreviousPage` / `GoToNextPage` / マウスホイール端到達）は1見開き（1行）単位で移動する。
- 同一見開き内の相方ページをクリックした場合、`CurrentPage` は切り替わるが表示中の見開き行・拡大率は維持する。
- 右綴じ（`RightToLeft`）見開き時のキーボード操作は `←` で次の見開き、`→` で前の見開きへ移動する（`PgUp` / `PgDn` / `↑` / `↓` は常に前／次）。
- フィット計算（`FitToWindow` / `FitToWidth`）は、見開き時に現在見開き行の合計幅（左幅 + ギャップ + 右幅）と最大高さを基準に算出する。
- 4段階優先レンダリング＆動的メモリ解放は、見開き行内の最大2ページおよび前後見開き行を対象とする。

---

## 変更対象ファイル一覧

### Core 層 (`src/PDFBinder.Core/`)
- `[NEW] Models/PdfPageLayout.cs`
- `[NEW] Models/PdfReadingDirection.cs`
- `[MODIFY] Models/PdfDocumentModel.cs`
- `[MODIFY] Helpers/PdfMetadataHelper.cs`
- `[MODIFY] Services/PdfService.cs`

### App 層 (`src/PDFBinder.App/`)
- `[MODIFY] Models/DetailPageViewMode.cs`
- `[NEW] Helpers/SpreadLayoutHelper.cs`
- `[NEW] ViewModels/DetailSpreadRowViewModel.cs`
- `[MODIFY] ViewModels/DetailEditorViewModel.cs`
- `[MODIFY] Models/DocumentSession.cs`
- `[MODIFY] ViewModels/MainViewModel.cs`
- `[MODIFY] Controls/VirtualizingPageStackPanel.cs`
- `[MODIFY] Views/DetailEditorView.xaml`
- `[MODIFY] Views/DetailEditorView.xaml.cs`
- `[MODIFY] MainWindow.xaml`
- `[MODIFY] MainWindow.xaml.cs`

### テスト＆ドキュメント
- `[NEW] tests/PDFBinder.Tests/Helpers/SpreadLayoutHelperTests.cs`
- `[NEW] tests/PDFBinder.Tests/DetailEditorSpreadViewTests.cs`
- `[MODIFY] tests/PDFBinder.Tests/Services/PdfMetadataHelperTests.cs`
- `[MODIFY] docs/basic_design.md`, `docs/PROJECT.md`, `README.md`, `Directory.Build.props`, `CHANGELOG.md`
- `[NEW] docs/plans/issue-251-two-page-spread-view-walkthrough.md`

---

## 検証計画
1. `dotnet test` を実行し、新規・既存すべての単体テストが 100% PASS することを確認。
2. `dotnet build` を実行し、警告・エラーなしで成功することを確認。
3. 実ファイル検証（`sample.pdf` 等）で `/TwoPageRight` + `/L2R` が自動反映され、見開き表示・手書き・ページ送り・綴じ方向切替が正常に動作することを確認。
