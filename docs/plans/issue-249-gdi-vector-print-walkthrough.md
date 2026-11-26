# Issue #249 印刷の大幅高速化（Win32 GDI + PDFium ハイブリッド印刷）検証報告（Walkthrough）

## 1. 概要
- **対象 Issue**: #249（印刷の大幅高速化：Win32 GDI + PDFium ベクター印刷と画像印刷のハイブリッド化）
- **作業ブランチ**: `issue-249-gdi-vector-print`
- **バージョン**: `0.17.4` → `0.18.0`（マイナーバージョンインクリメント）

印刷実行後にプレビュー画面が閉じた後、ステータスバーに「プリンターへ送信中...」が表示されたまま長時間待機する問題に対し、WPF/XPS パイプラインを廃止して **Win32 GDI + PDFium 直接ベクター印刷（標準）** と **Win32 GDI 直接 24bpp 非透過ラスター印刷（互換・インク付きページ）** のハイブリッド構成へ刷新しました。また、印刷実行時の事前レンダリング一括構築を撤廃し、**即時プレビュークローズ ＋ 1シート単位ストリーミング送信 ＋ リアルタイム進捗表示** を実装しました。

---

## 2. 主な変更内容

### 2.1 Win32 GDI + PDFium ハイブリッド印刷エンジン
1. **通常 PDF ページの直接ベクター描画（`GdiPrintService` / `PdfiumNativePrint`）**:
   - 同梱済み `pdfium.dll` の `FPDF_LoadMemDocument`（ピン留めメモリ＋ジョブ内キャッシュによる Unicode パス対応・ファイルロック回避）および `FPDF_RenderPage`（`FPDF_PRINTMODE_EMF` / `FPDF_ANNOT | FPDF_PRINTING`）を使用し、プリンターデバイスコンテキスト（`HDC`）へ直接ベクター出力します。
   - 従来の「全ページ 300 DPI `Bgra32` 画像化 → XPS シリアライズ → XPS→GDI 変換」を完全にバイパスすることで、文字・図面の鮮明さ（プリンターネイティブ解像度）と他アプリ同等の高速送信を実現しました。
   - すべての PDFium 呼び出しは `IPdfRenderer.AcquirePdfiumLockAsync` により既存の `PriorityAsyncLock` と排他制御を共有し、バックグラウンド印刷中の UI サムネイル・詳細描画と安全に共存します。
2. **24bpp 透過なし GDI ラスター描画＆自動フォールバック（`GdiBitmapConverter`）**:
   - 「画像として印刷」が有効な場合、画像ページ（JPG/PNG）、手書きインク付きページ（白紙上のインク含む）、およびベクター描画でエラーが発生した場合の自動フォールバック先として、300 DPI で描画・合成した画像を白背景合成済みの **24bpp 非透過 BGR（`BI_RGB`）** へ変換し、`StretchDIBits`（`HALFTONE` 補間）で直接送信します。
   - アルファチャンネルを排除することで、プリンタードライバー側での透過合成起因の 600 DPI 再ラスタライズによる膨張・遅延を防止しつつ、WPF 蛍光ペンの色味・見た目を 100% 再現します。
3. **DEVMODE 直接構築と部数フォールバック（`PrinterDevModeHelper`）**:
   - `BuildEffectiveDevMode` / `PatchDevModeBytes` により、保持中のドライバー固有 `DEVMODE`（またはプリンター既定値）へ用紙サイズ（A4/A3、`DM_PAPERWIDTH`/`DM_PAPERLENGTH` クリア含む）・向き・部数・両面設定を統合して `CreateDCW` へ直接渡します。ドライバーが複数部数を受理しない場合は自動でソフトウェア部数ループへフォールバックします。
4. **物理用紙全体基準の配置とクリッピング**:
   - `GetDeviceCaps`（`PHYSICALWIDTH`, `PHYSICALHEIGHT`, `PHYSICALOFFSETX`, `PHYSICALOFFSETY`, `LOGPIXELSX`, `LOGPIXELSY`）から物理用紙全体を基準にスロット矩形および非正方 DPI 対応のアスペクト比維持描画矩形を算出し、従来と同等の倍率・配置を維持しています。N-Up・冊子時は `SaveDC` / `IntersectClipRect` / `RestoreDC` によりスロット外へのはみ出しを防止します。

### 2.2 即時クローズ ＋ 1シート単位ストリーミング送信と進捗表示
- **不変スナップショット＆即時クローズ（`PrintPageSource` / `PrintViewModel`）**:
  - 印刷ボタン押下時、全ページの事前ビットマップ化を行わず、`InkStrokes.Clone()` を含む不変スナップショット（`PrintPageSource`）を生成して即座に印刷ダイアログを閉じます。
- **リアルタイムシート進捗通知（`BackgroundPrintQueueService` / `MainViewModel`）**:
  - `DirectProgress<T>` によるスレッドプール遅延のない同期進捗配信（`JobProgressChanged`）により、メイン画面ステータスバーおよび終了待機オーバーレイに `プリンターへ送信中... (3 / 10)`（複数待機時は残り件数併記）をリアルタイム表示します。

### 2.3 UI 拡張（`MainWindow.xaml` / `PrintSettings`）
- 印刷設定パネル最下部（印刷モードの下）に「画像として印刷（文字化け・崩れ対策）」チェックボックスを追加しました（初期値 OFF、アプリ起動中は他設定と同様に直前状態を保持）。

