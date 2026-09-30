# Issue #191 詳細エディタ 4段階優先レンダリングパイプライン 実装計画書（追加改修-2）

## 1. 概要
詳細エディタ表示時におけるレンダリングおよびサムネイル生成の優先順位を、ユーザー指定の **厳格な 4 段階順序保証パイプライン** に統一・再構築します。

### 対象優先順位（詳細エディタ表示時）
1. **Step 1: 現在表示中のページの高解像度背景**
   - 単一ページ表示: カレントページ
   - 連続スクロール表示: 画面内の可視ページ群（中央のカレントページ最優先、続いて他の可視ページ）
2. **Step 2: 前後10ページのサムネイル（480 × 672 px）**
   - 現在地から近い順（距離優先順: 0, +1, -1, +2, -2...）で未生成分を順次生成
3. **Step 3: 前後のページの高解像度背景**
   - 単一ページ表示: カレントページの前後1ページ（ズーム600%以下時）
   - 連続スクロール表示: 可視ページ群の外側前後1ページ
4. **Step 4: 先頭50ページのサムネイル（未生成分）**
   - Step 2 で対象にならなかった未生成ページ（1〜50ページ内）を、**現在ページから近い順（距離優先）** で順次生成

### 動作制御
- ユーザーがページをめくったりスクロールした際は、進行中のパイプライン（Step 2, 3, 4）を即座にキャンセルし、新しい現在地を基準として直ちに Step 1 から再スタートします。
- 既に生成済みのサムネイル・高解像度背景は自動スキップされるため、必要な差分のみを超高速で処理します。
- グリッドビュー（Binder Overview）でのサムネイル生成は現状通り（未生成または変更されたページを昇順でオンデマンド生成）を維持します。

---

## 2. 変更対象コンポーネントと詳細設計

### 2.1 `DetailEditorViewModel.cs`
- **描画メソッドの責務分離**:
  - `RenderPrimaryPagesAsync(CancellationToken token)`:
    - Step 1 用。単一表示時はカレントページ（`High` 優先度）、連続表示時は画面内の可視ページ群（中央最優先、`High` / `Low`）を描画。
  - `RenderSecondaryNeighborPagesAsync(CancellationToken token)`:
    - Step 3 用。単一表示時は直近前後1ページ（`Low` 優先度）、連続表示時は可視ページ群の外側直近（前後1ページ）を描画。完了後に `EvictOffscreenPageBackgrounds()` を実行。
- 単一ページ表示時のページ切り替え・ズーム変更通知の連携。

### 2.2 `MainViewModel.cs`
- **4段階パイプラインの統合制御**:
  - `ScheduleDetailViewPipelineAsync(int debounceMs = 50)`:
    - 既存の `_pipelineCts` を即座にキャンセルし、新トークンを発行。
    - 単一の直列パイプライン `RunDetailViewPipelineAsync` を起動。
  - `RunDetailViewPipelineAsync(CancellationToken token)`:
    - **Step 1**: `await DetailEditor.RenderPrimaryPagesAsync(token)`
    - **Step 2**: `await GenerateWindowThumbnailsAsync(targetIdx, DetailViewThumbnailWindowRadius, token)`（距離優先順、半径10ページ）
    - **Step 3**: `await DetailEditor.RenderSecondaryNeighborPagesAsync(token)`
    - **Step 4**: `await GenerateRemainingPreloadThumbnailsAsync(targetIdx, InitialPreloadThumbnailPageCount, token)`（1〜50ページ内の未生成分、距離優先順）
- 既存の `ScheduleInitialThumbnailsAsync` および `ScheduleDetailViewThumbnailsAsync` を本パイプラインへ統合し、二重起動や競合を防止。

### 2.3 単体テストの追加・更新
- `tests/PDFBinder.Tests/DetailViewThumbnailPipelineTests.cs`:
  - Step 1 $\rightarrow$ Step 2 $\rightarrow$ Step 3 $\rightarrow$ Step 4 の実行順序が厳格に守られることの検証。
  - ページ移動時に Step 4 の未生成処理が中断され、新ページの Step 1 から再開されることの検証。
  - Step 4 が現在ページから近い順（距離優先）で 50 ページ内の未生成サムネイルを補完することの検証。

---

## 3. 実装手順
1. `DetailEditorViewModel.cs` に `RenderPrimaryPagesAsync` および `RenderSecondaryNeighborPagesAsync` を実装。
2. `MainViewModel.cs` に Step 2（前後10ページ）および Step 4（先頭50ページ未生成分・距離優先）の生成ロジックと統合パイプラインを実装。
3. `DetailViewThumbnailPipelineTests.cs` を新規作成し、単体テストを実装。
4. 全単体テスト（`dotnet test`）およびビルド（`dotnet build`）を実行・検証。
5. Walkthrough レポート（`docs/plans/issue-191-thumbnail-priority-pipeline-2-walkthrough.md`）を作成。
