# 実装計画: PDFからJPEG/PNGへの画像書き出し機能 (#30)

## 概要
PDFドキュメントの各ページをJPEGまたはPNG画像として指定解像度（200dpi, 300dpi, 400dpi, 600dpi）で高画質にエクスポートする「画像書き出し」機能を実装します。既存の印刷プレビュー機能と同様に、左側に設定パネル、右側に用紙連動プレビューを備えたインアプリ・オーバーレイダイアログを提供します。

---

## ユーザー合意事項（ヒアリング結果）
1. **UI配置（起動ボタン）**:
   - リボンの「編集」タブ内、「ページ構成・抽出」グループにある「ページ分割」ボタンの右横に配置する。
2. **出力先とファイル指定**:
   - 複数ページを書き出す場合: フォルダ選択ダイアログ（`OpenFolderDialog`）を表示し、`{ドキュメント名}_page_001.png` のようなゼロ埋め連番ファイルとして一括保存する。
   - 1ページのみ書き出す場合: ファイル保存ダイアログ（`SaveFileDialog`）を表示し、ユーザーがファイル名を指定して保存する。
3. **JPEG画質**:
   - 設定UIをシンプルに保つため、JPEG選択時は高品質（90%）に固定する。
4. **進捗表示と完了フィードバック**:
   - ダイアログ下部の進捗バーで書き出し進捗（例: 「ページ 3 / 10 を書き出し中...」）を表示する。
   - 完了時にダイアログを閉じ、メインウィンドウのステータスバーに完了通知を表示する。
5. **ショートカットキー**:
   - 設定せず、リボンボタンからの起動のみとする。

---

## 設計詳細

### 1. ドメインモデル & サービス (`PDFBinder.Core`)
- **`ImageExportSettings.cs`**:
  - `ImageExportFormat`: `Png`, `Jpeg`（デフォルト: `Png`）
  - `ImageExportDpi`: `Dpi200 = 200`, `Dpi300 = 300`, `Dpi400 = 400`, `Dpi600 = 600`（デフォルト: `Dpi400`）
  - `ImageExportRangeType`: `AllPages`, `CurrentPage`, `Custom`（デフォルト: `AllPages`）
  - `CustomRangeText`: `string`（ページ指定文字列、例: "1-3, 5"）
  - `PropertyChanged` を実装し、設定変更時にプレビューや検証を自動発火。
- **`IImageExportService.cs` & `ImageExportService.cs`**:
  - `Task SaveImageAsync(BitmapSource bitmap, string filePath, ImageExportFormat format, int dpi, int quality = 90)`
  - `(int Width, int Height) CalculatePixelSize(double pointWidth, double pointHeight, int dpi)`
    - PDFのポイントサイズ（72 DPI）から目標DPIにおけるピクセル寸法を正確に算出。
  - `PngBitmapEncoder` / `JpegBitmapEncoder` を使用してエンコード・ファイル出力。

### 2. ViewModel (`PDFBinder.App`)
- **`ExportImagesViewModel.cs`**:
  - `ImageExportSettings Settings`
  - ページ範囲の検証と対象ページリストの算出（`PrintLayoutCalculator.TryParsePageRange` を再利用）
  - `CurrentPreviewIndex` / `TotalExportPages` / `DisplayExportPageText`（例: "1 / 5"）
  - `BitmapSource? PreviewImage`, `bool IsLoadingPreview`
  - `string? RangeErrorMessage`, `bool CanExport`
  - `bool IsExporting`, `string ExportProgressText`, `double ExportProgressValue`
  - `ExecuteExportCommand`:
    - 対象ページ数に応じて `SaveFileDialog`（1ページ）または `OpenFolderDialog`（複数ページ）を表示。
    - キャンセル用 `CancellationTokenSource` 管理。
    - 指定DPIの高解像度レンダリング（手書きストローク合成込み）を順次実行し、指定フォーマットで保存。
    - 完了後に `RequestClose(true, exportedCount, targetPath)` を発火。
- **`MainViewModel.cs`**:
  - `ExportImagesViewModel? ExportImagesViewModel`
  - `bool IsExportImagesDialogVisible`
  - `ShowExportImagesDialogCommand`
    - 現在のドキュメント、選択状態、およびレンダリングデリゲート（`RenderPageWithInkAsync`）を渡して `ExportImagesViewModel` を初期化・表示。
  - `CloseExportImagesDialogCommand`

### 3. View (`PDFBinder.App`)
- **リボンツールバー (`MainWindow.xaml`)**:
  - 「ページ分割」ボタンの直後に「画像書き出し」ボタンを追加。
  - アイコン: `&#xEB9F;` (Picture)
  - テキスト: "画像保存"
  - ToolTip: "ページをJPEG/PNG画像として書き出し"
- **画像書き出しオーバーレイダイアログ (`MainWindow.xaml`)**:
  - `IsExportImagesDialogVisible` に連動するインアプリ・モーダル。
  - **左側（設定パネル）**:
    - 出力形式（フォーマット）: `PNG` / `JPEG`（`DarkToggleRadioButtonStyle`）
    - 解像度: `200 dpi` / `300 dpi` / `400 dpi` / `600 dpi`（`DarkToggleRadioButtonStyle`）
    - 書き出し範囲: `すべて` / `現在のページ` / `ページ指定` (+ 入力欄・エラーメッセージ)
  - **右側（プレビューパネル）**:
    - 白背景シャドウ付きページプレビュー画像
    - ページ送りナビゲーション（◀ 1 / 5 ▶）
  - **フッター**:
    - 進捗バー & ステータステキスト（書き出し中のみ表示）
    - 「書き出し」ボタン（プライマリスタイル）
    - 「キャンセル」ボタン（セカンダリスタイル）

### 4. 単体テスト (`PDFBinder.Tests`)
- **`ImageExportServiceTests.cs`**:
  - DPI計算ロジック（72dpiポイントから200/300/400/600dpiのピクセル変換）の検証
  - PNGおよびJPEGへの保存エンコード処理の正常系テスト
- **`ExportImagesViewModelTests.cs`**:
  - ページ範囲（すべて、現在のページ、カスタム指定、不正入力）の解析・検証
  - フォーマットやDPI変更時のプロパティ連動
  - コマンドの実行可否（`CanExport`）
  - ページナビゲーション（前へ/次へ）の挙動

---

## 実装ステップ
1. **コア層の実装**:
   - `ImageExportSettings.cs`、`IImageExportService.cs`、`ImageExportService.cs` の作成
   - 単体テスト `ImageExportServiceTests.cs` の作成・検証
2. **ViewModel層の実装**:
   - `ExportImagesViewModel.cs` の作成
   - 単体テスト `ExportImagesViewModelTests.cs` の作成・検証
   - `MainViewModel.cs` への `ExportImagesViewModel` 統合
3. **UI層の実装**:
   - `MainWindow.xaml` にリボンボタンおよびオーバーレイダイアログを追加
4. **統合テスト & ビルド確認**:
   - `dotnet test` でテスト全件成功を確認
   - `dotnet build` でビルド警告・エラーなしを確認
