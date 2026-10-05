# 検証報告: PDFからJPEG/PNGへの画像書き出し機能 (#30)

## 概要
Issue #30 で要望された「PDFからJPEG/PNGへの画像書き出し」機能を実装しました。印刷プレビュー画面と同様の直感的なUI構造（左側: 設定パネル、右側: リアルタイム用紙連動プレビュー）を持つインアプリ・オーバーレイダイアログを提供し、指定した出力形式（PNG / JPEG）、解像度（200 / 300 / 400 / 600 DPI）、ページ範囲（すべて / 現在のページ / ページ指定）での高品位な画像書き出しを実現しました。

---

## 変更内容

### 1. コアモデル & サービス (`PDFBinder.Core`)
- **[ImageExportEnums.cs](file:///C:/Git/PDFBinder/src/PDFBinder.Core/Models/ImageExportEnums.cs)**:
  - 出力フォーマット `ImageExportFormat` (`Png`, `Jpeg`)
  - 出力解像度 `ImageExportDpi` (`Dpi200`, `Dpi300`, `Dpi400`, `Dpi600`)
  - ページ範囲種別 `ImageExportRangeType` (`AllPages`, `CurrentPage`, `Custom`)
- **[ImageExportSettings.cs](file:///C:/Git/PDFBinder/src/PDFBinder.Core/Models/ImageExportSettings.cs)**:
  - 画像書き出し設定モデル。初期値: PNG、400 DPI、全ページ
- **[IImageExportService.cs](file:///C:/Git/PDFBinder/src/PDFBinder.Core/Services/IImageExportService.cs)** & **[ImageExportService.cs](file:///C:/Git/PDFBinder/src/PDFBinder.Core/Services/ImageExportService.cs)**:
  - PDF物理寸法（72 DPIポイント）から指定DPIにおける正確なピクセル寸法を算出する `CalculatePixelSize`
  - ビットマップを指定フォーマット・DPIメタデータ・JPEG品質90%で一時ファイルへ安全出力したのち不可分置換する `SaveImageAsync`
  - JPEG出力時の透過ピクセル黒ずみを防止する白背景下地合成処理

### 2. ViewModel & アプリケーション層 (`PDFBinder.App`)
- **[ExportImagesViewModel.cs](file:///C:/Git/PDFBinder/src/PDFBinder.App/ViewModels/ExportImagesViewModel.cs)**:
  - ページ範囲（すべて、現在のページ、カスタム指定）の入力検証と対象ページリスト算出
  - 用紙シャドウ付きリアルタイムプレビュー表示およびページ送りナビゲーション（◀ 1 / 5 ▶）
  - 単一ページ時は `SaveFileDialog`、複数ページ時は `OpenFolderDialog` による保存先選択
  - ゼロ埋め連番ファイル（`{ドキュメント名}_page_001.png` 等）の自動生成
  - キャンセル可能な書き出しループ処理および進捗パーセンテージ・テキスト更新
- **[MainViewModel.cs](file:///C:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)**:
  - `ExportImagesViewModel` の生成・表示・終了通知ハンドリング（`ShowExportImagesDialogCommand`, `CloseExportImagesDialogCommand`）
  - 完了後のステータスバーへの完了通知メッセージ反映
  - ドキュメント状態に応じた `CanExportImages` プロパティの更新

### 3. View (`PDFBinder.App`)
- **[MainWindow.xaml](file:///C:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)**:
  - リボン「編集」タブの「ページ構成・抽出」グループ、「ページ分割」ボタンの右横に「画像保存」ボタン（アイコン: `&#xEB9F;`）を配置
  - インアプリ・オーバーレイダイアログ（`IsExportImagesDialogVisible`）の追加
    - 左側設定パネル: フォーマット（PNG/JPEG）、解像度（200/300/400/600 DPI）、書き出し範囲（すべて/現在のページ/ページ指定）
    - 右側プレビューパネル: 白背景シャドウ用紙プレビュー、ページ送りボタン
    - フッター: 進捗バー・ステータステキスト、書き出しボタン、キャンセルボタン

### 4. 単体テスト (`PDFBinder.Tests`)
- **[ImageExportServiceTests.cs](file:///C:/Git/PDFBinder/tests/PDFBinder.Tests/ImageExportServiceTests.cs)**:
  - DPI計算（200/300/400/600 DPI）の正常系および境界値テスト
  - PNGおよびJPEGへの画像ファイル出力・デコード検証テスト
- **[ExportImagesViewModelTests.cs](file:///C:/Git/PDFBinder/tests/PDFBinder.Tests/ExportImagesViewModelTests.cs)**:
  - 範囲選択（すべて、現在のページ、カスタム指定、不正入力エラー）の検証テスト
  - ページ送り（◀ ▶）のインデックス移動テスト
  - 単一ページ（ファイル保存）および複数ページ（フォルダー保存）の書き出し実行テスト
- **[MainViewModelExportImagesTests.cs](file:///C:/Git/PDFBinder/tests/PDFBinder.Tests/MainViewModelExportImagesTests.cs)**:
  - ダイアログの表示・非表示、プロパティ連動、完了時のステータスバー通知テスト

### 5. プロジェクト設定 & ドキュメント
- **[Directory.Build.props](file:///C:/Git/PDFBinder/Directory.Build.props)**: 新機能追加に伴い `0.10.2` から `0.11.0` にインクリメント
- **[CHANGELOG.md](file:///C:/Git/PDFBinder/CHANGELOG.md)**: `[0.11.0] - 2026-10-05` セクションを追加（エンドユーザー向け記述）
- **[docs/PROJECT.md](file:///C:/Git/PDFBinder/docs/PROJECT.md)**: 機能インベントリに `F74`（画像書き出し機能）を追加
- **[docs/basic_design.md](file:///C:/Git/PDFBinder/docs/basic_design.md)**: 第9章「PDFからJPEG/PNGへの画像書き出し仕様」を新規追加
- **[README.md](file:///C:/Git/PDFBinder/README.md)**: 主な機能に「PDFからJPEG/PNGへの高画質画像書き出し」を追加

---

## 検証結果

### 1. ビルド検証 (`dotnet build`)
- 警告: **0**
- エラー: **0**
- 結果: **成功**

### 2. 単体テスト検証 (`dotnet test`)
- 合計テスト数: **595**
- 合格: **595**
- 失敗: **0**
- スキップ: **0**
- 結果: **全テスト 100% PASS**
