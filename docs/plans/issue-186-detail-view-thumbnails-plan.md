# 実装計画: 詳細エディタ表示中の現在ページ前後10ページのサムネイル先行生成 (Issue #186)

## 1. 概要・目的
詳細エディタ（ページ閲覧・手書き編集ビュー）を表示している間、現在表示されているページの前後10ページ（現在ページを含む最大21ページ）の低解像度サムネイル（`Thumbnail`: 720×1008）を、現在ページから近い順でバックグラウンドにて先行生成します。
これにより、以下の効果を実現します：
- 高解像度描画（`PageBackground`）が完了するまでの間、サムネイルがプレビュー（下層レイヤー）として表示され、スクロール時やページ送り時の白飛びを防止する。
- 詳細エディタからグリッド俯瞰ビューへ切り替えた際に、直前に見ていた周辺ページのサムネイルが即時表示される。
- 遠いページの生成は行わず、現在ページの周辺のみに限定することで、CPUリソースとメモリ消費を最小限に抑える。

---

## 2. 設計方針・要件詳細

### 2.1 対象範囲と処理順序
- **生成対象範囲**:
  - 現在アクティブなページ（`currentIndex`）を中心とした `[currentIndex - 10, currentIndex + 10]` の範囲内の有効インデックス。
  - すでにサムネイルが存在し、ダーティフラグも立っていないページ（`Thumbnail != null && !IsThumbnailDirty`）は除外。
- **処理順序（距離優先）**:
  - 現在ページに近い順（距離 0 → +1 → -1 → +2 → -2 … → +10 → -10）に並び替えて1ページずつ直列生成。
  - 直近で移動する可能性の高い隣接ページが最優先で生成される。

### 2.2 トリガーとデバウンス制御
- **トリガー**:
  1. 詳細エディタ初期化時（ドキュメント読み込み直後）
  2. 詳細エディタでのカレントページ変更時（ページめくり、ジャンプ、連続スクロール時の可視ページ変化）
  3. グリッドビューから詳細エディタへ切り替えた時
- **デバウンス（遅延実行）**:
  - 約200〜300ms（250ms）の待機時間を設ける。
  - 高速スクロールや連続クリック中は直前のタスクをキャンセル（`_thumbnailCts?.Cancel()`）し、操作停止後に最新の表示ページを基準として生成を開始する。

### 2.3 UIへの影響（サイレント実行）
- 詳細エディタ表示中のバックグラウンドサムネイル生成中は、ステータスバーのメッセージ（`StatusMessage`）やプログレスバー表示（`IsLoading = true`）を行わず、**サイレント（非侵入的）**に実行する。手書き編集や閲覧の操作感を一切妨げない。

### 2.4 グリッドビューとの連携
- グリッドビュー（一覧表示）に切り替えた際は、既存の `EnsureThumbnailsGeneratedAsync()` により、ドキュメント全体の全未生成ページを対象に最後まで生成する。
- 詳細エディタに戻った際は、全ページ生成を中断し、現在ページの前後10ページのみに対象を絞る。

---

## 3. 変更対象ファイルと主な実装内容

1. **`src/PDFBinder.App/ViewModels/MainViewModel.cs`**:
   - 定数 `DetailViewThumbnailWindowRadius = 10` の定義
   - `ScheduleDetailViewThumbnailsAsync(int debounceMs = 250)` メソッドの追加
     - 直前の `_thumbnailCts` をキャンセル
     - デバウンス待機
     - `IsDetailViewActive` の確認
     - 現在ページインデックスを基準に距離順（0, +1, -1, +2, -2...）で対象リストを抽出
     - サイレントに `UpdatePageThumbnailAsync` を順次実行
   - `OnDetailEditorPropertyChanged` における `CurrentPageNumber` 変更検知時に `ScheduleDetailViewThumbnailsAsync` を呼び出し
   - `IsDetailViewActive` プロパティのセッター（`partial void OnIsDetailViewActiveChanged`）で、詳細ビュー復帰時に `ScheduleDetailViewThumbnailsAsync` を呼び出し、グリッドビュー復帰時に `EnsureThumbnailsGeneratedAsync` を呼び出すよう整理
   - ドキュメント読み込み後（`OpenSingleDocumentAsync` 等）に詳細エディタがアクティブな場合、初期ページ周辺のサムネイル生成をスケジュール

2. **`src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`**:
   - 必要に応じて、カレントページ変更時の通知が確実に `MainViewModel` へ届くようプロパティ通知を確認・整理

3. **`tests/PDFBinder.Tests/ViewModelsTests.cs` (または `DetailViewThumbnailsTests.cs`)**:
   - 前後10ページの対象抽出ロジック（距離順）のテスト
   - 既存サムネイル保持ページのスキップ検証
   - デバウンスとキャンセルの動作検証
   - 単体テストの追加と全件PASS確認

---

## 4. 検証手順
1. **単体テスト検証**:
   - `dotnet test` を実行し、全テスト（新規追加テスト含む）が 100% 成功することを確認。
2. **ビルド検証**:
   - `dotnet build` を実行し、エラーおよび警告がないことを確認。
3. **Walkthrough作成**:
   - `docs/plans/issue-186-detail-view-thumbnails-walkthrough.md` に実装内容とテスト結果を記録。
