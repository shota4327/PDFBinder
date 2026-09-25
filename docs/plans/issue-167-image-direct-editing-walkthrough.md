# Walkthrough: Issue #167 画像データの直接編集対応および編集タブへの名称変更

## 1. 概要
- **Issue**: #167 画像データの直接編集に対応する
- **目的**: 
  - JPEG (`.jpg`, `.jpeg`) / PNG (`.png`) ファイルを PDF Binder で直接開き、回転（90度単位）や手書きアノテーション（ペン・蛍光ペン・直線・消しゴム・太さ・色）を行い、元の画像形式またはPDF形式として保存できるようにする。
  - リボンタブの「PDF編集」表記を、画像にも適合する「編集」に変更する。
  - 画像編集時は、単一画像編集に特化するため不適正なPDF構成変更機能（結合・白紙追加・抽出/分割・削除・グリッド俯瞰・連続スクロール・ページ送り）を安全に無効化（グレーアウト）する。

---

## 2. 変更概要

### 2.1 コアデータモデル & ドメインサービス
- **`DocumentKind.cs`** (新規作成): ドキュメントの種別を表現する `enum DocumentKind { Pdf, Image }` を定義。
- **`PdfDocumentModel.cs`**:
  - `DocumentKind` および `IsImage` プロパティを追加。
  - `FileName` において画像ファイル名も正しく抽出・返却するよう整備。
- **`IImageService.cs` / `ImageService.cs`** (新規作成):
  - `IsSupportedImage`: `.jpg`, `.jpeg`, `.png` の拡張子判定。
  - `LoadImageDocumentAsync`: 画像ファイルを1ページの `PdfDocumentModel`（`DocumentKind = DocumentKind.Image`）として読み込み。DetailEditorのズーム100%時に 1:1 ピクセル表示となるようpt換算（`Width = PixelWidth * 72.0 / 96.0`）。元画像のピクセル解像度およびDPIを保持。
  - `SaveImageAsync`: 元画像に回転および手書きストロークを高解像度アルファ合成して保存。元画像のピクセル解像度（96 DPI基準）にスケール（`96.0 / 72.0`）を合わせ、高品質JPEG（品質約92%）または可逆PNGでファイル出力。一時ファイル出力からの不可分置換（アトミック保存）を実施。
  - `ExportToPdfAsync`: PdfSharp を使用し、回転・手書きストローク（アピアランス付き注釈＋ISFメタデータ）を含んだ単一ページPDFドキュメントを生成・保存。
- **`PdfService.cs`**:
  - `AttachInkAnnotationToPage` および `SafeReplaceFile` を `internal static` に変更し、`ImageService` との処理共通化・再利用を実現。
- **`PdfiumRenderer.cs`**:
  - `RenderPageAsync`: 画像ファイルの場合は PDFium のラスタライズを行わず、メモリ上の元画像ビットマップをそのまま背景画像として返却。
  - `ExtractInteractiveDataAsync`: 画像ファイルの場合は文字抽出や注釈リンク抽出をスキップ（空データを即時返却）。

### 2.2 アプリケーション & UI
- **`DocumentSession.cs`**: ドキュメントセッションに `IsImage` プロパティを追加。
- **`CommandLineArgsHelper.cs`**: コマンドライン引数処理において `.jpg`, `.jpeg`, `.png` を対象ファイルとして抽出可能に拡張。
- **`MainViewModel.cs`**:
  - `IImageService` を DI 注入。
  - プロパティ追加: `IsImageDocumentActive`
  - 画像編集時の機能制御プロパティ追加: `CanAppendDocument`, `CanAddBlankPage`, `CanExportSelectedPages`, `CanSplitAllPages`, `CanSplitPagesHalf`, `CanDeleteSelectedPages`, `CanToggleViewMode`, `CanToggleContinuousScroll`, `CanClosePageDetail`
  - 各コマンド（`AppendDocument`, `AddBlankPage`, `DeleteSelectedPages`, `ExportSelectedPages`, `SplitAllPages`, `SplitPagesHalf`, `ClosePageDetail`）の実行可否（`CanExecute`）を連動。
  - `OpenDocumentAsync` / `OpenSingleDocumentAsync`: 画像ファイルを独立した新規セッション（単一タブ）としてオープン。複数画像オープン時も1枚ごとに別タブとして生成。
  - `SaveDocumentAsSessionAsync`: 画像ドキュメント時のファイル保存フィルタに「JPEG画像」「PNG画像」「PDFドキュメント」を表示・切替。
  - `ExecuteSaveForDocumentAsync`: 画像ドキュメントの場合は `ImageService.SaveImageAsync`（上書きまたは画像形式保存）または `ExportToPdfAsync`（PDF形式保存）へルーティング。
  - `OnIsDetailViewActiveChanged`: 画像編集時はグリッドビューへの切り替えを抑止し詳細ビューを維持。
  - `HandleFileDropAsync` / `InsertPdfFilesAsync`: ドロップされたファイルのうち画像ファイルは常に新規タブとして開き、グリッドへの差し込み結合から除外。
