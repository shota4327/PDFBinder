# Issue #223 連続スクロール時の毎フレームO(N)走査とCancellationTokenSource過剰生成の改善 実装計画

## 1. 概要
- **Issue**: #223 [性能改善][影響度: 高] 連続スクロール時の毎フレームO(N)ビジュアルツリー走査とCancellationTokenSource過剰生成の改善
- **対象カテゴリ**: 表示・描画（Display / Rendering）
- **目的**:
  - 連続スクロール表示（`Continuous`）でのスクロール時に、毎フレーム発生していた全ページ（$0 \dots N-1$）に対する `ItemContainerGenerator.ContainerFromItem` および `TransformToVisual` による $O(N)$ ビジュアルツリー走査を、二分探索と前後走査による $O(\log N)$ 処理へ最適化する。
  - 毎フレーム発生していた `ViewModel.ScheduleContinuousScrollRender()` による `CancellationTokenSource` の生成・キャンセル・破棄と `Task.Delay` アロケーションを、`DispatcherTimer` によるデバウンス処理へ改善し、スクロール中のGCプレッシャーとCPU負荷を排除する。

---

## 2. 現状の課題と改善アプローチ

### 課題 1: 毎フレームの全ページ $O(N)$ ビジュアルツリー走査
- **現状**:
  - `OnScrollViewerScrollChanged` でビューポート中央に最も近いカレントページを特定するために、全ページ（$N$ ページ）をループし `ContainerFromItem` と `TransformToVisual` を呼び出している。
  - `GetVisiblePagesInViewport()` でも同様に全ページ（$N$ ページ）をループし、ビューポートとの交差判定を行っている。
  - スクロール中（60fps）、100ページのドキュメントでは毎秒 6,000回以上のビジュアルツリー走査が発生している。
- **改善策**:
  - WPFの縦方向 `StackPanel` において、各ページコンテナのY座標はインデックス順に狭義単調増加（Monotonically Increasing）である数学的性質を利用。
  - 新規ヘルパークラス `ViewportLayoutHelper` を導入し、二分探索でビューポートと交差する代表ページ（$1$ ページ）を $\approx \log_2(N)$ ステップ（1,000ページでも約10ステップ）で特定。
  - 代表ページから前後へ走査を広げ、ビューポートと交差する可視ページ群（通常1〜3ページ）のみを抽出。
  - 可視ページ群の境界座標（`Top`, `Bottom`）から、追加の `TransformToVisual` 呼び出しなしでビューポート中央に最も近いカレントページを決定。

### 課題 2: 毎フレームの `CancellationTokenSource` と `Task.Delay` アロケーション
- **現状**:
  - スクロールイベントごとに `ViewModel.ScheduleContinuousScrollRender()` が呼ばれ、`_renderCts` の破棄・再生成と `Task.Delay(150)` が毎フレーム繰り返される。
  - スクロール中に大量のキャンセル例外（`OperationCanceledException`）が内部発生し、GCプレッシャーとUIスレッド負荷を発生させる。
- **改善策**:
  - `DetailEditorView` 内に `DispatcherTimer`（150ms）を配置。スクロールイベント発生時はタイマーをリセット（`Stop() -> Start()`）するのみとし、毎フレームの非同期タスク生成を完全に防止。
  - スクロールが停止（150ms静止）したタイミングでのみタイマーが発火し、`ViewModel.ScheduleContinuousScrollRender(immediate: true)` を1回だけ実行。
  - これによりスクロール中の CTS / Task / 例外アロケーションをゼロ（100%削減）とする。

---

## 3. 実装詳細

### 3.1 `ViewportLayoutHelper` の新規作成 (`src/PDFBinder.App/Helpers/ViewportLayoutHelper.cs`)
- 単一責任の原則に従い、座標走査ロジックをViewから分離・単体テスト可能化。
- データ構造:
  - `public readonly record struct PageBounds(double Top, double Bottom)`: コンテナのビューポート内Y座標範囲。
  - `public readonly record struct VisibleItemBounds(int Index, PageBounds Bounds)`: 可視アイテムのインデックスと境界。
- メソッド:
  - `public static List<VisibleItemBounds> FindVisibleItems(int totalCount, double viewportHeight, Func<int, PageBounds?> boundsProvider)`:
    二分探索＋前後展開による $O(\log N)$ 可視判定。コンテナ未生成等の例外時は線形走査へ安全にフォールバック。
  - `public static int? FindBestMatchingIndex(IReadOnlyList<VisibleItemBounds> visibleItems, double targetCenterY)`:
    可視アイテムの中から中央座標（`(Top + Bottom) / 2.0`）が `targetCenterY` に最も近いインデックスを算出（追加の座標変換呼び出しゼロ）。

### 3.2 `DetailEditorView.xaml.cs` の最適化
- `_scrollDebounceTimer`（`DispatcherTimer`, Interval=150ms）の追加。
- `Unloaded` および `DataContextChanged` 時のタイマー停止とリソース管理。
- `OnScrollViewerScrollChanged`:
  - `ViewportLayoutHelper.FindVisibleItems` により可視ページを $O(\log N)$ で取得。
  - `ViewportLayoutHelper.FindBestMatchingIndex` によりカレントページを決定・更新。
  - `_scrollDebounceTimer.Stop(); _scrollDebounceTimer.Start();` でスクロールデバウンスをリセット。
- `GetVisiblePagesInViewport`:
  - `ViewportLayoutHelper.FindVisibleItems` を用いて可視ページの一覧（`IEnumerable<DetailPageItemViewModel>`）を返却。

### 3.3 `DetailEditorViewModel.cs` の最適化
- `ScheduleContinuousScrollRender(bool immediate = true)`:
  - View側のデバウンス完了後に呼び出されるため、既定で `immediate: true` として即時描画を実行。
  - 外部呼び出しや単体テスト用にも柔軟に対応可能とする。

---

## 4. 単体テスト計画 (`tests/PDFBinder.Tests/ViewportLayoutHelperTests.cs` 等)
1. **`ViewportLayoutHelperTests`**:
   - 境界値・エッジケース（要素数0、要素数1、要素数2）
   - 大量要素（$N=1,000$）における二分探索の正確性と呼び出し回数（最大15回以下）の検証
   - スクロール位置（最上部、中央、最下部、オーバースクロール時）における可視範囲の正確性
   - 不均等なページ高さ（縦長・横長混在）での動作検証
   - `FindBestMatchingIndex` の正確性（中央に最も近いページの抽出）
   - コンテナ未生成時（null返却時）の安全なフォールバック
2. **`DetailEditorViewModelTests`**:
   - `ScheduleContinuousScrollRender(immediate: true)` が即時レンダリングを開始することの検証
   - `PageViewMode == DetailPageViewMode.SinglePage` 時には連続スクロール描画が実行されないことの検証

---

## 5. 品質・完了条件の検証
1. `dotnet test`: 全単体テストが 100% PASS すること（既存の 668 テスト ＋ 新規テスト）。
2. `dotnet build`: 警告およびエラーなく成功すること。
3. コード品質: メソッド行数30〜50行以内の遵守、日本語ドキュメントコメントの付与。
