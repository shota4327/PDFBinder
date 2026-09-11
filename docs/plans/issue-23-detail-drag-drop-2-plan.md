# Implementation Plan - 詳細ビューおよびグリッドビューでのPDFドラッグ＆ドロップ共通化 (Issue #23 追加改修-2)

詳細ビューおよびグリッドビューの両方において、エクスプローラー等からの外部PDFファイルのドラッグ＆ドロップによる追加結合（未読み込み時は新規表示）を完全に共通化し、未読み込み時のウェルカム表示およびドラッグ中の視覚的フィードバック（ドロップ案内オーバーレイ）を統一して提供します。

## ユーザー要求と合意事項
- **PDF未読み込み時の初期表示（両ビュー共通）**:
  - グリッドビューの初期状態と同様のウェルカムカードを詳細ビューにも表示し、未読み込み時の見た目と操作案内を完全に統一する。
  - 「PDFファイルを開くか、ここにドラッグ＆ドロップしてください」
  - 「複数PDFの結合、ページの並び替え・回転・削除、手書き記入が可能です」
  - 「ファイルを開く...」ボタン
- **ドラッグ＆ドロップの基本動作**:
  - 外部PDFファイルがドロップされた場合、未読み込み（0ページ）時は新規オープン、既にPDFが開かれている場合は末尾に追加結合する。
  - 追加結合後も、詳細ビューの現在のスクロール位置を維持する（表示中のページをそのまま閲覧可能）。
- **視覚的フィードバック（ドロップ案内オーバーレイの共通表示）**:
  - **詳細ビューおよびグリッドビューのどちらを表示中であっても**、PDF読み込み済みの状態で外部PDFをドラッグした際は、メイン表示領域（リボンとステータスバーの間）全体に共通の半透明ドロップ案内オーバーレイを表示（「ここにPDFをドロップして末尾に追加・結合」）。
  - PDF未読み込み時は初期ウェルカム表示が既にドラッグ＆ドロップ案内を兼ねているため、ドラッグ中も自然に受入可能とし、適切なドロップエフェクト（`DragDropEffects.Copy`）を提示。
  - PDF以外のファイルやドラッグ領域外への離脱時はオーバーレイを非表示とし、ドロップを受け付けない。

---

## 提案する変更内容

### 1. メイン画面・UI層 (`src/PDFBinder.App/`)

#### [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- メイン画面切り替えエリア（`Grid Grid.Row="2"`）に `AllowDrop="True"` を設定。
- ドラッグ＆ドロップイベント（`PreviewDragEnter`, `PreviewDragOver`, `PreviewDragLeave`, `Drop`）を設定。
- `Grid Grid.Row="2"` の最前面レイヤーとして、ドラッグ中にのみ表示される共通ドロップ案内オーバーレイ（半透明背景、点線枠、アイコン、案内メッセージ「ここにPDFをドロップして末尾に追加・結合」）を配置。
  - `IsDetailViewActive` に依存せず、読み込み済み（`Document.Pages.Count > 0`）かつファイルドラッグ中に共通表示される。

#### [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- ページ数が0件（未読み込み）の場合に、GridViewと同様のウェルカムカード（📄アイコン、説明文、「ファイルを開く...」ボタン）を表示するレイアウトを追加。

#### [GridView.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml.cs)
- 外部ファイルドロップ（`DataFormats.FileDrop`）は親の `MainWindow` で統一的に処理・オーバーレイ表示するため、カード間の並び替え（`PdfPageModel`）のみを `GridView` で処理し、ファイルドロップ処理を共通化。

#### [MainWindow.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml.cs)
- ドラッグイベントハンドラーを実装:
  - `PreviewDragEnter` / `PreviewDragOver`: `DataFormats.FileDrop` を検証し、PDFファイルが含まれる場合に `DragDropEffects.Copy` を設定。読み込み済みであればオーバーレイを活性化（`IsDragOver = true`）。
  - `PreviewDragLeave`: ドラッグ離脱時にオーバーレイを非表示化（`IsDragOver = false`）。
  - `Drop`: ドロップ完了時にオーバーレイを非表示化し、ViewModel のファイルドロップ処理（`HandleFileDropAsync`）を呼び出す。

#### [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- プロパティ追加:
  - `IsDragOver` (`bool`): ドロップ案内オーバーレイの表示フラグ。
- メソッド追加:
  - `HandleFileDropAsync(IEnumerable<string> filePaths)`:
    - 渡されたPDFファイルを判定し、0ページなら `OpenDocumentAsync`、既存ページありなら `AppendDocumentAsync` を順次実行。
    - ドロップ完了後も現在のスクロール位置を維持。

---

### 2. 単体テスト層 (`tests/PDFBinder.Tests/`)

#### [DefaultDetailViewTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DefaultDetailViewTests.cs)
- ドロップ処理に関する単体テストを追加:
  - `HandleFileDrop_WhenNoDocument_OpensDocument`: ドキュメントが空の状態でドロップされた場合、新規に読み込まれること。
  - `HandleFileDrop_WhenDocumentLoaded_AppendsToDocument`: 既にページが存在する状態でドロップされた場合、末尾に結合されること。
  - `HandleFileDrop_FiltersNonPdfFiles`: PDF以外の拡張子のファイルが除外されること。

---

## 検証手順

### 自動テスト
```powershell
dotnet test
```
- 全テスト（既存96件＋新規追加テスト）が 100% PASS することを確認。

### ビルド検証
```powershell
dotnet build
```
- 警告・エラー 0 件で正常ビルドできることを確認。
