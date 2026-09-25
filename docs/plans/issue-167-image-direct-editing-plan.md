# Issue #167: 画像データの直接編集対応 実装計画

## 1. 概要
JPEG・PNG画像ファイルをPDF Binderで直接開き、回転および手書き（ペン、マーカー、消しゴム、直線ツール等）の編集を行い、元の画像形式またはPDF形式として保存できるようにします。
画像ファイルは1枚ごとに独立した新規ドキュメント（セッション）として開き、PDF編集特有の機能（結合・分割・白紙追加・グリッド表示・連続スクロール等）は無効化（グレーアウト）します。
あわせて、画像編集にも適合するようリボンタブの「PDF編集」の表示名を「編集」に変更します。

---

## 2. 要件定義・仕様決定（ユーザーヒアリング結果）
- **対応画像形式**: JPEG (`.jpg`, `.jpeg`)、PNG (`.png`) に限定。
- **リボンタブ名称の変更**:
  - タブ 0 の名称「PDF編集」を「編集」に変更（PDF・画像双方に対応するUI表記への統一）。
- **保存仕様**:
  - **上書き保存 (Ctrl+S)**: 元の画像形式（JPEGまたはPNG）に回転および手書きストロークを高画質に合成して直接上書き保存。
  - **名前を付けて保存 (Ctrl+Shift+S)**: 画像形式（JPEG / PNG）および PDF形式（*.pdf）を選択して保存可能。
- **解像度・画質**:
  - 元画像のピクセル解像度およびDPIを維持。手書きストロークを元画像の解像度に合わせて高画質に合成。JPEG保存時は品質約92%でエンコード。
- **開き方・複数ファイル・ドラッグ＆ドロップ**:
  - ファイルダイアログ、エクスプローラーからのD&D、起動コマンドライン引数すべてに対応。
  - 画像ファイルは複数ページにはせず、必ず1枚ごとに別ドキュメント（タブ）として独立して開く。
  - 既存ドキュメント（PDF/画像問わず）へのドラッグ＆ドロップ時も、ページへの挿入・差し込みは行わず新規ドキュメントとして開く。
- **UI制御・グレーアウト**:
  - 画像ドキュメントがアクティブな場合は、以下を無効化（グレーアウト）:
    - 別のPDF結合・追加 (`AppendDocumentCommand`)
    - 白紙追加 (`AddBlankPageCommand`)
    - 選択ページ抽出 (`ExportSelectedPagesCommand`)
    - 全ページ分割 (`SplitAllPagesCommand`, `SplitPagesHalfCommand`)
    - ページ削除 (`DeleteSelectedPagesCommand`)
    - グリッド表示切替（詳細エディタ固定）
    - 連続スクロール切替（単一ページ表示固定）
    - ページ送り（前へ / 次へ）
  - 利用可能な機能:
    - 回転（左回転・右回転）
    - 手書きツール全般（ペン、蛍光ペン、消しゴム、直線、太さ、色、図形認識）
    - 印刷（印刷プレビューおよび実印刷）
    - ズーム・パン（拡大、縮小、ウィンドウ合わせ、手のひらツール）
    - アンドゥ / リドゥ (Ctrl+Z / Ctrl+Y)
    - 保存 / 名前を付けて保存
  - 初期リボンタブは「編集」タブを選択。

---

## 3. アーキテクチャおよびコンポーネント設計

### 3.1 モデル拡張 (`PDFBinder.Core`)
- `PdfDocumentModel`:
  - `DocumentKind` 列挙体（`Pdf` または `Image`）を追加。
  - `IsImage` プロパティ（`DocumentKind == DocumentKind.Image`）を追加。
- `PdfPageModel`:
  - `SourceFilePath` が画像ファイルの場合の寸法算出を整備（DPIとピクセル数からWPF pt寸法へ変換し、100%ズーム時に1:1ピクセル表示となるように設定）。

