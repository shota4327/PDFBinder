# 検証報告書: 詳細エディタ 4 段階優先順位パイプライン（追加改修-2）

- **Issue**: [#191](https://github.com/shota4327/PDFBinder/issues/191)
- **対象ブランチ**: `issue-191-memory-optimization`
- **作成日**: 2026-09-30
- **対応タスク**: 詳細エディタ表示時におけるレンダリングおよびサムネイル生成の厳格な4段階優先順位パイプライン化

---

## 1. 概要と背景

本追加改修（#191 追加改修-2）では、ユーザーからの確認・要望（「詳細エディタ表示時のサムネイル・高解像度背景の生成優先順位の厳格化」「ページ移動時の中断と新現在地基準での即時再開」「先頭50ページ未生成分の最低優先度・距離優先での継続補完」）に基づき、従来個別に動作していた動的レンダリングとサムネイル先行生成を、単一の直列 4 段階優先順位保証パイプラインとして統合・再構築しました。

### 確定した4段階優先順位（詳細エディタ表示時）
1. **Step 1: 現在表示中ページの高解像度背景**
   - 単一表示モード: カレントページ（最優先 `High`）
   - 連続表示モード: 画面内の可視ページ群（`Low`、現在ページは `High`）
2. **Step 2: 前後10ページのサムネイル（480×672px）**
   - 現在地からの距離優先順（0, +1, -1, +2, -2...）で直列生成。直近のページめくり時の白飛びを抑止
3. **Step 3: 前後ページの高解像度背景**
   - 単一表示モード: カレント前後1ページ
   - 連続表示モード: 可視外側の前後1ページ
   - 完了後に画面外背景画像の動的解放（`EvictOffscreenPageBackgrounds`）を実行
4. **Step 4: 先頭50ページの未生成サムネイル**
   - Step 2 の対象外となった先頭50ページ内の未生成サムネイルを、現在地からの距離優先順（同距離なら前方優先）で継続生成。Step 2 で生成済みのサムネイルは重複スキップ

---

## 2. 変更内容一覧

### 2.1 `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- **`PipelineExecutionHandler` デリゲート**:
  - `Func<int, Task>?` 型の外部委譲ハンドラーを追加。`MainViewModel` と疎結合に連携。
  - ハンドラー不在時（単体テスト環境等）はフォールバックとして Step 1 & Step 3 を直接実行し、完全な後方互換性を維持。
- **Step 分割レンダリングメソッド**:
  - `GetPrimaryPagesToRender()` / `RenderPrimaryPagesAsync(token)`: Step 1 用（単一: カレント、連続: 可視ページ群）。
  - `GetSecondaryNeighborPagesToRender()` / `RenderSecondaryNeighborPagesAsync(token)`: Step 3 用（単一: カレント前後1ページ、連続: 可視外側前後1ページ）。完了後に `EvictOffscreenPageBackgrounds()` を呼び出し。
- **`RenderPageItemAsync` の優先度引数対応**:
  - `RenderPriority priority = RenderPriority.Normal` 引数を追加し、PDFiumレンダラー排他ロックへ正確な優先度を伝達。
- **初期読み込み時の分離**:
  - `PerformDynamicRenderAsync` において、`isInitialLoad` 時はパイプライン委譲を行わず直接フォールバック描画を実施。サムネイル生成は `MainViewModel.ScheduleInitialThumbnailsAsync` 経由で整流。

### 2.2 `src/PDFBinder.App/ViewModels/MainViewModel.cs`
- **パイプライン連携の配線**:
  - コンストラクタにて `_detailEditor.PipelineExecutionHandler = delayMs => ScheduleDetailViewPipelineAsync(delayMs);` を設定。
- **Step 4 ターゲット抽出メソッド**:
  - `GetRemainingPreloadTargetPages(int currentIndex, int maxPageCount = 50)`: 先頭50ページ内の未生成・ダーティなページを、現在地からの距離昇順・同距離なら前方優先（`OrderBy(c => c.distance).ThenByDescending(c => c.index)`）で抽出。
- **直列パイプライン実行メソッド**:
  - `RunDetailViewPipelineAsync(int currentIndex, CancellationTokenSource cts, int debounceMs = 0)`: Step 1 $\rightarrow$ Step 2 $\rightarrow$ Step 3 $\rightarrow$ Step 4 を直列に実行。各ステップ間および内部ループで `token.ThrowIfCancellationRequested()` を判定。
  - `ScheduleDetailViewPipelineAsync(int debounceMs = 0)`: 実行中の `_thumbnailCts` を即座にキャンセルし、新現在地で最新パイプラインを開始。
- **既存サムネイル呼び出しの一本化**:
  - `ScheduleInitialThumbnailsAsync` および `ScheduleDetailViewThumbnailsAsync` を `ScheduleDetailViewPipelineAsync` へ集約。
  - `OnIsDetailViewActiveChanged` において、グリッド切り替え時は詳細パイプラインを即座にキャンセルし、詳細ビュー有効化時はパイプラインを開始。

### 2.3 `tests/PDFBinder.Tests/DetailViewThumbnailPipelineTests.cs`（新規作成）
- **5件のパイプライン検証テスト**:
  1. `GetRemainingPreloadTargetPages_ExcludesAlreadyGeneratedAndReturnsDistanceOrder`: 60ページ中0〜10生成済み時、11〜49が距離昇順で抽出されることを検証。
  2. `GetRemainingPreloadTargetPages_PageInMiddle_ReturnsDistanceOrderWithForwardPriority`: カレント20ページ時、同距離で前方（31）が後方（9）より優先され、50以降が含まれないことを検証。
  3. `RunDetailViewPipelineAsync_ExecutesStepsInStrictOrder`: モックレンダラーのログにより、Step 1（高解像度 High） $\rightarrow$ Step 2（サムネイル 0〜10） $\rightarrow$ Step 3（高解像度 Low） $\rightarrow$ Step 4（サムネイル 11〜49）の厳格な順序実行を検証。
  4. `ScheduleDetailViewPipelineAsync_CancelsPreviousTaskOnRestart`: 高速ページ切り替え時に先行パイプラインが中断され新ページのパイプラインが完走することを検証。
  5. `DetailEditor_WithoutHandler_FallbackDirectlyRendersStep1AndStep3`: ハンドラー未設定時に Step 1 & Step 3 が直接安全にフォールバック実行されることを検証。

### 2.4 ドキュメントおよび設定ファイルの同期
- `docs/basic_design.md`: 6.6節に「詳細エディタ 4 段階優先順位パイプライン（順序保証・中断再開制御）」の完全な仕様を追記。
- `docs/PROJECT.md`: 機能インベントリに F70（詳細エディタ 4 段階優先順位パイプライン）を完了として追加。
- `CHANGELOG.md`: [0.9.2] セクションにエンドユーザー向けリリースノートを追記。

---

## 3. 検証結果

### 3.1 単体テスト実行結果 (`dotnet test`)
```
成功!   -失敗:     0、合格:   551、スキップ:     0、合計:   551、期間: 7 s - PDFBinder.Tests.dll (net10.0)
```
- 全551件の単体テストがすべて PASS。
- 新規追加した `DetailViewThumbnailPipelineTests`（5件）および更新した `DetailViewThumbnailsTests`（12件）もすべて PASS。

### 3.2 ビルド検証結果 (`dotnet build`)
```
ビルドに成功しました。
    0 個の警告
    0 エラー
経過時間 00:00:02.42
```
- コンパイル警告およびエラー 0 件を確認。

---

## 4. ユーザー要求に対する適合性チェックリスト

| ユーザー要求項目 | 実装方針・確認結果 | 判定 |
| :--- | :--- | :---: |
| 1. 現在表示中のページの高解像度背景（Step 1） | `DetailEditor.RenderPrimaryPagesAsync` により単一時は最優先 `High` で即時描画 | **適合** |
| 2. 前後10ページのサムネイル（Step 2） | `GenerateWindowThumbnailsAsync` により距離優先順で480×672pxサムネイルを生成 | **適合** |
| 3. 前後のページの高解像度背景（Step 3） | `DetailEditor.RenderSecondaryNeighborPagesAsync` により前後1ページを高解像度描画後、画面外アンロードを実施 | **適合** |
| 4. 先頭50ページのサムネイル（Step 4） | `GetRemainingPreloadTargetPages` によりStep 2未生成分のみを抽出、距離優先順（同距離なら前方優先）で生成 | **適合** |
| ページ移動時のキャンセル＆即時再開 | `ScheduleDetailViewPipelineAsync` で直前の `_thumbnailCts` を即座に中断し、新ページ基準でStep 1から即時再開 | **適合** |
| グリッドビューは現状維持 | グリッドビュー表示時は従来通りの昇順オンデマンド生成を維持し、詳細ビューパイプラインと干渉しないよう分離 | **適合** |
