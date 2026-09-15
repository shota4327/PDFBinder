# Issue #53 前回終了時のウィンドウサイズを復元する 実装計画

## 1. 概要
前回アプリ終了時のウィンドウサイズ（幅・高さ）および最大化状態を記憶し、次回起動時にその状態を復元します。
また、保存されたサイズがディスプレイ解像度・作業領域よりも大きい場合は画面内（作業領域）におさまるように自動調整します。

---

## 2. 決定した仕様・方針（/grill-me ヒアリング結果）
1. **復元対象のウィンドウ状態**:
   - ウィンドウサイズ（幅・高さ）および最大化状態（通常/最大化）を復元。
   - 表示位置は画面中央（`WindowStartupLocation = CenterScreen`）を維持。
   - 最大化状態で終了した場合は、通常復元時のサイズ（`RestoreBounds`）と最大化フラグを保存し、次回起動時に最大化で表示され、ユーザーが元に戻した際も前回のサイズを維持する。
   - 最小化状態で終了した場合は、次回通常ウィンドウとして復元されるよう安全にフォールバック。
2. **設定ファイルの保存先・フォーマット**:
   - 実行ファイル（EXE）と同階層の `settings.json`（完全ポータブル優先設計）。
   - 将来の設定拡張を見据えて `AppSettings` クラス配下に `WindowSettings` を配置する拡張可能 JSON 構造。
   - 読み込み・書き込み時の権限不足や JSON 破損等に対する堅牢な例外ハンドリング（アプリがクラッシュしないフォールバック設計）。
3. **画面サイズ調整（境界判定）**:
   - タスクバーを除いた作業領域（`SystemParameters.WorkArea`）を上限とし、ウィンドウの最小サイズ（`MinWidth=750, MinHeight=500`）を下限として調整。
   - 画面サイズ調整ロジックは WPF ウィンドウや UI スレッドに依存しない純粋ロジック（`WindowBoundsHelper`）として分離し、100% 単体テストで検証可能とする。

---

## 3. アーキテクチャおよび変更予定コンポーネント

### 3.1 `PDFBinder.Core`
- **[NEW] `Models/AppSettings.cs`**:
  - `AppSettings` クラス: 全体設定コンテナ（将来の設定追加に対応）。
  - `WindowSettings` クラス: `Width` (double), `Height` (double), `IsMaximized` (bool) などのウィンドウ状態プロパティ。
- **[NEW] `Services/ISettingsService.cs`**:
  - 設定の読み込み・保存を規定するインターフェース。
- **[NEW] `Services/SettingsService.cs`**:
  - `settings.json` の非同期/同期読み書き、ディレクトリ存在確認、破損時のデフォルト値フォールバック処理。

### 3.2 `PDFBinder.App`
- **[NEW] `Helpers/WindowBoundsHelper.cs`**:
  - `AdjustBounds(double savedWidth, double savedHeight, double maxWidth, double maxHeight, double minWidth, double minHeight)`
  - 画面作業領域内にサイズをおさめ、最小サイズを下回らないように計算する純粋関数。
- **[MODIFY] `MainWindow.xaml.cs`**:
  - ウィンドウ初期化時（コンストラクタまたは `SourceInitialized`）に `SettingsService` から設定をロードし、`WindowBoundsHelper` で調整した上で `Width`, `Height`, `WindowState` を適用。
  - ウィンドウ終了時（`OnClosing` または `Closed`）に現在のサイズ（通常時: ActualWidth/ActualHeight、最大化/最小化時: RestoreBounds）および `WindowState` を `SettingsService` 経由で保存。

### 3.3 `PDFBinder.Tests`
- **[NEW] `SettingsServiceTests.cs`**:
  - 設定の保存と読み込みの往復テスト。
  - ファイルが存在しない場合のデフォルト値返却テスト。
  - 不正な JSON や空ファイルの場合のエラー回復テスト。
- **[NEW] `WindowBoundsHelperTests.cs`**:
  - 通常サイズがそのまま維持されるテスト。
  - 作業領域を超えるサイズが画面内に縮小調整されるテスト（Issue #53 要件）。
  - 最小サイズ（750x500）を下回る場合に最小サイズへ補正されるテスト。
  - 作業領域自体が最小サイズより小さい極小解像度環境でのエッジケーステスト。
- **[MODIFY] `MainWindowInitializationTests.cs`**:
  - 設定復元処理を含む MainWindow の初期化および破棄が正常に行われるテスト。

---

## 4. 検証計画

### 4.1 自動テスト
```powershell
dotnet test
```
- 新規追加する `SettingsServiceTests`, `WindowBoundsHelperTests` を含む全テストが 100% パスすることを確認。

### 4.2 手動検証
1. **通常リサイズ＆再起動**:
   - ウィンドウを任意のサイズ（例: 1200x800）に変更してアプリ終了。
   - 再起動時に 1200x800 で画面中央に復元されることを確認。
2. **最大化＆元に戻す検証**:
   - ウィンドウを最大化してアプリ終了。
   - 再起動時に最大化状態で起動し、「元に戻す」ボタンを押すと最大化前のサイズに戻ることを確認。
3. **画面解像度超過の調整検証**:
   - `settings.json` に作業領域を超える極大サイズ（例: 99999x99999）を直接書き込んで起動。
   - 作業領域内（画面内）におさまるサイズに調整されて起動することを確認。
4. **設定ファイルが存在しない初期状態**:
   - `settings.json` がない状態でデフォルトサイズ（1100x760）で正常起動することを確認。
