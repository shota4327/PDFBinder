# サムネイル拡大率シークバー廃止 & 「縮小」「拡大」ボタン新設 実装計画

Issue #7: グリッド俯瞰ビューにおけるサムネイル表示サイズの操作性を改善するため、シークバーおよびパーセント表示を撤去し、ツールバーのアクションボタングループ末尾に「縮小」「拡大」のリボンボタンを新設します。

## ユーザー合意事項（/grill-me）
- **シークバー・％表示の完全撤去**: ヘッダー右端の Slider およびパーセント表示ボタンを削除。
- **ボタンスタイル**: 他の機能ボタンと同一の「リボンスタイル（上アイコン 20px・下テキスト 11px、高さ48px）」。
- **アイコン**:
  - 縮小: `&#xE720;`（虫眼鏡マイナス）
  - 拡大: `&#xE71F;`（虫眼鏡プラス）
- **配置位置・並び順**:
  - ツールバー末尾（「やり直す」の右側）にセパレーター区切りで「縮小」「拡大」の順に配置。
- **変化量と上限・下限制御**:
  - 1クリックあたり 20px 増減（140px 〜 360px）。
  - 下限（140px）到達時は「縮小」ボタンを非活性（`CanExecute = false`）。
  - 上限（360px）到達時は「拡大」ボタンを非活性（`CanExecute = false`）。
- **ショートカット & 表示制御**:
  - ショートカット: `Ctrl + -`（縮小）、`Ctrl + +`（拡大）。
  - ツールチップ: 「縮小 (Ctrl+-)」「拡大 (Ctrl++)」。
  - 詳細手書きエディタ表示中は非表示（Collapsed）。

---

## 変更ファイル一覧

### 1. ViewModel / Model 関連
#### [MODIFY] [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- 定数 `MinThumbnailSize = 140.0`, `MaxThumbnailSize = 360.0`, `ThumbnailSizeStep = 20.0` の定義
- コマンド `ZoomInThumbnailCommand`（CanExecute: `CanZoomInThumbnail`）の実装
- コマンド `ZoomOutThumbnailCommand`（CanExecute: `CanZoomOutThumbnail`）の実装
- `OnThumbnailSizeChanged` での `NotifyCanExecuteChanged` 呼び出し
- 不要となったシークバー向けパーセント表示プロパティ・リセットコマンドの整理

### 2. View 関連
#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- `<Window.InputBindings>` に `Ctrl+-`（縮小）および `Ctrl++`（拡大）を追加
- ヘッダー右端のシークバーおよびパーセント表示 StackPanel を削除
- アクションボタングループ末尾にセパレーターと「縮小」「拡大」のリボンボタンを追加（`IsDetailViewActive` 連動）

### 3. テスト関連
#### [MODIFY] [ViewModelsTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ViewModelsTests.cs)
- `ZoomInThumbnailCommand` による拡大・上限（360px）および CanExecute 状態の検証テスト
- `ZoomOutThumbnailCommand` による縮小・下限（140px）および CanExecute 状態の検証テスト

### 4. ドキュメント関連
#### [MODIFY] [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
- ツールバーUI仕様のズーム操作記述をシークバーから「縮小」「拡大」ボタンへ更新
#### [MODIFY] [docs/PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
- Issue #7 のステータスおよび機能インベントリを更新

---

## 検証手順
### 自動テスト
- `dotnet test`: 既存テストおよび新規ズームイン/アウトコマンド検証テストが 100% PASS することを確認。
### ビルド確認
- `dotnet build`: 警告・エラー 0 件で成功することを確認。
