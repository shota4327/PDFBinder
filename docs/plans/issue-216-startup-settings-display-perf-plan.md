# Issue #216 実装計画: MainWindow起動時の同期ファイルI/O・モニター列挙および非コンパイル正規表現の改善

## 1. 概要・目的
`MainWindow` 起動時および終了時において発生していた以下のボトルネックを解消し、ウィンドウ初期化〜表示までのUIスレッド負荷を軽減します。

1. **非コンパイル正規表現の都度解析コスト**: `DisplayProfileService.SanitizeIdentifier` での `Regex.Replace` 静的呼び出しを `.NET` ソースジェネレーター（`[GeneratedRegex]`）に置き換え、ランタイムでの正規表現パターン解析を排除する。
2. **設定ファイル（`settings.json`）の重複ファイルI/O・JSONリフレクションコスト**: `SettingsService` にメモリキャッシュおよび `System.Text.Json` Source Generator（`JsonSerializerContext`）を導入し、起動時に読み込んだ `AppSettings` をメモリ上で再利用することで終了時等の不要なディスク再読み込みとリフレクションオーバーヘッドを削減する。
3. **ディスプレイ構成検出（Win32 API走査）の重複実行**: `DisplayProfileService` にプロファイルキーのメモリキャッシュを導入し、ディスプレイ設定変更（`SystemEvents.DisplaySettingsChanged`）が発生しない限り、Win32 API（`EnumDisplayMonitors` / `GetMonitorInfo` / `EnumDisplayDevices`）の再走査を回避する。

---

## 2. 現状の課題と原因
1. **UIスレッドでの同期ファイルI/OとJSONデシリアライズ**:
   - `MainWindow` コンストラクタ内の `RestoreWindowSettings()` が `_settingsService.Load()` を呼び出して `settings.json` を読み込み・デシリアライズしている。
   - さらにウィンドウ終了時（`SaveWindowSettings()`）にも再度 `_settingsService.Load()` でディスクからファイルを読み直しており、不要なディスクI/Oが発生している。
   - `JsonSerializer` がランタイムリフレクションベースで動作しており、初回シリアライズ／デシリアライズ時にメタデータ構築コストが発生している。
2. **非コンパイル正規表現の都度実行**:
   - `DisplayProfileService.SanitizeIdentifier` において `Regex.Replace(raw.Trim(), @"[^a-zA-Z0-9_\-]", "_")` および `Regex.Replace(sanitized, @"_+", "_")` が静的メソッドとして使用されており、呼び出しごとにパターン解析が走っている。
3. **ディスプレイ構成検出の重複呼び出し**:
   - 起動時（`RestoreWindowSettings` → `ResolveEffectiveWindowSettings`）と終了時（`SaveWindowSettings` → `GetCurrentProfileKey`）の双方で毎回 `DetectDisplayMonitors()` が呼ばれ、`EnumDisplayMonitors` / `GetMonitorInfo` / `EnumDisplayDevices` のWin32 API群が重複実行されている。

---

## 3. 実装方針・設計

### 3.1 `DisplayProfileService` の `[GeneratedRegex]` 化とプロファイルキーのキャッシュ化
- **対象ファイル**: `src/PDFBinder.App/Services/DisplayProfileService.cs`
- **変更内容**:
  1. `DisplayProfileService` を `public partial class DisplayProfileService : IDisplayProfileService, IDisposable` に変更する。
  2. `SanitizeIdentifier` で使用する2つの正規表現を `[GeneratedRegex]` 属性を用いたソースジェネレーターメソッドとして定義する:
     - `[GeneratedRegex(@"[^a-zA-Z0-9_\-]")] private static partial Regex InvalidCharsRegex();`
     - `[GeneratedRegex(@"_+")] private static partial Regex ConsecutiveUnderscoresRegex();`
  3. プロファイルキーのキャッシュ機構を導入する:
     - `_cachedProfileKey` フィールドと排他ロック（`object _cacheLock`）を保持し、`GetCurrentProfileKey()` 初回呼び出し時に計算したプロファイルキーをキャッシュする。
     - `InvalidateCache()` メソッドを提供し、`Microsoft.Win32.SystemEvents.DisplaySettingsChanged` イベント発生時にキャッシュをクリア（無効化）する。
     - イベント購読によるメモリリークを防止するため、`IDisposable` を実装して `SystemEvents.DisplaySettingsChanged` の購読解除を確実に行う（また静的イベントからの強参照リークを避けるようハンドラ管理を徹底する）。

