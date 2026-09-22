# Walkthrough - バージョン情報ダイアログのインアプリ・オーバーレイ化とモーダルUIスタイルの共通化

## 概要
Issue #149 に基づき、従来の別ウィンドウ（`AboutDialog : Window`）形式だったバージョン情報表示を、保存確認ダイアログと同様の「インアプリ・オーバーレイ（暗転背景＋中央カード）」形式へ移行しました。
あわせて、アプリ内のモーダル表示（未保存変更確認、印刷設定・プレビュー、バージョン情報）で重複していた半透明暗転背景およびカード枠デザインを共通リソース化し、アプリ全体のデザイン統一と保守性向上を実現しました。

---

## 変更内容

### 1. モーダルUI共通スタイルの定義（`PDFBinder.App`）
* [`App.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/App.xaml):
  * `ModalBackdropGridStyle`: 半透明黒背景（`#80000000`）、フォーカス不可（`Focusable="False"`）の共通暗転背景スタイル。
  * `ModalCardBorderStyle`: 背景 `{StaticResource SurfaceBackgroundBrush}`、境界線 `{StaticResource SurfaceBorderBrush}`、角丸 `12px`、滑らかなドロップシャドウ（BlurRadius 28px, ShadowDepth 8px）を持つ共通カードスタイル。

### 2. バージョン情報ダイアログのインアプリ・オーバーレイ化
* **新コントロールの作成**:
  * [`AboutOverlayControl.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutOverlayControl.xaml) / [`AboutOverlayControl.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutOverlayControl.xaml.cs):
    * 従来のカードコンテンツ（アイコン、タイトル、バージョンバッジ `v0.2.0`、キャッチコピー、ランタイム・ライセンス・著作権情報、GitHubリンク、閉じるボタン）を移植した UserControl。
    * 外枠に `ModalCardBorderStyle` を適用。
    * 閉じるボタンに `{Binding CloseAboutCommand}` をバインド。
* **レガシーコードの削除**:
  * 従来の別ウィンドウ形式だった `AboutDialog.xaml` および `AboutDialog.xaml.cs` を完全に削除。

### 3. メイン画面の統合と共通スタイル適用（`PDFBinder.App`）
* [`MainWindow.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml):
  * 未保存変更確認ダイアログ: `ModalBackdropGridStyle` および `ModalCardBorderStyle` を適用。
  * 印刷設定・プレビューダイアログ: `ModalBackdropGridStyle` および `ModalCardBorderStyle` を適用。
  * バージョン情報ダイアログ: `Panel.ZIndex="1002"` のインアプリ・オーバーレイとして追加。
* [`MainWindow.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml.cs):
  * `HandleAboutDialogKeyDown`: バージョン情報ダイアログ表示中の Esc キー押下で `CloseAboutCommand` を実行、グローバルショートカットキーを抑止。
  * `OnAboutBackdropMouseDown`: カード外側の暗転背景クリック時にダイアログを閉じる処理を実装。
  * `OnAboutCardMouseDown`: カード本体クリック時のイベントバブリングを停止（誤クローズを防止）。

### 4. ViewModel の更新（`PDFBinder.App`）
* [`MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs):
  * `_isAboutDialogVisible` (bool, `[ObservableProperty]`) を追加。
  * `ShowAboutCommand`: `IsAboutDialogVisible = true` を設定。
  * `CloseAboutCommand`: `IsAboutDialogVisible = false` を設定。

### 5. 単体テストの拡充（`PDFBinder.Tests`）
* [`AppVersionHelperTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/Services/AppVersionHelperTests.cs):
  * `MainViewModel_ShowAboutCommand_SetsIsAboutDialogVisibleTrue`: コマンド実行で `IsAboutDialogVisible` が true になることを検証。
  * `MainViewModel_CloseAboutCommand_SetsIsAboutDialogVisibleFalse`: コマンド実行で `IsAboutDialogVisible` が false になることを検証。

### 6. プロジェクト設定・ドキュメント更新
* [`Directory.Build.props`](file:///c:/Git/PDFBinder/Directory.Build.props): バージョンを `0.1.0` から `0.2.0` へインクリメント。
* [`CHANGELOG.md`](file:///c:/Git/PDFBinder/CHANGELOG.md): `## [0.2.0] - 2026-09-22` セクションを追加。
* [`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md): 6.9節（バージョン情報インアプリ・オーバーレイ）および6.10節（共通モーダルUI仕様）を同期・更新。
* [`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md): 機能インベントリに `F62`（UI・モーダル共通化）を追加。

---

## 検証結果

### 自動テスト（`dotnet test`）
```text
C:\Git\PDFBinder\tests\PDFBinder.Tests\bin\Debug\net10.0-windows\PDFBinder.Tests.dll (.NETCoreApp,Version=v10.0) のテスト実行
合計 1 個のテスト ファイルが指定されたパターンと一致しました。

成功!   -失敗:     0、合格:   413、スキップ:     0、合計:   413、期間: 4 s - PDFBinder.Tests.dll (net10.0)
```
* 全 413 件の単体テストがすべて合格（100% PASS）。

### ビルド確認（`dotnet build`）
```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```
* 警告・エラーともに 0 件で正常終了。
