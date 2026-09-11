# Walkthrough - 詳細ビューおよびグリッドビュー共通PDFドラッグ＆ドロップ対応 (Issue #23 追加改修-2)

詳細ビューおよびグリッドビューの双方において、外部PDFファイルをドラッグ＆ドロップして末尾に追加結合（未読み込み時は新規オープン）できる機能を実装し、未読み込み時のウェルカム表示の統一、およびドラッグ中の共通ドロップ案内オーバーレイ表示を実現しました。

## 変更内容の概要

### 1. メイン画面のファイルドロップ・オーバーレイ統合
- [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml):
  - メイン画面切り替えエリア（`Grid Grid.Row="2"`）に `AllowDrop="True"` およびドラッグ＆ドロップトンネリングイベント（`PreviewDragEnter`, `PreviewDragOver`, `PreviewDragLeave`, `PreviewDrop`）を設定。
  - 最前面レイヤーに共通ドロップ案内オーバーレイ（点線枠、アイコン、案内文「ここにPDFをドロップして末尾に追加・結合」）を追加。
  - `IsDragOver` プロパティと連動し、PDF読み込み済みの状態で外部PDFがドラッグされた時にのみ半透明オーバーレイを表示。
- [MainWindow.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml.cs):
  - `UpdateDragState` メソッドにて `DataFormats.FileDrop` を検知し、拡張子 `.pdf` の検証を実施。
  - PDFファイルが含まれている場合に `DragDropEffects.Copy` を適用し、読み込み済みであればオーバーレイを活性化。
  - `OnMainAreaDrop` でドロップされたファイル群を取得し、ViewModelの `HandleFileDropAsync` を呼び出し。

### 2. ViewModel層のドロップ処理と状態管理
- [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs):
  - `IsDragOver` プロパティを追加。
  - `HandleFileDropAsync(IEnumerable<string>? filePaths)` メソッドを実装。
  - ドキュメントが0ページの場合は先頭ファイルを新規オープン（`OpenDocumentAsync`）し、残りのファイルを末尾に結合（`AppendDocumentAsync`）。
  - ドキュメント読み込み済みの場合は全ファイルを末尾に順次結合。ドロップ後も詳細ビューの現在のスクロール位置を維持。

### 3. 未読み込み時表示の統一とGridView整理
- [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml):
  - ページ数が0件の場合に、GridViewと同様のウェルカムカード（📄アイコン、「PDFファイルを開くか、ここにドラッグ＆ドロップしてください」、「ファイルを開く...」ボタン）を表示。
- [GridView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml) / [GridView.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml.cs):
  - ルート要素の重複したファイルドロップハンドラーを整理し、親のMainWindowで統一的にファイルドロップとオーバーレイを処理するよう集約（内部カードの並び替えハンドラーは維持）。

### 4. ドキュメントの同期更新
- [basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md): 未読み込み時表示の統一、詳細・グリッド共通のドラッグ＆ドロップおよびドロップ案内オーバーレイ仕様を反映。
- [README.md](file:///c:/Git/PDFBinder/README.md): 機能概要に詳細ビュー・グリッド共通のドラッグ＆ドロップ機能およびドロップ案内オーバーレイを追記。
- [PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md): Feature Inventory（F32, F50）のステータスを更新。

---

## 検証結果

### 1. 自動単体テスト
`DefaultDetailViewTests.cs` にドロップ処理に関するテスト3件を追加し、全テストスイートを実行しました。

```powershell
dotnet test
```

- **結果**: 成功 (99 / 99 テスト PASS)
  - `MainViewModel_HandleFileDrop_WhenNoDocument_OpensFirstAndAppendsSubsequent`: 空ドキュメント時に先頭がオープンされ以降が追加結合されること
  - `MainViewModel_HandleFileDrop_WhenDocumentLoaded_AppendsAllFiles`: ドキュメント読み込み済み時にすべてのファイルが末尾に追加結合されること
  - `MainViewModel_HandleFileDrop_FiltersNonPdfFiles`: PDF以外のファイル（.docx, .png, .txt等）が除外され処理されないこと

### 2. ソリューションビルド
```powershell
dotnet build
```

- **結果**: 成功（0 警告, 0 エラー）
