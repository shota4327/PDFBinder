# バージョン情報ダイアログのインアプリ・オーバーレイ化とモーダルUIスタイルの共通化

## 概要
現在、別ウィンドウ（`Window`）として表示されている「バージョン情報ダイアログ（About画面）」を、保存確認ダイアログと同様の「インアプリ・オーバーレイ（暗転背景＋中央カード）」表示方式へ移行します。
また、アプリ内の3大オーバーレイ（保存確認、印刷、バージョン情報）で重複している半透明暗転背景およびカード枠デザイン（角丸12px、サーフェス背景、境界線、ドロップシャドウ）のスタイル定義を共通リソース化し、アプリ全体のデザイン統一と保守性向上を図ります。

## 決定事項（インタビュー結果）
1. **共通化方針**: UI層（外枠スタイル・カード枠デザイン）を共通リソース化し、印刷ダイアログも含めてスタイルを統一する。表示制御プロパティはViewModelごとに独立して管理する。
2. **コンポーネント構成**: バージョン情報UIは [`AboutOverlayControl`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutOverlayControl.xaml)（UserControl）として独立したViewに切り出し、[`MainWindow.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml) にインアプリ・オーバーレイとして配置する。
3. **背景クリック挙動**: バージョン情報ダイアログは情報表示型であるため、背景暗転部分（カード外側）のクリックでも閉じるようにする。
4. **レガシーコードの扱い**: 従来の別ウィンドウ形式の [`AboutDialog.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutDialog.xaml) / [`AboutDialog.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutDialog.xaml.cs) は完全に削除する。

---

## User Review Required

> [!NOTE]
> - 保存確認ダイアログと印刷ダイアログは誤操作防止のため「背景暗転部分のクリックでは閉じない」仕様を維持します。
> - バージョン情報ダイアログのみ、「閉じるボタン」「Escキー」に加えて「背景暗転部分クリック」でも閉じられる親切設計とします。
> - `Directory.Build.props` のバージョンは機能拡張として `0.1.0` から `0.2.0` にインクリメントします。

---

## Proposed Changes

### 1. UIスタイル共通化（`PDFBinder.App`）

#### [MODIFY] [App.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/App.xaml)
- モーダル共通スタイルを追加:
  - `ModalBackdropStyle`: 半透明背景（`#80000000`）、フォーカス不可、ZIndex 等の設定用スタイル
  - `ModalCardBorderStyle`: 背景 `{StaticResource SurfaceBackgroundBrush}`、境界線 `{StaticResource SurfaceBorderBrush}`、角丸 `12`、ドロップシャドウ効果を持つ共通カードスタイル

---

### 2. バージョン情報インアプリ・オーバーレイ（`PDFBinder.App`）

#### [NEW] [AboutOverlayControl.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutOverlayControl.xaml)
- 従来の `AboutDialog.xaml` のカード内コンテンツ（アイコン、バージョンバッジ、アプリ紹介、各種機能一覧、GitHubリンク、閉じるボタン）を移植した UserControl。
- カード枠には `ModalCardBorderStyle` を適用。
- 閉じるボタンは `{Binding CloseAboutCommand}` にバインド。

#### [NEW] [AboutOverlayControl.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutOverlayControl.xaml.cs)
- 初期化およびハイパーリンククリック時の外部ブラウザ起動処理（`OnRequestNavigate`）を実装。
- バージョン文字列は `AppVersionHelper.DisplayVersion` をバインド/設定。

#### [DELETE] [AboutDialog.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutDialog.xaml)
- 不要となった従来のウィンドウXAMLを削除。

#### [DELETE] [AboutDialog.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/AboutDialog.xaml.cs)
- 不要となった従来のウィンドウコードビハインドを削除。

---

### 3. メイン画面への組み込みと共通スタイル適用（`PDFBinder.App`）

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- **保存確認ダイアログ**: 背景GridおよびカードBorderに共通スタイルを適用。
- **印刷ダイアログ**: 背景GridおよびカードBorderに共通スタイルを適用。
- **バージョン情報ダイアログ**:
  - `Panel.ZIndex="1002"` のインアプリ・オーバーレイを追加。
  - `Visibility="{Binding IsAboutDialogVisible, Converter={StaticResource BooleanToVisibilityConverter}}"` で表示制御。
  - 背景Gridのクリックで `CloseAboutCommand` を呼び出す（カード部分のクリック時はイベント停止して閉じないようにする）。
  - 中央に `AboutOverlayControl` を配置。

#### [MODIFY] [MainWindow.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml.cs)
- キーボードハンドリングに `HandleAboutDialogKeyDown` を追加:
  - `vm.IsAboutDialogVisible` が true の場合、Esc キー押下で `vm.CloseAboutCommand.Execute(null)` を実行。
  - グローバルショートカットキーの抑止。
- バージョン情報ダイアログ背景クリックによる閉じるハンドラー `OnAboutBackdropMouseDown` を実装。

---

### 4. ViewModel の更新（`PDFBinder.App`）

#### [MODIFY] [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- `_isAboutDialogVisible` (bool, `[ObservableProperty]`) を追加。
- `ShowAbout()`: `IsAboutDialogVisible = true` を設定（`ShowAboutDialogAction` が設定されている場合はテスト互換のためそれを優先）。
- `CloseAbout()`: `IsAboutDialogVisible = false` を設定する `[RelayCommand]` を追加。

---

### 5. 単体テストの更新（`PDFBinder.Tests`）

#### [MODIFY] [AppVersionHelperTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/Services/AppVersionHelperTests.cs)
- `MainViewModel_ShowAboutCommand_SetsIsAboutDialogVisibleTrue`: コマンド実行で `IsAboutDialogVisible` が true になることを検証。
- `MainViewModel_CloseAboutCommand_SetsIsAboutDialogVisibleFalse`: コマンド実行で `IsAboutDialogVisible` が false になることを検証。
- 既存の `ShowAboutDialogAction` テストの維持。

---

### 6. プロジェクト設定・ドキュメント更新

#### [MODIFY] [Directory.Build.props](file:///c:/Git/PDFBinder/Directory.Build.props)
- バージョンを `0.1.0` から `0.2.0` にインクリメント。

#### [MODIFY] [CHANGELOG.md](file:///c:/Git/PDFBinder/CHANGELOG.md)
- `## [0.2.0] - 2026-09-22` セクションを追加し、最上部に空の `## [Unreleased]` を配置。
- バージョン情報ダイアログのインアプリ・オーバーレイ化およびモーダルUI共通化を記録。

#### [MODIFY] [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
- バージョン情報ダイアログの表示方式（インアプリ・オーバーレイ）および共通モーダルスタイルの仕様を更新。

#### [MODIFY] [docs/PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
- 機能インベントリの更新。

---

## Verification Plan

### 自動テスト
- 全テストスイートの実行:
  ```powershell
  dotnet test
  ```
- ソリューション全体のビルド検証:
  ```powershell
  dotnet build
  ```

### 手動検証（動作確認観点）
1. **バージョン情報ダイアログの表示・消去**:
   - 右上の「version」メニューまたはヘルプからバージョンダイアログがインアプリ・オーバーレイとして美しく開くこと。
   - 「閉じる」ボタンのクリックでダイアログが閉じること。
   - 背景の暗転部分をクリックするとダイアログが閉じること。
   - カード内（リンクやテキスト等）をクリックしても閉じないこと。
   - Esc キーを押すとダイアログが閉じること。
   - バージョン番号が正しく表示されていること。
2. **既存ダイアログへの影響確認**:
   - 保存確認ダイアログが崩れず従来通り動作すること（背景クリックでは閉じず、ボタンやEscで閉じること）。
   - 印刷ダイアログが崩れず従来通り動作すること。
