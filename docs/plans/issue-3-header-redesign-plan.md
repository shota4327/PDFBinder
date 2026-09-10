# 上部ヘッダー（ツールバー）デザイン刷新 実装計画

Issue #3: アプリ上部ヘッダー（ツールバー）の視認性および操作性を高めるため、アプリアイコンの単独配置・高品質表示、機能アイコンの大型化（システムアイコンフォント/ベクター採用）、リボンスタイルおよびアイコン専用ボタンスタイルの導入、表示サイズの％動的表示とリセット機能の実装を行います。

## ユーザー確認・合意事項

先ほど `/grill-me` にて合意した以下の仕様に基づき実装します：
- **アプリアイコン**: タイトル文字「PDF Binder」を削除し、38×38pxの大きめアイコンのみを左端に配置。高品質スケーリング（`RenderOptions.BitmapScalingMode="HighQuality"`）を適用し、ツールチップに「PDF Binder」を表示。
- **機能アイコン方式**: Windows標準システムアイコンフォント（`Segoe Fluent Icons, Segoe MDL2 Assets`）を採用し、アイコンサイズを 20〜24px 程度に大型化。
- **リボンスタイルボタン**: 「開く」「保存」「別名保存」「PDF追加結合」「白紙追加」「選択抽出」「全分割」は、上部に大きめアイコン、下部に機能名テキストを配置する縦並びリボンスタイルを適用。
- **アイコン専用ボタン**: 「左回転」「右回転」「削除」「元に戻す」「やり直す」はテキストを排したアイコン専用ボタンスタイル（約36×36px）とし、ツールチップで操作名をシンプルに表示。「削除」は赤系アクセントを適用。
- **表示サイズ（％表示・リセット）**: 基準値 220px を 100% としたパーセント計算プロパティを `MainViewModel` に追加し、スライダー横に「表示サイズ: 100%」を表示。クリックで 100%（220px）にリセット可能とする。

---

## 変更予定ファイル一覧

### 1. View / Resource 関連

#### [MODIFY] [App.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/App.xaml)
- 縦並びリボンボタンスタイル（`RibbonToolbarButtonStyle`）の追加
  - 上部: アイコン（Segoe Fluent Icons / Segoe MDL2 Assets、FontSize: 20〜22px）
  - 下部: ラベルテキスト（FontSize: 11px）
- アイコン専用ボタンスタイル（`IconToolbarButtonStyle`）の追加
  - 均一な幅・高さ（36×36px）、中央揃え
- 危険アクション用アイコンボタンスタイル（`DangerIconToolbarButtonStyle`）の追加
  - 削除ボタン用（通常時赤文字、ホバー時淡い赤背景）

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- ヘッダー左端:
  - タイトル「PDF Binder」テキストを削除
  - `Image` を 38×38px、`RenderOptions.BitmapScalingMode="HighQuality"`、`SnapsToDevicePixels="True"`、`ToolTip="PDF Binder"` に更新
- アクションボタングループ:
  - 「開く」「保存」「別名保存」「PDF追加結合」「白紙追加」「選択抽出」「全分割」を `RibbonToolbarButtonStyle` に更新
  - 「左回転」「右回転」「削除」「元に戻す」「やり直す」を `IconToolbarButtonStyle` に更新し、ツールチップを設定
- 表示サイズエリア:
  - `MainViewModel.ThumbnailZoomPercentage` とバインドし、「表示サイズ: 100%」の動的表示とリセットクリック動作を実装

### 2. ViewModel / Model 関連

#### [MODIFY] [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- `ThumbnailZoomPercentage` プロパティの追加（`ThumbnailSize` 変更時に `OnPropertyChanged` 通知、基準 220px = 100%）
- `ResetThumbnailSizeCommand`（`[RelayCommand]`）の追加（220.0 にリセット）

### 3. テスト関連

#### [MODIFY] [ViewModelsTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ViewModelsTests.cs)
- `ThumbnailZoomPercentage` の計算（デフォルト 100%、最小・最大値でのパーセント検証）のテスト追加
- `ResetThumbnailSizeCommand` のリセット動作検証のテスト追加

### 4. ドキュメント関連

#### [MODIFY] [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
- 上部ヘッダー・ツールバーのUI仕様・ボタン構成およびズーム表示仕様を最新化

#### [MODIFY] [docs/PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
- Issue #3 の進捗状況および機能インベントリを更新

---

## 検証手順

### 自動テスト
- `dotnet test` を実行し、既存テストおよび新規追加テスト（ズーム％計算・リセット）が 100% PASS することを確認。

### ビルド確認
- `dotnet build` を実行し、警告およびエラーなく正常にコンパイルされることを確認。
