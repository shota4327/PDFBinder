# Implementation Plan - Issue #243: PNG画像書き出しにおける白背景原則化および透過オプションの追加

## 1. 概要
Word等のOfficeアプリから出力されたPDFなど、用紙背景が未描画（透明）なPDFを手書き編集してPNG画像書き出しを行った際、出力画像の背景が透明のままになってしまう問題を修正します。
PNG出力においても白背景を原則（デフォルト）としつつ、必要に応じて透過PNGを出力できる「背景を透過にする」オプションとプレビュー表示機能を追加します。

---

## 2. 変更対象ファイルと責務

### Core層（モデル・サービス）
1. `src/PDFBinder.Core/Models/ImageExportSettings.cs`
   - `IsTransparentBackground` プロパティ（bool, デフォルト `false`）の追加
   - `ResetToDefault()` および `CopyFrom()` への反映
2. `src/PDFBinder.Core/Services/IImageExportService.cs`
   - `SaveImageAsync` メソッドへの `bool isTransparent = false` パラメータ追加
3. `src/PDFBinder.Core/Services/ImageExportService.cs`
   - `PrepareBitmapForExport` において、`format == ImageExportFormat.Jpeg || !isTransparent` の場合は白背景合成（`CompositeOnWhiteBackground`）を実施
   - `format == ImageExportFormat.Png && isTransparent` の場合のみ透過のまま出力

### App層（ViewModel・View・リソース）
4. `src/PDFBinder.App/ViewModels/ExportImagesViewModel.cs`
   - `CanEnableTransparency`: `Settings.Format == ImageExportFormat.Png` であれば有効化
   - `IsCheckerboardBackground`: `Settings.Format == ImageExportFormat.Png && Settings.IsTransparentBackground` であれば有効化
   - Settings変更通知に応じたプロパティ連動通知
   - `ExportPagesLoopAsync` での `SaveImageAsync` 呼び出しに `Settings.IsTransparentBackground` を連携
5. `src/PDFBinder.App/App.xaml`
   - 市松模様（チェッカーボード柄）ブラシ `CheckerboardBrush` のリソース定義
6. `src/PDFBinder.App/MainWindow.xaml`
   - 画像書き出し設定パネルに「背景を透過にする」チェックボックスを追加（`IsEnabled="{Binding CanEnableTransparency}"`）
   - プレビュー用紙の `Border` において、`IsCheckerboardBackground` に応じて `CheckerboardBrush` と白背景（`White`）を切り替えるスタイル設定

### ドキュメント・プロジェクト設定
7. `Directory.Build.props`
   - バージョンを `0.16.0` から `0.17.0` へインクリメント
8. `CHANGELOG.md`
   - `## [0.17.0] - 2026-11-05` セクションを追加（エンドユーザー向けにシンプルに記述）し、上部に空の `## [Unreleased]` を配置
9. `docs/basic_design.md` / `docs/PROJECT.md` / `README.md`
   - 仕様変更・新機能の反映

### テスト
10. `tests/PDFBinder.Tests/Services/ImageExportServiceTests.cs`
    - PNG書き出し（白背景デフォルト／透過指定）の単体テスト
11. `tests/PDFBinder.Tests/ViewModels/ExportImagesViewModelTests.cs`
    - 設定モデルおよびViewModelの透過プロパティ連動テスト

---

## 3. 実装手順（タスク分解）

### タスク 1: Core層の実装と単体テスト（モデル・サービス）
- `ImageExportSettings` に `IsTransparentBackground` を追加
- `IImageExportService` および `ImageExportService` を修正し、デフォルトで白背景合成、透過フラグ有効時のみ透過保持とする
- `ImageExportServiceTests` を追加・更新して動作検証

### タスク 2: App層の実装（ViewModel・View）
- `ExportImagesViewModel` に透過有効判定・チェッカーボード背景判定プロパティを追加し、書き出し処理へ連携
- `App.xaml` に `CheckerboardBrush` を定義
- `MainWindow.xaml` に「背景を透過にする」チェックボックスとプレビュー背景の切り替えを反映
- `ExportImagesViewModelTests` を作成・更新してViewModel動作を検証

### タスク 3: ドキュメント・バージョン更新
- `Directory.Build.props` を `0.17.0` にインクリメント
- `CHANGELOG.md`、`docs/basic_design.md`、`docs/PROJECT.md`、`README.md` を更新

### タスク 4: 全体ビルドおよびテスト検証
- `dotnet test` で全テストが 100% PASS することを確認
- `dotnet build` で警告およびエラーがないことを確認
- 検証報告（Walkthrough）を作成

---

## 4. 検証計画
- **自動テスト**:
  - `ImageExportService`:
    - 透過ピクセルを含む画像を PNG で書き出した際、デフォルト（`isTransparent = false`）で白背景ピクセルになることの検証
    - `isTransparent = true` の場合にアルファ値が保持されることの検証
    - JPEG 書き出し時は `isTransparent = true` でも白背景になることの検証
  - `ExportImagesViewModel`:
    - フォーマット切り替え（PNG/JPEG）に伴う `CanEnableTransparency` / `IsCheckerboardBackground` の状態遷移
- **全テスト実行**: `dotnet test`
