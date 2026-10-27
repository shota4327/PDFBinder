# 実装計画: Issue #236 アクティブウィンドウへのOSアクセントカラー連動縁取りの追加

## 1. 概要
Windows アプリケーションの標準的な動作として、ウィンドウがアクティブな状態の時にウィンドウ外周に Windows のシステムアクセントカラー（OS設定色）に連動した縁取り線（1px Border）を表示します。  
非アクティブ時には目立たない境界線（`SurfaceBorderBrush`）に切り替え、最大化時には画面端への食い込みや不要な枠線描画を防ぐため枠線幅を 0px に自動調整します。

---

## 2. 背景と設計方針

### 2.1 現状の課題
- PDF Binder ではカスタムタイトルバーおよびタブバー統合のため `WindowChrome`（`GlassFrameThickness="0"`）を採用。
- ウィンドウ最外層に枠線（Border）が配置されていないため、アクティブ／非アクティブの視覚的な区別がつかず、Windows 10 / 11 の標準的なウィンドウ外観との親和性に改善の余地がある。

### 2.2 設計方針
1. **OSアクセントカラー取得ヘルパー（`SystemAccentColorHelper`）の新設**:
   - `DwmGetColorizationColor` (dwmapi.dll) を P/Invoke で呼び出し、OS のカラー値を取得。
   - DWM が無効または取得値が 0 の場合は、レジストリ `HKCU\Software\Microsoft\Windows\DWM` の `ColorizationColor` / `AccentColor` を参照し、万一の例外時にも既定アクセント色（`#0078D4`）へ安全にフォールバック。
   - アプリ起動中にも Windows の設定変更（`WM_DWMCOLORIZATIONCOLORCHANGED` (0x0320) および `WM_SETTINGCHANGE` (0x001A)）をメッセージフックで検知し、リアルタイムにブラシの色を更新。
2. **アプリケーションリソースの定義（`App.xaml`）**:
   - `SystemAccentBrush`（初期値: `#0078D4`）を定義し、動的リソース（`DynamicResource`）として参照可能にする。
3. **メインウィンドウへの外枠 Border 配置（`MainWindow.xaml`）**:
   - 最外層に `Border`（`x:Name="WindowFrameBorder"`）を配置。
   - `Border.Style` 内のトリガーにより以下を制御:
     - **通常時（非最大化）**: `BorderThickness="1"`
       - **アクティブ時 (`IsActive=True`)**: `BorderBrush="{DynamicResource SystemAccentBrush}"`
       - **非アクティブ時 (`IsActive=False`)**: `BorderBrush="{StaticResource SurfaceBorderBrush}"`
     - **最大化時 (`WindowState=Maximized`)**: `BorderThickness="0"`（枠線非表示）
4. **単体テストの作成**:
   - `SystemAccentColorHelperTests` を追加し、ARGB変換、フォールバック動作、Null安全性を検証。

---

## 3. 実装手順

### フェーズ 1: ヘルパークラスの実装
1. `src/PDFBinder.App/Helpers/SystemAccentColorHelper.cs` を新規作成。
   - Win32 API (`DwmGetColorizationColor`) 定義。
   - レジストリ読み込みおよびフォールバック処理。
   - ARGB 32-bit 値から `System.Windows.Media.Color` への変換メソッド。
   - `UpdateAccentBrush(ResourceDictionary resources)` メソッド。
   - ウィンドウフック処理（`HookWindow(Window window)`）によるカラー変更イベント監視。

### フェーズ 2: XAMLリソースおよびウィンドウ外枠の実装
1. `src/PDFBinder.App/App.xaml`:
   - `SystemAccentBrush` をリソースに追加。
2. `src/PDFBinder.App/MainWindow.xaml`:
   - ルートの `Grid` を `Border` で囲み、`IsActive` と `WindowState` に応じたトリガーを設定。
3. `src/PDFBinder.App/MainWindow.xaml.cs`:
   - `SourceInitialized` イベントで `SystemAccentColorHelper.Initialize(this)` を呼び出し、OSアクセントカラーの初期反映とメッセージフック登録を実行。

### フェーズ 3: 単体テストと品質検証
1. `tests/PDFBinder.Tests/Helpers/SystemAccentColorHelperTests.cs` を新規作成。
   - カラー変換ロジック、不正値フォールバック等のテスト。
2. `dotnet test` でテストスイートの全件パス（654件＋新規テスト）を確認。
3. `dotnet build` でビルドが警告・エラーなく完了することを確認。

---

## 4. 影響範囲
- **UI表示**: 通常表示時にアクティブ状態のウィンドウ外周にアクセントカラーの 1px 枠線が表示される。
- **パフォーマンス**: カラー取得は起動時および OS 設定変更時のみ行われるため、描画パフォーマンスへの影響は皆無。
- **互換性**: Windows 10 および Windows 11 の双方で動作。
