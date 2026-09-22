# Issue #145 実装計画: バージョン管理（Directory.Build.props・SemVer）とCHANGELOG整備、Aboutダイアログの追加

## 概要
セマンティックバージョニング（SemVer）に基づくバージョン一元管理の導入、Keep a Changelog準拠の `CHANGELOG.md` ドキュメントの整備、タイトルバーのアプリアイコンから開く「version」プルダウンおよびダークテーマAboutモーダルダイアログの新設、ならびにPRマージごとの自律的インクリメント（パターン2）に関する開発運用ルールを整備します。

---

## 決定事項（ヒアリング結果まとめ）
1. **バージョン管理方式**:
   - セマンティックバージョニング（SemVer: `Major.Minor.Patch`）
   - 初期バージョン: **`0.1.0`**（直近の完成状態、正式版 `1.0.0` は以後のユーザー指示時）
   - 正本管理: リポジトリルートに **`Directory.Build.props`** を新設し一元管理
2. **CHANGELOG.md**:
   - [Keep a Changelog 1.1.0](https://keepachangelog.com/ja/1.1.0/) 準拠
   - 見出しは英語標準（`## [Unreleased]`, `### Added`, `### Changed`, `### Fixed`）
   - 各項目の説明文は **日本語**
   - 直近までの成果を `## [0.1.0] - 2026-09-22` として初期集約
3. **アプリUI（バージョン表示）**:
   - タイトルバー左上のアプリアイコンをクリック可能（`IsHitTestVisibleInChrome="True"`）にし、プルダウンメニュー（「version」）を表示
   - 選択時にダークテーマに調和した専用のモーダルダイアログ（About画面）を表示
   - アセンブリから動的にバージョン番号（`v0.1.0`）を取得して表示
4. **運用ルール（パターン2: PRマージごとの自律的インクリメント）**:
   - 各PR作業時に `CHANGELOG.md` の `[Unreleased]` を更新
   - PRマージ後、変更内容に応じてエージェントが自律的にバージョンをインクリメント（新機能: マイナー / バグ修正: パッチ）し、Gitタグ作成および `build.ps1` による配布バイナリ発行を実行

---

## 提案される変更内容

### 1. プロジェクト構成 & バージョン一元管理
#### [NEW] `Directory.Build.props`
- ルートに配置し、以下の共通プロパティを定義:
  - `<Version>0.1.0</Version>`
  - `<AssemblyVersion>0.1.0.0</AssemblyVersion>`
  - `<FileVersion>0.1.0.0</FileVersion>`
  - `<InformationalVersion>0.1.0</InformationalVersion>`
  - `<Product>PDF Binder</Product>`
  - `<Company>PDF Binder Contributors</Company>`
  - `<Copyright>Copyright © 2026</Copyright>`
- 全プロジェクト（`PDFBinder.App`, `PDFBinder.Core`, `PDFBinder.Tests`）および生成される `PDFBinder.exe` に自動反映

---

### 2. ドキュメント整備
#### [NEW] `CHANGELOG.md`
- Keep a Changelog 1.1.0 に準拠
- `## [Unreleased]` セクション
- `## [0.1.0] - 2026-09-22` セクション（これまでの主要機能を `### Added` に集約）

#### [MODIFY] `GEMINI.md`
- 「3.1 Git & ブランチ・プルリクエスト（PR）運用ルール」および「5. ドキュメントおよびルールの自律的・継続的アップデート」を更新:
  - 日常の開発時における `CHANGELOG.md` の `[Unreleased]` 追記ルール
  - PRマージ完了時の自律的バージョンインクリメント規則（パターン2: 新機能ならマイナー `0.2.0`、バグ修正ならパッチ `0.1.1`）
  - Gitタグ作成および `build.ps1` によるバイナリ再生成手順

#### [MODIFY] `docs/basic_design.md`
- バージョニング方針およびアプリアイコンの「version」プルダウン＆Aboutダイアログ仕様を追記

#### [MODIFY] `README.md`
- バージョン情報および `CHANGELOG.md` への参照リンクを追記

---

### 3. アプリUI：バージョン情報（About画面）
#### [NEW] `src/PDFBinder.App/Views/AboutDialog.xaml` & `AboutDialog.xaml.cs`
- アプリのダークテーマ（`#1E1E1E`, `#2D2D30`, `#3F3F46` 等）に調和した専用のモーダルウィンドウ
- アプリアイコン（64x64）、アプリ名、動的バージョンバッジ（例: `v0.1.0`）、アプリ概要説明、動作環境（.NET 10 / win-x64）、オープンソースライセンス情報、閉じるボタン
- `Esc` キーまたは閉じるボタン押下でスムーズに終了

#### [NEW] `src/PDFBinder.App/Services/AppVersionHelper.cs`
- 実行中のアセンブリからバージョン文字列（例: `0.1.0`）を取得するユーティリティクラス

#### [MODIFY] `src/PDFBinder.App/MainWindow.xaml`
- タイトルバー左上のアプリアイコン部分を変更:
  - `WindowChrome.IsHitTestVisibleInChrome="True"` を設定
  - クリック時にプルダウンメニュー（コンテキストメニューまたはPopup）を開き、「version」項目を表示
  - 「version」選択時に `ShowAboutCommand` を実行

#### [MODIFY] `src/PDFBinder.App/ViewModels/MainViewModel.cs`
- `ShowAboutCommand` を追加（Aboutダイアログの表示処理）

---

### 4. 単体テスト
#### [NEW] `tests/PDFBinder.Tests/Services/AppVersionHelperTests.cs`
- バージョン取得ヘルパーの単体テスト（フォーマット検証、SemVer形式チェック）

---

## 検証計画

### 自動テスト
- `dotnet test`: すべての単体テストが正常に PASS すること（既存テスト + 新規バージョンヘルパーテスト）
- `dotnet build`: 警告やエラーなくビルドが成功すること

### 手動・バイナリ検証
1. **EXEプロパティ検証**:
   - `build.ps1` を実行し、出力された `dist/self-contained/PDFBinder.exe` のプロパティ（ファイルバージョン、製品バージョン）が `0.1.0`（または `0.1.0.0`）になっていることを確認
2. **UI動作検証**:
   - アプリを起動し、タイトルバー左上のアプリアイコンをクリックした際に「version」プルダウンが表示されることを確認
   - 「version」をクリックした際、ダークテーマに調和したAboutダイアログが中央に表示され、バージョン番号 `v0.1.0` が正しく表示されることを確認
   - `Esc` キーまたは閉じるボタンでダイアログが閉じられることを確認
