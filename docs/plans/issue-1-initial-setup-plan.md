# 実装計画書: PDF Binder アプリケーション基盤構築 (Issue #1)

Windowsデスクトップ向けPDF編集・バインダー管理・手書きアノテーションアプリ「PDF Binder」の初期プロジェクト構築と基本実装計画を定めます。

## ユーザー確認・合意事項
以下の主要設計方針について確認・合意済みです。
1. **プラットフォーム・言語**: Windows デスクトップ、C# (.NET 10.0 / WPF、`net10.0-windows`)
2. **UIデザイン**: WPF-UI / モダンFluentスタイル、プロジェクト同梱の `icon.png` を正本アプリアイコンとして使用
3. **PDF処理 & レンダリング**:
   - `PdfSharp` (MIT): ページの回転、並び替え、削除、結合、分割、空白ページ追加、PDF出力
   - `PDFium` (Apache-2.0 / BSD): ページの高速・高精細サムネイル生成およびエディタ内レンダリング
4. **画面構成・操作性**:
   - **メイン画面**: 全ページをタイル状に俯瞰できるサムネイルグリッド（ドラッグ＆ドロップ並び替え、回転、削除、複数選択、結合・分割、Undo/Redo）
   - **手書き詳細画面**: ページダブルクリックで開くフル機能エディタ（ペン、蛍光ペン、消しゴム[ストローク/部分]、直線、ズーム・パン、Undo/Redo）
5. **ファイル管理**: メモリ/一時バッファ読み込みによる非破壊編集と安全な上書き/別名保存
6. **開発規約**: `sampleGEMINI.md` をベースに策定した `GEMINI.md` を厳格に遵守

---

## 提案する変更内容

### 1. プロジェクト構造とソリューション初期化
.NET 10.0 ソリューションを作成し、責務に応じてプロジェクトを分離します。

- `PDFBinder.sln`: ソリューションファイル
- `src/PDFBinder.App`: WPF メインアプリケーション（UI、Views、ViewModels、Styles）
- `src/PDFBinder.Core`: PDF操作、ドキュメントモデル、レンダリング、アノテーションロジック（UI非依存の純粋クラスライブラリ）
- `tests/PDFBinder.Tests`: xUnit 単体テストプロジェクト

#### [NEW] [PDFBinder.sln](file:///c:/Git/PDFBinder/PDFBinder.sln)
#### [NEW] [PDFBinder.App.csproj](file:///c:/Git/PDFBinder/src/PDFBinder.App/PDFBinder.App.csproj)
#### [NEW] [PDFBinder.Core.csproj](file:///c:/Git/PDFBinder/src/PDFBinder.Core/PDFBinder.Core.csproj)
#### [NEW] [PDFBinder.Tests.csproj](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PDFBinder.Tests.csproj)

---

### 2. ドキュメント体系の整備
`GEMINI.md` の第5条に従い、正本ドキュメントを作成します。

#### [NEW] [basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md): システム構成、クラス設計、画面仕様、アノテーションデータ仕様を網羅
#### [NEW] [PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md): 機能インベントリと進捗ステータス管理
#### [NEW] [README.md](file:///c:/Git/PDFBinder/README.md): アプリ概要、機能一覧、ビルド手順
#### [NEW] [issue-1-initial-setup-plan.md](file:///c:/Git/PDFBinder/docs/plans/issue-1-initial-setup-plan.md): 計画書の永続保管

---

### 3. コア機能実装のロードマップ（フェーズ分け）

- **フェーズ 1: プロジェクト基盤とアイコン設定**
  - .NET 10.0 ソリューション作成、NuGetパッケージ導入（PdfSharp, PDFiumSharp, CommunityToolkit.Mvvm 等）
  - `icon.png` を `.ico` リソースおよび WPF ウィンドウアイコンとして設定
- **フェーズ 2: PDF処理コア（PDFBinder.Core）の実装と単体テスト**
  - PDF読み込み・非破壊ドキュメントモデル
  - ページ操作（回転、削除、並び替え、結合、分割、空白ページ追加）の実装
  - xUnit による完全自動テスト作成・パス確認
- **フェーズ 3: グリッド俯瞰ビューとサムネイル表示の実装**
  - PDFiumによるサムネイル生成
  - グリッド一覧レイアウト、複数選択、ドラッグ＆ドロップによる並び替え
  - ツールバー（開く、上書き保存、別名で保存、結合、分割、空白追加、アンドゥ/リドゥ）
- **フェーズ 4: 手書き詳細エディタの実装**
  - InkCanvas統合（ペン、蛍光ペン、消しゴム、直線ツール）
  - ズーム（拡大・縮小）＆パン（手のひらツール）
  - インクストロークの高解像度PDF反映・保存
- **フェーズ 5: 統合検証とリリースビルド確認**
  - 単一ファイル配布ビルド（Self-Contained Single-File EXE）の検証
  - 各種PDFでの動作確認・メモリリーク検証

---

## 検証計画

### 自動テスト
- コア操作の単体テスト:
  ```powershell
  dotnet test
  ```
- ソリューション全体のビルド確認:
  ```powershell
  dotnet build
  ```

### 手動検証
- 各種PDFファイル（単一ページ、複数ページ、横向きページ、混合サイズ等）を読み込み、回転・並び替え・削除・結合・分割・空白追加が正しく動作することを確認。
- ページをダブルクリックして手書きエディタを開き、ペン・蛍光ペン・消しゴム・直線が正しく描画・消去され、PDFに保存できることを確認。