- **`MainWindow.xaml`**:
  - リボンタブ 0 の表記を「PDF編集」から「編集」に変更。
  - 表示モード切替（詳細/グリッド）および連続スクロール切替ラジオボタンに `CanToggleViewMode`, `CanToggleContinuousScroll` をバインドし、画像時は無効化（グレーアウト）。
- **`MainWindow.xaml.cs` / `GridView.xaml.cs`**:
  - ファイルドラッグ＆ドロップ時の拡張子判定に画像形式（`.jpg`, `.jpeg`, `.png`）を許可。

### 2.3 バージョン管理・ドキュメント更新
- **`Directory.Build.props`**: 新機能追加に伴い、`0.4.3` から `0.5.0` へマイナーバージョンインクリメント。
- **`CHANGELOG.md`**: `## [0.5.0] - 2026-09-25` セクションを作成し、エンドユーザー向けの機能リリースノートを確定。
- **`docs/basic_design.md`**: 第8章「画像ファイル（JPEG / PNG）の直接編集・合成保存仕様」を追記、リボンタブ名「編集」を反映。
- **`README.md`**: 主な機能に「画像ファイル（JPEG / PNG）の直接編集・保存」を追記。
- **`docs/PROJECT.md`**: 機能インベントリに F64（編集タブ名称変更）および F65（画像データ直接編集）の完了を反映。

---

## 3. テスト・検証結果

### 3.1 自動テストの追加
- **`ImageServiceTests.cs`**:
  - `IsSupportedImage_ValidExtensions_ReturnsTrue`: `.jpg`, `.jpeg`, `.png` の大文字小文字対応判定
  - `IsSupportedImage_InvalidExtensions_ReturnsFalse`: `.pdf`, `.txt`, 無効拡張子の除外判定
  - `LoadImageDocumentAsync_Png_LoadsPageWithCorrectDimensions`: PNG画像のピクセル寸法・DPI・DIP/pt換算寸法の整合性検証
  - `SaveImageAsync_PngWithRotationAndInk_SavesCompositedImage`: 回転および手書きストローク合成後のPNG画像生成検証
  - `SaveImageAsync_Jpeg_SavesValidJpeg`: JPEG形式での合成保存検証
  - `ExportToPdfAsync_ImageDocument_GeneratesValidPdf`: 画像ドキュメントの単一ページPDF書き出しおよびISFメタデータ埋め込み検証
- **`MainViewModelImageTests.cs`**:
  - `OpenSingleDocumentAsync_ImageFile_OpensAsImageSession`: 画像ファイルが `IsImage = true` の独立セッションとして開くことの検証
  - `ImageDocument_DisablesPdfSpecificFeatures`: 画像アクティブ時に `CanAppendDocument`, `CanAddBlankPage`, `CanExportSelectedPages`, `CanSplitAllPages`, `CanSplitPagesHalf`, `CanDeleteSelectedPages`, `CanToggleViewMode`, `CanToggleContinuousScroll`, `CanClosePageDetail` がすべて `false` となることの検証
  - `PdfDocument_EnablesAllFeatures`: PDFアクティブ時に上記機能が利用可能となることの検証
  - `ImageDocument_CannotSwitchToGridView`: 画像アクティブ時に `IsDetailViewActive = false` を設定しても詳細ビュー（`true`）へ復帰することの検証
  - `HandleFileDropAsync_MultipleImages_OpensAsSeparateSessions`: 複数画像をドロップした際にそれぞれ独立したタブとして開くことの検証
  - `InsertPdfFilesAsync_MixedFiles_OpensImagesSeparately`: PDFと画像が混在したドロップで、PDFはグリッドへ差し込まれ、画像は別タブとして開くことの検証
  - `ExecuteSaveForDocumentAsync_ImageSaveAndPdfExport_CallsImageService`: 画像ドキュメントの上書き保存およびPDFエクスポートのルーティング検証
- **`CommandLineArgsHelperTests.cs`**:
  - `FilterPdfFiles_IncludesSupportedImageFiles`: コマンドライン引数からPDFおよび画像形式が正しく抽出されることの検証

### 3.2 テスト実行結果
```text
dotnet test
  Passed! - Failed: 0, Passed: 448, Skipped: 0, Total: 448
```
全 448 件の単体テストが 100% 成功。

### 3.3 ビルド検証
```text
dotnet build
  0 警告
  0 エラー
  ビルドに成功しました。
```
警告・エラーとも 0 件でビルド成功。
