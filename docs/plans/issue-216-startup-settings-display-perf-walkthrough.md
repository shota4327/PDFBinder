# Issue #216 検証報告 (Walkthrough): MainWindow起動時の同期ファイルI/O・モニター列挙および非コンパイル正規表現の改善

## 1. 実装内容の概要
`MainWindow` 起動時および終了時において発生していた以下のオーバーヘッドを解消し、ウィンドウ表示の高速化とリソース管理の改善を実施しました。

1. **`DisplayProfileService` の `[GeneratedRegex]` 導入とプロファイルキーキャッシュ化**:
   - `DisplayProfileService` を `partial class` 化し、`SanitizeIdentifier` で毎回実行されていた `Regex.Replace` をコンパイル時生成の `[GeneratedRegex]` メソッド（`InvalidCharsRegex()` / `ConsecutiveUnderscoresRegex()`）に置き換え、ランタイムのパターン解析コストを排除しました。
   - `GetCurrentProfileKey()` の結果をメモリ上にキャッシュし、`Microsoft.Win32.SystemEvents.DisplaySettingsChanged` イベントが発生するまで Win32 API（`EnumDisplayMonitors` / `GetMonitorInfo` / `EnumDisplayDevices`）の再走査を回避する仕組みを導入しました。
   - 静的イベント購読によるメモリリークを防止するため、`IDisposable` を実装し、`MainWindow.OnClosed` 時に確実に購読解除されるようにしました。
2. **`SettingsService` のメモリキャッシュ化と JSON Source Generator 導入**:
   - `AppSettingsJsonContext`（`[JsonSerializable(typeof(AppSettings))]`）を導入し、リフレクションフリーな高速シリアライズ／デシリアライズに置き換えました。
   - `SettingsService` にメモリキャッシュ（`_cachedSettings`）を導入し、起動時に一度読み込んだ設定インスタンスを終了時（`SaveWindowSettings`）などでもディスク再読み込みなしで即座に再利用できるようにしました。
3. **ドキュメント・バージョンの更新**:
   - `Directory.Build.props` のバージョンを `0.13.4` → `0.13.5` へインクリメントしました。
   - `CHANGELOG.md` に `0.13.5` の変更履歴を追記し、`docs/basic_design.md` の仕様記述を更新しました。

---

## 2. 変更ファイル一覧
- `src/PDFBinder.App/Services/DisplayProfileService.cs`
- `src/PDFBinder.Core/Services/SettingsService.cs`
- `src/PDFBinder.App/MainWindow.xaml.cs`
- `tests/PDFBinder.Tests/SettingsServiceTests.cs`
- `tests/PDFBinder.Tests/DisplayProfileServiceTests.cs`
- `Directory.Build.props`
- `CHANGELOG.md`
- `docs/basic_design.md`
- `docs/plans/issue-216-startup-settings-display-perf-plan.md`
- `docs/plans/issue-216-startup-settings-display-perf-walkthrough.md`

---

## 3. ビルドおよび単体テスト検証結果

### 3.1 ビルド検証 (`dotnet build`)
- 警告 `0`、エラー `0` で正常にビルド完了することを確認しました。

### 3.2 単体テスト検証 (`dotnet test`)
- 新規追加したキャッシュ検証テスト（`Load_WhenCalledMultipleTimes_ReturnsCachedSettingsWithoutReReadingDisk`、`GetCurrentProfileKey_CachesResultAndAvoidsRepeatedMonitorDetection`）を含め、全 **648 件** の単体テストが 100% PASS することを確認しました。

```text
成功!   -失敗:     0、合格:   648、スキップ:     0、合計:   648、期間: 8 s - PDFBinder.Tests.dll (net10.0)
```