### 3.2 サービス実装 (`PDFBinder.Core`)
- `IImageService` / `ImageService`:
  - `Task<PdfDocumentModel> LoadImageDocumentAsync(string filePath)`: 画像ヘッダー・メタデータを読み込み、適切な寸法を持つ1ページのドキュメントを構築。
  - `Task SaveImageAsync(PdfDocumentModel doc, string outputPath)`:
    - 元画像ビットマップを読み込み、ページの回転角度に従って幾何回転。
    - InkStrokesの座標系（WPF論理ポイント）をビットマップの実ピクセル座標系へスケーリング変換。
    - `DrawingVisual` / `RenderTargetBitmap` を用いて、元画像の上に高品質にストロークを合成。
    - 拡張子に応じた `JpegBitmapEncoder` / `PngBitmapEncoder` でエンコードし、一時ファイル経由で安全にアトミック保存。
  - `Task SaveImageAsPdfAsync(PdfDocumentModel doc, string outputPath)`:
    - `PdfSharp` を使用し、画像および手書き注釈を含む1ページのPDFを作成して保存。
- `IPdfRenderer` / `PdfiumRenderer`:
  - `RenderPageAsync`: 対象ファイルが画像形式の場合、PDFiumではなくWPF画像デコーダーを用いて指定解像度・回転角度にレンダリングした `BitmapSource` を返却。
  - `ExtractInteractiveDataAsync`: 画像ファイルの場合は空の `PageInteractiveData` を返却（PDFテキスト/リンク注釈は存在しないため安全にスキップ）。

### 3.3 コマンドライン・引数処理 (`CommandLineArgsHelper.cs`)
- `.pdf` に加え、`.jpg`, `.jpeg`, `.png` も受け付けるように正規表現または拡張子判定を拡張。

### 3.4 ViewModel・UI制御 (`PDFBinder.App`)
- `MainViewModel`:
  - `OpenDocumentAsync`: ファイルダイアログのフィルターに `*.jpg;*.jpeg;*.png` を追加。
  - `OpenSingleDocumentAsync`: ファイル拡張子を判定し、画像の場合は `ImageService.LoadImageDocumentAsync` を呼び出して新規セッションを生成。
  - `HandleFileDropAsync`: PDFファイルと画像ファイルの両方を抽出し、順次 `OpenSingleDocumentAsync` で新規ドキュメントとして開く。
  - `SaveDocumentSessionAsync` / `SaveDocumentAsSessionAsync`:
    - 画像ドキュメントの場合、画像上書き保存または画像/PDF選択保存ダイアログへ分岐。
  - `CanExecute` / `IsEnabled` プロパティの拡充:
    - `IsImageDocumentActive` を監視し、結合・白紙・分割・削除・ビュー切替（グリッド/連続）のボタン状態をリアルタイム制御。
- `MainWindow.xaml`:
  - リボンタブ 0 の表記を「PDF編集」から「編集」に変更。
  - 対象ボタンの `IsEnabled` バインドを設定。

---

## 4. 単体テスト計画 (`PDFBinder.Tests`)
1. **`ImageServiceTests`**:
   - JPEGおよびPNGの読み込みテスト（寸法、ページ数1、`DocumentKind.Image`の検証）。
   - 回転および手書きストロークを付与した状態でのJPEG/PNG保存テスト（出力ファイル生成、画像サイズ、破損なしの検証）。
   - 画像ドキュメントのPDF保存テスト（生成されたPDFがPdfSharpで読み込めること）。
2. **`CommandLineArgsHelperTests`**:
   - JPEG・PNGファイルパスの解析・引数抽出テスト。
3. **`MainViewModelImageTests`**:
   - 画像ファイル読み込み時のセッション追加、`IsImageDocumentActive`のプロパティ変化の検証。
   - 画像ドキュメントアクティブ時の各コマンド（結合、白紙、分割等）の無効化検証。
   - グリッドビューへの切り替え抑止検証。

---

## 5. 検証手順 (Walkthrough)
1. `dotnet test` で新規および既存の全単体テストが 100% PASS することを確認。
2. `dotnet build` で警告およびエラーがないことを確認。
