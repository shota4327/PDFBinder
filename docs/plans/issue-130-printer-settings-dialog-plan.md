# 印刷プレビュー画面におけるプリンター印刷設定ボタンの追加 (Issue #130)

印刷プレビュー画面において、プリンター選択プルダウンの右横に「設定」ボタン（歯車アイコン）を追加し、表示されているプリンターのドライバー固有の「印刷設定」ダイアログ（Win32 `DocumentProperties`）を開いて、用紙サイズ・向き・部数・両面等の設定を双方向同期するとともに、ドライバー固有設定（カラー/モノクロ、給紙トレイ、画質等）を印刷実行時に引き継ぐ機能の実装計画です。

---

## ユーザー確認・合意事項

- **印刷設定ダイアログの表示と設定同期**:
  - プリンター選択コンボボックスの右横に歯車アイコンボタン（⚙ `\uE713`、ツールチップ「プリンターのプロパティ（印刷設定）」）を配置。
  - ボタン押下時に、選択中プリンターのドライバー固有の「印刷設定」ダイアログ（Win32 API `DocumentProperties`）をモーダル表示。
  - ダイアログで「OK」が押された場合、用紙サイズ・向き・部数・両面印刷などの設定値を読み取り、PDFBinder側のUI各項目およびプレビュー表示へ即座に自動反映。
- **ドライバー固有設定の保持と印刷実行時の適用**:
  - ダイアログで設定されたドライバー固有情報（DEVMODE / PrintTicket）を内部で保持し、印刷実行時（`WpfPrintService.PrintAsync`）にも引き継いで適用。
- **ボタンの活性状態制御**:
  - プリンター未選択時、プリンター不在時、または印刷処理中（`IsPrinting == true`）はボタンを非活性（Disabled）にする。
- **プリンター切り替え時の動作**:
  - プルダウンで別のプリンターに切り替えた場合は、直前のプリンターの固有設定（DEVMODE）をクリアし、新プリンターの既定設定をベースにする（用紙サイズや向きなど画面上の共通設定は維持）。

---

## 変更内容と構成

### 1. コアモデル・インターフェース層 (PDFBinder.Core)

#### [MODIFY] [IPrintService.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/IPrintService.cs)
- `PrinterSettingsDialogResult` レコードまたはクラスを追加（変更された用紙サイズ、向き、部数、両面設定、DEVMODEバイト配列等を保持）。
- `IPrintService` に `PrinterSettingsDialogResult? ShowPrinterSettingsDialog(string printerName, nint ownerHwnd, byte[]? currentDevMode = null);` を追加。

#### [MODIFY] [PrintSettings.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PrintSettings.cs)
- `byte[]? DriverDevMode { get; set; }` プロパティを追加（ドライバー固有設定バイナリの保持用）。

---

### 2. プラットフォーム印刷サービス層 (PDFBinder.App)

#### [MODIFY] [WpfPrintService.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Services/WpfPrintService.cs)
- Win32 API P/Invoke（`winspool.drv`）を実装:
  - `OpenPrinter`, `ClosePrinter`, `DocumentProperties`
- `ShowPrinterSettingsDialog(string printerName, nint ownerHwnd, byte[]? currentDevMode)` の実装:
  - 指定プリンターのハンドルをオープン。
  - 既存の `currentDevMode` があればバッファにロードし、`DM_IN_PROMPT | DM_IN_BUFFER | DM_OUT_BUFFER` でダイアログを表示。
  - OK（`IDOK == 1`）時に変更された `DEVMODE` 構造体を解析:
    - `dmPaperSize` (DMPAPER_A4 -> A4, DMPAPER_A3 -> A3)
    - `dmOrientation` (DMORIENT_PORTRAIT / DMORIENT_LANDSCAPE)
    - `dmCopies` (部数)
    - `dmDuplex` (DMDUP_SIMPLEX / DMDUP_VERTICAL / DMDUP_HORIZONTAL)
    - DEVMODE バッファを `byte[]` としてコピー。
  - `PrinterSettingsDialogResult` として返却。
- `ExecutePrintDialog`:
  - `settings.DriverDevMode` が存在する場合、`PrintTicketConverter`（`ReachFramework.dll` / `System.Printing`）を用いて DEVMODE から `PrintTicket` を初期化またはマージし、ドライバー固有設定（カラー/モノクロ、給紙トレイ、印刷品質等）を印刷チケットに反映。
  - その上でアプリ側の UI 設定（用紙サイズ・向き・部数・両面）を適用（`ConfigurePrintTicket`）して印刷ジョブを実行。

---

### 3. プレゼンテーション層 (PDFBinder.App)

#### [MODIFY] [PrintViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/PrintViewModel.cs)
- `OpenPrinterSettingsCommand` の追加（`[RelayCommand(CanExecute = nameof(CanOpenPrinterSettings))]`）。
- `CanOpenPrinterSettings`: `!IsPrinting && !string.IsNullOrEmpty(Settings.PrinterName)`。
- `Settings.PropertyChanged` のハンドラで `PrinterName` の変更を検知:
  - 前プリンターと異なる場合は `Settings.DriverDevMode = null` にリセット。
  - `OpenPrinterSettingsCommand.NotifyCanExecuteChanged()` を呼出。
- `OpenPrinterSettings`:
  - `Application.Current.MainWindow` からウィンドウハンドルを取得。
  - `_printService.ShowPrinterSettingsDialog(Settings.PrinterName, hwnd, Settings.DriverDevMode)` を呼出。
  - 結果が取得できたら、各設定値（PaperSize, Orientation, Copies, DuplexMode, DriverDevMode）を `Settings` に反映し、プレビューを再計算（`ValidateAndRefresh()`）。

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- プリンター選択コンボボックスの行を 3 列の Grid（ComboBox / 余白 / 設定ボタン）に変更。
- 歯車アイコン（`\uE713`）のコンパクトボタンを配置。
- `ToolTip="プリンターのプロパティ（印刷設定）"` を設定。
- `Command="{Binding OpenPrinterSettingsCommand}"` にバインド。

---

### 4. 単体テスト層 (PDFBinder.Tests)

#### [MODIFY] [PrintViewModelTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PrintViewModelTests.cs)
- `OpenPrinterSettingsCommand` の実行可否テスト（プリンター未設定時・印刷実行中は無効、プリンター設定時は有効）。
- プリンター切り替え時に `DriverDevMode` がリセットされることのテスト。
- 設定ダイアログで返された設定（PaperSize, Orientation, Copies, DuplexMode）が `Settings` に正しく反映され、プレビュー更新がトリガーされることのテスト。

---

## 検証手順

### 1. 自動テスト
- `dotnet test` を実行し、既存テストおよび新規追加テストが全件 PASS することを確認。

### 2. ビルド検証
- `dotnet build` を実行し、警告およびエラーなくビルドが成功することを確認。

### 3. 手動動作検証
- アプリを起動し、PDFを開いて「印刷」ダイアログを表示。
- プリンタープルダウン横に歯車アイコンボタンが表示されていることを確認。
- 歯車ボタンをクリックし、選択中プリンターのドライバー固有の「印刷設定」ダイアログがモーダル表示されることを確認。
- 印刷設定ダイアログで用紙の向き（横）や用紙サイズ（A3）を変更して「OK」をクリックした際、PDFBinder側のUIおよびプレビューが即座に同期されることを確認。
- 別のプリンターを選択した際、ボタンの活性状態や設定リセットが適切に動作することを確認。
