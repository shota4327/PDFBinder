# Walkthrough: 初期化時サムネイル先行生成ページ数の拡大（先頭50ページ） (Issue #185)

## 1. 概要・背景
Issue #185 の技術検討において、PDFium / `Docnet.Core` の内部グローバルロック（`DocLib.Lock`）によりネイティブ描画が直列化されていることから、複数スレッド並列処理の導入は見送りとし、**ドキュメント初期読み込み（初期化）時の低解像度サムネイル先行生成ページ数を先頭10ページから先頭50ページ（`InitialPreloadThumbnailPageCount = 50`）へ拡大する** 改修を実施しました。

なお、詳細エディタでのページ閲覧・めくり時に動作する「現在表示しているページの前後10ページ先行生成（`DetailViewThumbnailWindowRadius = 10`）」は変更せずそのまま維持しています。

---

## 2. 主な変更点

### 2.1 ViewModel 層: 初期化時先行生成の分離・実装
- **[`MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)**:
  - **定数の追加**:
    - `public const int InitialPreloadThumbnailPageCount = 50;` を追加定義。
    - 閲覧時の `DetailViewThumbnailWindowRadius = 10;` はそのまま維持。
  - **対象ページ抽出メソッド**:
    - `public List<PdfPageModel> GetInitialPreloadTargetPages()` を追加。
    - ドキュメント先頭から最大50ページ（`Math.Min(Document.Pages.Count, InitialPreloadThumbnailPageCount)`）のうち、サムネイル未生成またはダーティなページを昇順で抽出。
  - **スケジューリングと共通サイレントループ**:
    - `public Task ScheduleInitialThumbnailsAsync(int debounceMs = 250)` を追加。
    - `OnActiveSessionChanged`（新規セッション設定時）において、詳細エディタがアクティブな場合に本メソッドを呼び出し、先頭50ページの生成を開始。
    - 共通のサイレント生成ループ `RunSilentThumbnailGenerationAsync` へ処理を集約し、コード重複を排除・単一責任の原則を徹底。

### 2.2 テストコード: 初期化時生成ロジックの網羅的検証
- **[`DetailViewThumbnailsTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailViewThumbnailsTests.cs)**:
  - `GetInitialPreloadTargetPages_EmptyDocument_ReturnsEmptyList`: 空ドキュメント時に空リストを返すことを検証。
  - `GetInitialPreloadTargetPages_LargeDocument_ReturnsFirst50PagesInOrder`: 100ページドキュメントで、先頭50ページ（PageNumber 1〜50）が昇順で正しく抽出されることを検証。
  - `GetInitialPreloadTargetPages_SmallDocument_ReturnsAllPagesInOrder`: 30ページドキュメントで、全30ページが取得されることを検証。
  - `GetInitialPreloadTargetPages_SkipsPagesWithExistingCleanThumbnails`: 既存のクリーンなサムネイルが除外され、未生成・ダーティなページのみが抽出されることを検証。
  - 既存の `GetDetailViewTargetPages`（前後10ページ抽出）の全テストがそのまま正常動作することを維持・確認。

### 2.3 バージョンおよびドキュメント
- **[`Directory.Build.props`](file:///c:/Git/PDFBinder/Directory.Build.props)**:
  - バージョンを `0.9.0` から `0.9.1` へインクリメント。
- **[`CHANGELOG.md`](file:///c:/Git/PDFBinder/CHANGELOG.md)**:
  - エンドユーザー向けリリースノートとして `## [0.9.1] - 2026-09-29` セクションを追加。
- **[`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md)**:
  - 初期化時先頭50ページ先行生成と閲覧時前後10ページ追従生成の仕様を明記・同期。

---

## 3. 検証結果

### 3.1 単体テスト実行結果 (`dotnet test`)
全539件の単体テストがすべて 100% 成功しました。
```
C:\Git\PDFBinder\tests\PDFBinder.Tests\bin\Debug\net10.0-windows\PDFBinder.Tests.dll (.NETCoreApp,Version=v10.0) のテスト実行
合計 1 個のテスト ファイルが指定されたパターンと一致しました。

成功!   -失敗:     0、合格:   539、スキップ:     0、合計:   539、期間: 8 s - PDFBinder.Tests.dll (net10.0)
```

### 3.2 ビルド検証結果 (`dotnet build`)
ビルドが警告・エラーなく成功しました。
```
ビルドに成功しました。
    0 個の警告
    0 エラー

経過時間 00:00:02.65
```
