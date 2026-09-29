# 実装計画: 初期化時サムネイル先行生成ページ数の拡大（先頭50ページ） (Issue #185)

## 1. 概要・背景
Issue #185 の検討において、レンダリングエンジン（PDFium / `Docnet.Core`）が内部グローバルロック（`DocLib.Lock`）により直列化されていることから、複数スレッド並列化による描画高速化は見送りとし、**ドキュメント初期読み込み（初期化）時のサムネイル先行生成ページ数を先頭10ページから先頭50ページへ拡大する** 方針を採用します。

なお、詳細エディタでのページ閲覧・めくり時に動作する「現在表示しているページの前後10ページ先行生成（`DetailViewThumbnailWindowRadius = 10`）」は現状のまま変更せず維持します。

これにより、ページ数の多いドキュメントを開いた直後に、先頭50ページ分の低解像度サムネイル（`Thumbnail`: 720×1008）がバックグラウンド（低優先度・サイレント）で生成され、ファイルオープン直後の連続スクロールやページめくり時の白紙表示を劇的に抑制します。

---

## 2. 変更対象ファイルと具体的な改修内容

### 2.1 定数および ViewModel ロジック
#### [MODIFY] [`MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
1. **初期化時先行生成ページ数定数の追加**:
   - `public const int InitialPreloadThumbnailPageCount = 50;` を定義。
   - `DetailViewThumbnailWindowRadius = 10;`（前後10ページ）は**変更せず現状のまま維持**。
2. **初期化時先行生成対象の抽出メソッドの追加**:
   - `public List<PdfPageModel> GetInitialPreloadTargetPages()` メソッドを追加。
   - ドキュメント先頭から最大50ページ（`Math.Min(Document.Pages.Count, InitialPreloadThumbnailPageCount)`）のうち、サムネイル未生成またはダーティなページ（`Thumbnail == null || IsThumbnailDirty`）を昇順（0, 1, 2, ...）で抽出。
3. **初期化時先行生成スケジューリング**:
   - `ScheduleInitialThumbnailsAsync(int debounceMs = 250)` メソッドを追加（または既存のサムネイルスケジューラを初期化対応）。
   - ドキュメント読み込み完了時（`OnActiveSessionChanged` で新規セッション設定時）に、詳細ビューがアクティブな場合は `ScheduleInitialThumbnailsAsync` を呼び出し、先頭50ページの生成を開始。
   - カレントページ変更時（ページめくり時）は、従来通り `ScheduleDetailViewThumbnailsAsync`（現在ページの前後10ページ・距離優先順）を実行。既に初期化生成済みのページはスキップされるため無駄な再生成は発生しません。

### 2.2 テストコード
#### [MODIFY] [`DetailViewThumbnailsTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailViewThumbnailsTests.cs)
- `GetInitialPreloadTargetPages` に関する単体テストを追加:
  - 100ページドキュメントで、先頭50ページ（PageNumber 1〜50）が順番に正しく抽出されること。
  - 30ページドキュメントで、上限クランプにより全30ページが抽出されること。
  - 既にサムネイルが生成済みのページが正しく除外され、未生成・ダーティなページのみが抽出されること。
- 既存の `GetDetailViewTargetPages`（前後10ページ、計最大21ページ抽出）のテストケースは**そのままパスすることを維持・確認**。

### 2.3 ドキュメント・プロジェクト設定
#### [MODIFY] [`Directory.Build.props`](file:///c:/Git/PDFBinder/Directory.Build.props)
- バージョンを `0.9.0` から `0.9.1`（パッチインクリメント）へ更新。

#### [MODIFY] [`CHANGELOG.md`](file:///c:/Git/PDFBinder/CHANGELOG.md)
- `## [Unreleased]` の直下に `## [0.9.1] - 2026-09-29` セクションを追加（エンドユーザー向け記述）。
- 「ファイルを開いた直後に、先頭50ページ分のサムネイルをバックグラウンドで先行生成するよう改善し、ページめくり時の白紙表示を大幅に低減」といった内容を記載。

#### [MODIFY] [`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md)
- ドキュメント初期化時の先行生成ページ数（先頭50ページ）と、カレントページ追従時の前後10ページ先行生成の仕様を明記・同期。

---

## 3. 検証計画
1. **単体テスト検証**:
   - `dotnet test` を実行し、既存テスト（前後10ページ検証）および新規追加テスト（先頭50ページ検証）を含む全テストが 100% 成功することを確認。
2. **ビルド検証**:
   - `dotnet build` を実行し、警告およびエラーなく成功することを確認。
3. **Walkthrough作成**:
   - `docs/plans/issue-185-increase-thumbnail-preload-pages-walkthrough.md` に実装内容とテスト結果を記録。