### 3.2 `SettingsService` のメモリキャッシュ化および JSON Source Generator 導入
- **対象ファイル**: `src/PDFBinder.Core/Services/SettingsService.cs`
- **変更内容**:
  1. `AppSettingsJsonContext`（`[JsonSerializable(typeof(AppSettings))]` を付与した ` partial class AppSettingsJsonContext : JsonSerializerContext`）を定義し、リフレクションフリーな高速シリアライズ／デシリアライズを実現する。
  2. `SettingsService` 内に `_cachedSettings`（`AppSettings?`）およびロックオブジェクトを保持する:
     - `Load()` 呼び出し時、既に `_cachedSettings` が存在する場合はディスクI/Oを行わずキャッシュ済みインスタンス（のクローンまたは保持インスタンス）を即座に返却する。
     - `Save(AppSettings settings)` 呼び出し時、`_cachedSettings` を最新状態に更新した上でファイルへ書き出す。
     - 必要に応じてキャッシュを明示的にクリアまたは強制再読み込みできる内部機構を備え、既存の単体テストおよびマルチプロファイル保存仕様との完全な互換性を維持する。

### 3.3 `MainWindow` における設定保持と `DisplayProfileService` の適切なリソース解放
- **対象ファイル**: `src/PDFBinder.App/MainWindow.xaml.cs`
- **変更内容**:
  1. `RestoreWindowSettings()` で読み込んだ `AppSettings`（およびキャッシュされた `_settingsService.Load()`）を活用し、`SaveWindowSettings()` 実行時の不要なディスク再読み込みを排除する。
  2. `OnClosed` 時に `_displayProfileService` が `IDisposable` を実装している場合は `Dispose()` を呼び出し、`SystemEvents.DisplaySettingsChanged` のイベント購読を確実に解除してメモリリークを防止する。

---

## 4. 変更対象ファイル
1. `src/PDFBinder.App/Services/DisplayProfileService.cs`
   - `partial class` 化、`[GeneratedRegex]` の導入、プロファイルキーキャッシュと `SystemEvents.DisplaySettingsChanged` 連動、`IDisposable` 実装
2. `src/PDFBinder.Core/Services/SettingsService.cs`
   - メモリキャッシュ機構の追加、`AppSettingsJsonContext`（JSON Source Generator）の導入
3. `src/PDFBinder.App/MainWindow.xaml.cs`
   - 終了時の `_displayProfileService` 解放処理（`IDisposable` 対応）および設定アクセスの最適化
4. `tests/PDFBinder.Tests/SettingsServiceTests.cs`
   - キャッシュ動作（2回目以降の `Load()` でディスクI/Oが発生しないこと、`Save()` 時にキャッシュが更新されること）の単体テスト追加
5. `tests/PDFBinder.Tests/DisplayProfileServiceTests.cs`
   - `GetCurrentProfileKey()` のキャッシュ動作（モニター検出デリゲートが初回のみ呼ばれ、2回目はキャッシュから返ること、`InvalidateCache()` 後に再検出されること）および `[GeneratedRegex]` による `SanitizeIdentifier` の単体テスト追加
6. `Directory.Build.props` / `CHANGELOG.md` / `docs/basic_design.md`
   - パッチバージョン更新（`0.13.4` → `0.13.5`）およびドキュメント同期

---

## 5. テスト・検証計画
1. **単体テストの追加・実行**:
   - `SettingsServiceTests`: メモリキャッシュ有効時の動作検証（ファイル削除後もキャッシュから返ること、`Save` でキャッシュが更新されること）
   - `DisplayProfileServiceTests`: モニタープロバイダー呼び出し回数の検証（キャッシュヒット時は1回のみ呼ばれること、`InvalidateCache` で再取得されること）、正規表現サニタイズの境界値テスト
   - `MainWindowInitializationTests`: 既存のウィンドウサイズ・ディスプレイプロファイル復元・保存テストがすべてパスすること
2. **全体テスト・ビルド確認**:
   - `dotnet test` で全テストが 100% PASS すること
   - `dotnet build` で警告・エラーが 0 件であること
