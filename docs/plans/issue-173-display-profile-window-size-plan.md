# 実装計画: 外部モニター接続環境に応じたウィンドウサイズの個別記憶・復元

## 1. 概要 (Overview)
- **Issue**: [#173 外部モニター接続環境に応じたウィンドウサイズの個別記憶・復元](https://github.com/shota4327/PDFBinder/issues/173)
- **対象ブランチ**: `feature/issue-173-display-profile-window-size`
- **目的**: ノートPC単体（本体ディスプレイのみ）利用時と、外部モニター接続時（外部モニターのみ表示、あるいはマルチモニター環境）で、それぞれの画面環境に応じたウィンドウサイズ（幅・高さ・最大化状態）を個別に記憶・自動復元できるようにする。

---

## 2. 背景と課題 (Background & Issues)
- 現行の PDF Binder では、アプリケーション終了時のウィンドウサイズと最大化状態が `AppSettings.Window`（単一の `WindowSettings` オブジェクト）に保存される。
- ノートPC単体（例: 1920x1080）と、外付け外部モニター（例: 2560x1440 や 3840x2160 等）では快適な作業ウィンドウサイズや画面レイアウトが大きく異なる。
- 外部モニターで全画面または大画面で作業した後に本体画面で開くと画面からはみ出し（境界調整されるものの小さく窮屈になる）、逆に本体画面サイズで終了後に外部モニターで開くと極端に小さく表示されるなど、接続環境を切り替えるたびにウィンドウのリサイズや再配置が必要になっていた。

---

## 3. 設計方針とアーキテクチャ (Design & Architecture)

### 3.1 ディスプレイ構成の識別（プロファイルキー生成）
- Windows のネイティブディスプレイ情報（Win32 `EnumDisplayMonitors`、`GetMonitorInfo`、`EnumDisplayDevices`）を活用し、アクティブな画面構成から一意かつ可読性の高いプロファイルキー（`ProfileKey`）を生成する。
- **キーの構成要素**:
  - モニター識別名（Friendly Name: 例 `T27h-30`、またはデバイスID・製造元コード）
  - モニター解像度（例: `2560x1440`）
  - 画面構成（単一画面 `1mon` / 複数画面 `2mon` 等）
  - 生成キー例: `T27h-30_2560x1440_1mon`, `Internal_1920x1080_1mon`, `T27h-30_2560x1440_2mon`
- Win32 API 取得に失敗した場合やテスト実行時のフォールバックとして、WPF の `SystemParameters`（解像度・作業領域）を基にしたキー生成を行う。

### 3.2 設定データモデルの拡張 (`AppSettings.cs`)
既存設定との完全な下位互換性を保持するため、`AppSettings` にディスプレイスペック別のマップを追加する。
```csharp
public class AppSettings
{
    /// <summary>
    /// 既定またはフォールバックのウィンドウ表示設定（下位互換性維持）
    /// </summary>
    public WindowSettings Window { get; set; } = new();

    /// <summary>
    /// ディスプレイ構成プロファイルごとの個別ウィンドウ設定（キー: プロファイル識別子）
    /// </summary>
    public Dictionary<string, WindowSettings> DisplayProfiles { get; set; } = new();
}
```
- 既存の `settings.json`（`Window` のみ存在）を読み込んだ場合でも、空の辞書として安全に初期化されエラーにならない。
- 保存時は、最新のウィンドウ状態を `Window`（共通フォールバック用）と `DisplayProfiles[currentProfileKey]` の双方に更新・書き出しを行う。

### 3.3 サービス層の新設 (`IDisplayProfileService` / `DisplayProfileService`)
- 単一責任の原則（SRP）に従い、ディスプレイ検出・プロファイルキー生成および設定解決ロジックを `PDFBinder.App.Services.DisplayProfileService` として独立させる。
- インターフェース `IDisplayProfileService` を定義し、単体テストで任意のモニター環境をモック・シミュレート可能にする。
```csharp
public interface IDisplayProfileService
{
    /// <summary>
    /// 現在のアクティブなディスプレイ環境を一意に表すプロファイルキーを取得します。
    /// </summary>
    string GetCurrentProfileKey();

    /// <summary>
    /// 設定情報から、現在のディスプレイプロファイルに対応する最適なウィンドウ設定を解決します。
    /// </summary>
    WindowSettings ResolveEffectiveWindowSettings(AppSettings settings);
}
```

### 3.4 ウィンドウ復元および保存フローの改修 (`MainWindow.xaml.cs`)
- **起動時 (`RestoreWindowSettings`)**:
  1. `_settingsService.Load()` で `AppSettings` をロード。
  2. `_displayProfileService.ResolveEffectiveWindowSettings(settings)` を呼び出し、現在のプロファイルに対応する `WindowSettings` を取得（該当プロファイルが未保存の場合は `settings.Window` または既定値へフォールバック）。
  3. `WindowBoundsHelper.AdjustBounds` を通して、現在の作業領域に収まるように安全に補正してウィンドウへ適用。
- **終了時 (`SaveWindowSettings`)**:
  1. 現在のウィンドウ状態（幅、高さ、最大化フラグ）を取得。
  2. `_displayProfileService.GetCurrentProfileKey()` で現在のプロファイルキーを取得。
  3. `settings.DisplayProfiles[profileKey]` に現在の設定を格納。
  4. 同時に `settings.Window` も更新（共通フォールバック）。
  5. `_settingsService.Save(settings)` を実行。

---

## 4. 変更対象ファイル一覧 (Affected Files)

1. **`src/PDFBinder.Core/Models/AppSettings.cs`**:
   - `DisplayProfiles` プロパティ（`Dictionary<string, WindowSettings>`）を追加。
2. **`src/PDFBinder.App/Services/IDisplayProfileService.cs` (新規)**:
   - ディスプレイプロファイルサービスのインターフェース定義。
3. **`src/PDFBinder.App/Services/DisplayProfileService.cs` (新規)**:
   - Win32 API によるモニター名・解像度・画面数取得、およびプロファイルキー生成と設定解決の実装。
4. **`src/PDFBinder.App/MainWindow.xaml.cs`**:
   - `_displayProfileService` の導入。
   - `RestoreWindowSettings()` および `SaveWindowSettings()` の呼び出し先更新。
5. **`tests/PDFBinder.Tests/DisplayProfileServiceTests.cs` (新規)**:
   - プロファイルキー生成、該当キーの存在時/未存在時のフォールバック解決、境界補正の検証。
6. **`tests/PDFBinder.Tests/SettingsServiceTests.cs`**:
   - `DisplayProfiles` の永続化・復元および下位互換性の単体テスト追加。
7. **ドキュメント更新**:
   - `docs/basic_design.md`: 設定モデルとマルチディスプレイ対応仕様の追記。
   - `CHANGELOG.md`: 本改善内容の記載（PR準備段階）。

---

## 5. テスト・検証計画 (Verification Plan)

### 5.1 自動テスト (Automated Tests)
- `dotnet test`: 既存の全単体テスト（100% PASS）および新規追加テストの全件パス確認。
  - プロファイルが存在する場合にそのサイズ・最大化が正確に復元されること。
  - プロファイルが存在しない場合に `Window`（共通設定）またはデフォルト値へ安全にフォールバックすること。
  - 画面作業領域を超える不正なサイズ値が保存されていても、`WindowBoundsHelper` により安全な範囲にクリップされること。
  - `AppSettings` の JSON シリアライズ/デシリアライズでプロファイル辞書が正常に保持されること。

### 5.2 手動検証 (Manual Verification)
- 外部モニター接続環境（2560x1440）でアプリを起動し、特定のサイズ・位置で終了する。
- 再度起動し、同じサイズで復元されることを確認。
- （シミュレーション・設定ファイル検証）`settings.json` を確認し、プロファイルキー（`DisplayProfiles`）配下にキーとサイズが保存されていることを目視確認する。
