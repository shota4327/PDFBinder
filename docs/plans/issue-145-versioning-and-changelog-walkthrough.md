# Issue #145 検証報告: バージョン管理（Directory.Build.props・SemVer）とCHANGELOG整備、Aboutダイアログの追加

## 概要
セマンティックバージョニング（SemVer）に基づくバージョン一元管理の導入、Keep a Changelog 1.1.0 準拠の `CHANGELOG.md` 整備、タイトルバーのアプリアイコンから開く「version」プルダウンおよびダークテーマAboutモーダルダイアログの新設、ならびにPRマージごとの自律的バージョンインクリメント運用ルール（パターン2）を整備しました。
また、ユーザーフィードバックに基づき、プルダウンの「version」文字をアイコンなし・フォントサイズ+2pt（14pt）とし、ダイアログ内のバージョンバッジ「v0.1.0」も同様に+2pt（14pt）へ拡大調整しました。

---

## 主な変更点

### 1. バージョン一元管理 (`Directory.Build.props`)
- リポジトリルートに `Directory.Build.props` を配置し、バージョン（`0.1.0`）、製品名、著作権を一元定義。
- 全プロジェクト（`PDFBinder.App`, `PDFBinder.Core`, `PDFBinder.Tests`）および生成される `PDFBinder.exe` に自動反映されることを確認。

### 2. CHANGELOG ドキュメントの整備 (`CHANGELOG.md`)
- リポジトリルートに `CHANGELOG.md` を新設。
- Keep a Changelog 1.1.0 に準拠し、見出しは英語標準（`## [Unreleased]`, `### Added`, `### Changed`, `### Fixed`）、説明文は日本語で統一。
- これまでに実装された全主要機能を `## [0.1.0] - 2026-09-22` に集約・分類。
- 今回の Issue #145 の変更を `## [Unreleased]` に追記。

### 3. アプリUI：アプリアイコンのプルダウン＆Aboutダイアログ
- **アプリアイコンのプルダウンメニュー**:
  - `MainWindow.xaml`: タイトルバー左上のアプリアイコンをボタン化し、`WindowChrome.IsHitTestVisibleInChrome="True"` を付与。
  - 左クリックでダークテーマのプルダウンメニュー（`ContextMenu`）が下方向に展開し、**アイコンなしのシンプルな文字「version」（14pt、初期12ptから+2pt拡大）** を表示。
  - `App.xaml`: アイコンがない時に不要な左余白が発生しないよう `DarkMenuItemStyle` のトリガーを最適化。
- **ダークテーマAboutモーダルダイアログ**:
  - `AboutDialog.xaml` / `AboutDialog.xaml.cs`: アプリのダークテーマ（`#1E1E1E`）に調和した角丸・ドロップシャドウ付きの専用モーダルウィンドウを新設。
  - **バージョンバッジ（`v0.1.0`）を 14pt（初期12ptから+2pt拡大）** にサイズアップし、視認性を向上。
  - アプリアイコン、アプリ名、概要、ランタイム情報（.NET 10.0 / win-x64）、ライセンス情報、GitHubリンク、閉じるボタンを表示。
- **動的バージョン取得サービス**:
  - `AppVersionHelper.cs`: アセンブリからセマンティックバージョン文字列（`0.1.0` / `v0.1.0`）を抽出するヘルパー。
- **ViewModel連携**:
  - `MainViewModel.cs`: `ShowAboutCommand` および差し替え可能なデリゲート `ShowAboutDialogAction` を追加。

### 4. 開発・運用ルールの自律更新
- `GEMINI.md`:
  - 「3.1 Git & ブランチ・プルリクエスト（PR）運用ルール」に「PRマージ時の自律的バージョンインクリメント＆Gitタグ作成（パターン2）」を明記。
  - 「5. ドキュメントおよびルールの自律的・継続的アップデート」に `CHANGELOG.md` の同期更新ルールを追記。
- `basic_design.md`:
  - セクション 6.9 にアプリアイコン・プルダウンおよびAbout画面の仕様を追加。
  - セクション 9 にバージョン管理およびリリース運用仕様を追加。
- `README.md`:
  - バージョン管理・CHANGELOG.md への参照リンクを追加。

---

## 検証結果

### 1. 単体テスト (`dotnet test`)
- 新規追加した `AppVersionHelperTests.cs` を含む、全 411 件のテストを実行。
- **結果: 411件すべて合格（失敗 0、スキップ 0）**

```text
成功!   -失敗:     0、合格:   411、スキップ:     0、合計:   411、期間: 4 s - PDFBinder.Tests.dll (net10.0)
```

### 2. ソリューションビルド (`dotnet build`)
- **結果: 0 警告、0 エラーでビルド成功**

### 3. 配布バイナリ発行およびEXEプロパティ検証 (`build.ps1`)
- `.\build.ps1` を実行し、生成された `PDFBinder.exe` のプロパティを確認:
  - **FileVersion**: `0.1.0.0`
  - **ProductVersion**: `0.1.0+...`
  - **ProductName**: `PDF Binder`
  - **LegalCopyright**: `Copyright © 2026`
- `Directory.Build.props` のバージョン情報が一元反映されていることを確認。
