# 実装計画 - Issue #137 追加改修-2: A3横分割時の回転ズレ防止および詳細ビュー即時更新

A3横などのPDF分割時において、コンテンツが回転ズレ（横倒し・縦向き化）を起こす問題、および詳細ビュー（手書きエディタ）表示中に分割を実行した際に表示が即時更新されない問題を根本解決します。

---

## 原因分析

1. **コンテンツが回転して縦向き（横倒し）になる原因**:
   - スキャナやオフィスソフトから出力された横向きPDFの多くは、用紙定義が縦長（例: 842×1190）でメタデータ `/Rotate 90` または `270` が設定されています。
   - 既存の `XPdfForm` による描画方式では、PDFSharp の `XPdfForm` が元PDFの `/Rotate` を解釈して自動適用する挙動と、アプリ側で行っていた手動回転変換（`page.Rotation`）が二重に重なり、余計な90度回転および縦横比の歪みが発生していました。
2. **分割直後に詳細ビューが更新されずA3のままになる原因**:
   - `MainViewModel.SplitPagesHalfAsync` において、`_pdfService.SplitPagesHalfAsync` を実行した後に、詳細エディタ（`DetailEditorViewModel`）に対するドキュメント再同期（`DetailEditor?.InitializeDocument(Document)`）が呼び出されていませんでした。
   - このため、詳細ビュー表示中に分割を実行した場合、詳細ビューのアイテムが古いA3ページのまま残っていました。

---

## 改善アプローチ

### 1. `CropBox` / `MediaBox` ネイティブクリッピング方式への刷新
`XPdfForm` による再描画・ラスタライズを廃止し、PdfSharp ネイティブの `outputDoc.AddPage(sourcePage)` に刷新します。
- **コンテンツ劣化ゼロ**: 元PDFのテキスト・ベクター・画像・フォント・解像度・既存注釈が1バイトも損なわれず、100%完全なクオリティで保持されます。
- **回転整合性**: 元ページの `Rotate` プロパティをそのまま保持し、ページの表示向き（`DisplayWidth` / `DisplayHeight`）に応じて、PDF生座標系の `CropBox` / `MediaBox` を左右（または上下）に正確に切り出します。
  - `Rotate == 0`: 左右分割なら $X=0 \sim W/2$, $X=W/2 \sim W$
  - `Rotate == 90`: 画面上の左右分割は PDF生座標系の $Y=0 \sim H/2$, $Y=H/2 \sim H$
  - `Rotate == 180`: 画面上の左右分割は PDF生座標系の $X=W/2 \sim W$, $X=0 \sim W/2$
  - `Rotate == 270`: 画面上の左右分割は PDF生座標系の $Y=H/2 \sim H$, $Y=0 \sim H/2$
  - （縦長ページの上下分割も同様にマッピング）

### 2. ViewModel の同期処理
- `MainViewModel.SplitPagesHalfAsync` において:
  - `DetailEditor?.InitializeDocument(Document);` を呼び出し、詳細ビューのページアイテムを分割後ページ群へ即座に更新。
  - 詳細ビューアクティブ時は `DetailEditor?.ScheduleDynamicRender(immediate: true)` を実行。
  - グリッドビューアクティブ時は `EnsureThumbnailsGeneratedAsync()` を実行。
- `MainViewModel.Undo` / `Redo` においても、全ページ置換（`ReplaceAllPagesCommand`）の際に `DetailEditor?.InitializeDocument(Document)` を呼び出して確実に画面同期。

---

## 変更対象ファイル

### 1. コアロジック (PDFBinder.Core)
- **`PdfService.cs`**:
  - `AppendSplitPdfPages` を `CropBox` / `MediaBox` 設定方式に刷新。
  - `XPdfForm` の使用を廃止し、`AddPage(sourcePage)` を使用。
  - 各分割後ページの `Width` / `Height` / `Rotation` / `OriginalRotation` を `CropBox` の寸法に合わせて設定。

### 2. ViewModel (PDFBinder.App)
- **`MainViewModel.cs`**:
  - `SplitPagesHalfAsync` に `DetailEditor?.InitializeDocument(Document)` を追加。
  - `Undo` / `Redo` に `DetailEditor?.InitializeDocument(Document)` を追加。

### 3. 単体テスト (PDFBinder.Tests)
- `SplitInvestigationTests.cs` の実験コードを整理し、`PdfServiceSplitHalfTests.cs` に回転付き横向きPDF（Rotate90 / 270）の分割検証テストを追加。
- `MainViewModelSplitHalfTests.cs` に詳細エディタ同期の検証を追加。

---

## 検証計画
- `dotnet test`: 全単体テストの実行（回転付きPDFのCropBox分割およびViewModel同期テストを含む）。
- `dotnet build`: 警告0・エラー0の確認。