---

## 3. 変更・新規作成ファイル一覧

| ファイルパス | 変更種別 | 内容 |
| :--- | :--- | :--- |
| `src/PDFBinder.Core/Models/PrintSettings.cs` | 変更 | `PrintAsImage` プロパティおよび `ResetToDefault` / `CopyFrom` 対応を追加 |
| `src/PDFBinder.Core/Models/PrintPageSource.cs` | 新規作成 | 印刷実行時点のページ不変スナップショット（クローン済みストローク保持） |
| `src/PDFBinder.Core/Services/IPdfRenderer.cs` | 変更 | `AcquirePdfiumLockAsync`, `GetCleanPdfBytesIfHasMarker` インターフェース追加 |
| `src/PDFBinder.Core/Services/PdfiumRenderer.cs` | 変更 | PDFium ロック共有およびパスワード対応クリーンバイト列キャッシュ取得を実装 |
| `src/PDFBinder.Core/Services/IPrintService.cs` | 変更 | `PrintStreamingAsync` メソッドを追加 |
| `src/PDFBinder.App/Services/GdiNativeMethods.cs` | 新規作成 | `gdi32.dll` Win32 GDI 印刷 P/Invoke 宣言・定数・構造体 |
| `src/PDFBinder.App/Services/PdfiumNativePrint.cs` | 新規作成 | `pdfium.dll` 印刷用 P/Invoke、DLL リゾルバ、ジョブ内ドキュメントキャッシュ |
| `src/PDFBinder.App/Services/GdiBitmapConverter.cs` | 新規作成 | 物理用紙基準スロット・フィット計算、白背景合成 24bpp BGR 変換 |
| `src/PDFBinder.App/Services/PrinterDevModeHelper.cs` | 変更 | `BuildEffectiveDevMode` / `PatchDevModeBytes` による直接 `DEVMODE` 構築・統合 |
| `src/PDFBinder.App/Services/GdiPrintService.cs` | 新規作成 | Win32 GDI + PDFium ハイブリッド印刷および自動フォールバック実装 |
| `src/PDFBinder.App/Services/WpfPrintService.cs` | 変更 | `PrintStreamingAsync` 実装を追加 |
| `src/PDFBinder.App/Services/BackgroundPrintQueueService.cs` | 変更 | ストリーミング印刷ジョブ対応および `JobProgressChanged`（同期 `DirectProgress`）追加 |
| `src/PDFBinder.App/ViewModels/PrintViewModel.cs` | 変更 | スナップショット方式による即時ダイアログクローズとストリーミング要求発火 |
| `src/PDFBinder.App/ViewModels/MainViewModel.cs` | 変更 | `GdiPrintService` 標準採用、スナップショット生成、シート進捗表示連携 |
| `src/PDFBinder.App/MainWindow.xaml` | 変更 | 「画像として印刷（文字化け・崩れ対策）」チェックボックスを追加 |
| `tests/PDFBinder.Tests/GdiBitmapConverterTests.cs` | 新規作成 | 物理用紙基準計算・非正方DPI・白背景合成24bpp変換の単体テスト |
| `tests/PDFBinder.Tests/GdiPrintServiceTests.cs` | 新規作成 | 描画種別判定・自動フォールバック・`PrintAsImage`・スナップショット隔離の単体テスト |
| `tests/PDFBinder.Tests/PrinterDevModeHelperTests.cs` | 変更 | `PatchDevModeBytes` の単体テストを追加 |
| `tests/PDFBinder.Tests/PrintViewModelTests.cs` | 変更 | ストリーミング即時クローズ・`PrintAsImage` 伝達の単体テストを追加 |
| `tests/PDFBinder.Tests/BackgroundPrintQueueServiceTests.cs` | 変更 | ストリーミング実行・シート進捗通知の単体テストを追加 |
| `tests/PDFBinder.Tests/MainViewModelPrintExitTests.cs` | 変更 | ステータスバーおよび待機ダイアログ進捗更新の単体テストを追加 |
| `docs/basic_design.md` | 変更 | Win32 GDI + PDFium ハイブリッド印刷・ストリーミング送信仕様を反映 |
| `docs/PROJECT.md` | 変更 | 機能インベントリ `F86`（完了）を追加 |
| `README.md` | 変更 | ハイブリッド印刷・ストリーミング進捗仕様の説明を更新 |
| `Directory.Build.props` | 変更 | バージョンを `0.18.0` へインクリメント |
| `CHANGELOG.md` | 変更 | `0.18.0` リリースノートを追加 |

---

## 4. 検証結果

### 4.1 ビルド検証 (`dotnet build`)
```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```

### 4.2 単体テスト検証 (`dotnet test --no-build`)
```text
C:\Git\PDFBinder\tests\PDFBinder.Tests\bin\Debug\net10.0-windows\PDFBinder.Tests.dll (.NETCoreApp,Version=v10.0) のテスト実行
合計 1 個のテスト ファイルが指定されたパターンと一致しました。

成功!   -失敗:     0、合格:   762、スキップ:     0、合計:   762、期間: 12 s - PDFBinder.Tests.dll (net10.0)
```
- 新規追加および既存の全 762 件の単体テストが 100% PASS（失敗 0、スキップ 0）。
