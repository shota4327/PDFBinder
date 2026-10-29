# Issue #223 連続スクロール時の毎フレームO(N)走査とCancellationTokenSource過剰生成の改善 検証報告

## 1. 概要
- **Issue**: #223 [性能改善][影響度: 高] 連続スクロール時の毎フレームO(N)ビジュアルツリー走査とCancellationTokenSource過剰生成の改善
- **作業ブランチ**: `issue-223-continuous-scroll-perf`
- **バージョン**: `0.14.2`

---

## 2. 変更内容まとめ

### 2.1 可視ページおよびカレントページ判定の $O(\log N)$ 化
- **`ViewportLayoutHelper` の新規導入** (`src/PDFBinder.App/Helpers/ViewportLayoutHelper.cs`):
  - WPF 垂直 `StackPanel` の単調増加性を活用し、二分探索でビューポートと交差する代表アイテムを特定後、前後走査へ展開するアルゴリズムを実装。
  - 1,000ページを超える大規模ドキュメントでも約10ステップ（最大15回以下）の境界参照で可視アイテムを抽出。
  - 可視アイテムの境界座標（`Top`, `Bottom`）から直接ビューポート中央に最も近いカレントページを特定（`FindBestMatchingIndex`）。追加の `TransformToVisual` 呼び出しをゼロ化。
  - コンテナ未生成時等に対する安全な線形走査フォールバック（`FindVisibleItemsLinear`）を完備。
- **`DetailEditorView.xaml.cs` の最適化**:
  - `OnScrollViewerScrollChanged` および `GetVisiblePagesInViewport` で重複していた全件ループ走査（$O(N)$）を撤廃し、`ViewportLayoutHelper` 呼び出しに一本化。

### 2.2 スクロールデバウンス処理の軽量化（ゼロアロケーション化）
- **`DispatcherTimer` によるデバウンス制御**:
  - `DetailEditorView` 内に 150ms の `DispatcherTimer`（`_scrollDebounceTimer`）を導入。
  - スクロールイベントごとに発生していた `CancellationTokenSource` の生成・破棄、`Task.Delay` のアロケーション、および内部キャンセル例外（`OperationCanceledException`）の発生を完全に排除。
  - スクロールイベント時はタイマーをリセット（`Stop() -> Start()`）するのみとし、スクロールが停止したタイミングでのみ `ViewModel.ScheduleContinuousScrollRender(immediate: true)` を1回だけ呼び出す構成へ改善。
  - View の `Unloaded` および `DataContextChanged` 時のタイマー停止による確実なリソース解放とメモリリーク防止を徹底。
- **`DetailEditorViewModel.cs` の最適化**:
  - `ScheduleContinuousScrollRender(bool immediate = true)` の引数拡張により、デバウンス完了後の即時レンダリング（`immediate: true`）に対応。

---

## 3. 検証結果

### 3.1 単体テスト実行結果 (`dotnet test`)
新規追加した `ViewportLayoutHelperTests`（12テスト）および `DetailEditorViewModelTests`（2テスト）を含め、全684テストが 100% PASS しました。

- **実行コマンド**: `dotnet test`
- **結果**: 成功（失敗: 0、合格: 684、スキップ: 0、合計: 684）
- **新規テスト項目**:
  - `ViewportLayoutHelperTests.FindVisibleItems_ZeroTotalCount_ReturnsEmpty`
  - `ViewportLayoutHelperTests.FindVisibleItems_ZeroOrNegativeViewportHeight_ReturnsEmpty`
  - `ViewportLayoutHelperTests.FindVisibleItems_SingleItem_AboveViewport_ReturnsEmpty`
  - `ViewportLayoutHelperTests.FindVisibleItems_SingleItem_BelowViewport_ReturnsEmpty`
  - `ViewportLayoutHelperTests.FindVisibleItems_SingleItem_InsideViewport_ReturnsItem`
  - `ViewportLayoutHelperTests.FindVisibleItems_SingleItem_SpanningEntireViewport_ReturnsItem`
  - `ViewportLayoutHelperTests.FindVisibleItems_ManyItems_ScrolledToTop_ReturnsFirstPages`
  - `ViewportLayoutHelperTests.FindVisibleItems_ManyItems_ScrolledToMiddle_ReturnsAccurateVisiblePages`
  - `ViewportLayoutHelperTests.FindVisibleItems_LargeCount_1000Items_PerformsMinimalCalls` (1,000ページ時15回以内のアクセス検証)
  - `ViewportLayoutHelperTests.FindVisibleItems_NullBoundsDuringBinarySearch_FallsBackToLinearScan` (線形フォールバック検証)
  - `ViewportLayoutHelperTests.FindVisibleItems_UnequalPageHeights_ReturnsAccuratePages` (変形・異サイズ用紙混在検証)
  - `ViewportLayoutHelperTests.FindBestMatchingIndex_EmptyVisibleItems_ReturnsNull`
  - `ViewportLayoutHelperTests.FindBestMatchingIndex_MultipleVisibleItems_SelectsItemClosestToCenter`
  - `ViewportLayoutHelperTests.FindBestMatchingIndex_SingleItem_ReturnsItsIndex`
  - `DetailEditorViewModelTests.ScheduleContinuousScrollRender_InContinuousMode_InvokesPipelineImmediatelyWithImmediateTrue`
  - `DetailEditorViewModelTests.ScheduleContinuousScrollRender_InSinglePageMode_DoesNotInvokePipeline`

### 3.2 ビルド確認 (`dotnet build`)
- **実行コマンド**: `dotnet build`
- **結果**: 0 個の警告、0 個のエラーで正常完了。

---

## 4. ドキュメント更新
以下の関連ドキュメントを自律的に更新しました。
1. `Directory.Build.props`: バージョンを `0.14.1` から `0.14.2` へ更新
2. `CHANGELOG.md`: `[0.14.2] - 2026-10-29` リリースノート（エンドユーザー向け記述）を追加
3. `docs/basic_design.md`: セクション 6.6 に連続スクロール時の二分探索走査およびデバウンス最適化（#223）を追記
4. `docs/PROJECT.md`: 機能インベントリ F39 のステータスと備考を更新
5. `README.md`: 詳細ビューの連続スクロール機能説明に二分探索とデバウンス最適化を追記
