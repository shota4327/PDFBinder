# 実装計画: Issue #224 グリッドビューでのマウス移動ごとのO(N)コンテナ座標計算（ドラッグ＆ドロップ・範囲選択）の最適化

## 1. 概要
- **Issue**: #224 `[性能改善][影響度: 中] グリッドビューでのマウス移動ごとのO(N)コンテナ座標計算（ドラッグ＆ドロップ・範囲選択）の最適化`
- **作業ブランチ**: `issue-224-optimize-grid-mouse-move`
- **目的**:
  `GridView` におけるドラッグ＆ドロップ並び替え時の挿入インジケーター位置計算（`CalculateInsertionTarget`）およびラバーバンド範囲選択（`UpdateRubberBandSelection`）において、マウスが 1px 移動するたびに全ページ（$0 \dots N-1$）に対して実行している `ItemContainerGenerator.ContainerFromIndex` と `TranslatePoint` の $O(N)$ 処理を、**セッション中レイアウトキャッシュ＋行単位二分探索（$O(\log R + C)$ / $O(\log R + K)$）＋差分選択更新**へ最適化する。

---

## 2. 現状の課題と座標系の分析

1. **ドラッグオーバー時の全アイテム座標計算（`GridView.CalculateInsertionTarget`, L438-L503）**:
   - `OnGridDragOver` / `OnDragScrolled` のたびに全ページ分ループして `ContainerFromIndex(i)` と `container.TranslatePoint(new Point(0, 0), ItemsHostGrid)` を呼び出し、毎回 `List` 生成と LINQ（`Where` / `OrderBy` / `ToList`）によるアロケーションが発生している。
   - さらに、`UpdateInsertionIndicator` では挿入位置が変わっていない場合も毎回 `InsertionIndicator.Margin` / `Height` / `Visibility` を書き換えている。
2. **ラバーバンド範囲選択時の全アイテム座標計算・全件代入（`GridView.UpdateRubberBandSelection`, L604-L635）**:
   - `OnGridPreviewMouseMove` / `OnDragScrolled` のたびに全ページ分の `ContainerFromIndex(i)` と `TranslatePoint(..., ItemsHostGrid)` を実行し、全ページに対して `page.IsSelected = ...` を評価・代入している。
3. **座標系の特性（重要なポイント）**:
   - `GridView.xaml` では `ThumbnailItemsControl`（内部は `WrapPanel Orientation="Horizontal"`）、`InsertionIndicator`、`SelectionCanvas` がすべて `GridScrollViewer` 内の `ItemsHostGrid` の直下に配置されている。
   - したがって、`ItemsHostGrid` 座標系における各サムネイルコンテナの矩形（`Rect`）は **スクロール（`VerticalOffset` 変化）では変化しない**。
   - レイアウト座標が変化するのは、ページ数・パネル幅/高さ（ウィンドウリサイズや非同期サムネイル読み込みによる行高さ変化）・`ThumbnailSize`・ドキュメント切替が発生した時のみである。

---

## 3. 改修設計方針

単一責任の原則（SRP）および単体テスト容易性を確保するため、座標キャッシュ・行単位二分探索ロジックとラバーバンド差分選択トラッカーを `PDFBinder.App.Helpers` の独立したヘルパークラスとして切り出し、`GridView.xaml.cs` から利用する。

### 3.1 レイアウト座標キャッシュ＆行単位二分探索ヘルパーの新規作成
- **対象ファイル**: `src/PDFBinder.App/Helpers/GridLayoutCache.cs`（新規作成）
- **構成**:
  - `public readonly record struct GridItemBounds(int Index, Rect Bounds)`
  - `public readonly record struct GridRowSpan(int StartOffset, int Count, double Top, double Bottom)`
  - `public readonly record struct GridLayoutSignature(int ItemCount, double PanelWidth, double PanelHeight, double ThumbnailSize)`
  - `public sealed class GridLayoutCache`:
    - **行インデックス構築（`Build` / `BuildRows`）**:
      - `ItemsHostGrid` 基準で1回だけ収集した全アイテム矩形を `WrapPanel` の水平行（`GridRowSpan`）ごとにグルーピングして保持する（行は `Top` 昇順、行内アイテムは `Left` 昇順）。
    - **挿入位置算出（`CalculateInsertionTarget(Point mousePos, int totalPageCount)`）**:
      - 行配列に対する **二分探索（$O(\log R)$）** で `mousePos.Y` を包含する行、または ±8px の許容範囲内で最も近い行を特定する。
      - 特定した単一行内のアイテム（$C$ 件）のみを走査して左右の挿入位置・`BarRect` を算出する（従来の先頭余白 `Math.Max(4, left + 3)`、中間 `left - 2`、末尾 `right - 7`、上下端フォールバック、空ドキュメント時 `(0, Rect(10, 10, 4, 150))` の仕様を完全維持）。
      - 1回の `DragOver` あたりの計算量を $O(N)$（ビジュアルツリー探索＋座標変換＋LINQ確保）から **$O(\log R + C)$（キャッシュ上の数値比較のみ・アロケーションゼロ）** へ削減する。
    - **矩形交差アイテム探索（`FindIntersectingIndices(Rect selectionRect, HashSet<int> destination)`）**:
      - 行配列に対して二分探索で `row.Bottom >= selectionRect.Top` となる先頭行を $O(\log R)$ で特定し、`row.Top <= selectionRect.Bottom` の間だけ行内アイテムを走査する。
      - 行内でも `item.Bounds.Left > selectionRect.Right` となった時点で早期終了し、`item.Bounds.IntersectsWith(selectionRect)` が真のインデックスのみを呼び出し元提供の `destination` に格納する（フレームごとのコレクション確保なし）。

### 3.2 ラバーバンド差分選択トラッカーの新規作成
- **対象ファイル**: `src/PDFBinder.App/Helpers/RubberBandSelectionTracker.cs`（新規作成）
- **構成**:
  - セッション開始時の初期選択インデックス集合 `_initialSelectedIndices`、前フレーム交差集合 `_previousIntersectingIndices`、現フレーム交差集合 `_currentIntersectingIndices`（2つの `HashSet<int>` をフレーム間でスワップ再利用）、および初期選択適用フラグ `_isInitialSelectionApplied` を保持する。
  - `Begin(IReadOnlyList<PdfPageModel> pages)`: セッション開始時点の選択済みインデックスを記録する。
  - `Update(GridLayoutCache layoutCache, Rect selectionRect, bool isCtrlPressed, IReadOnlyList<PdfPageModel> pages)`:
    1. `layoutCache.FindIntersectingIndices(selectionRect, _currentIntersectingIndices)` で交差インデックス集合を取得（$O(\log R + K)$）。
    2. `Ctrl` キー状態が前フレームから変化した場合のみ、`_initialSelectedIndices` に対して選択の復元／解除を差分適用する。
    3. 前フレームで交差していたが今フレームで外れた要素（`_previousIntersectingIndices \ _currentIntersectingIndices`）のみ選択状態を更新（`isCtrlPressed && _initialSelectedIndices.Contains(idx)` なら維持、それ以外は `false`）。
    4. 今フレームで新たに交差した要素（`_currentIntersectingIndices \ _previousIntersectingIndices`）のみ `IsSelected = true` に更新。
    5. `_previousIntersectingIndices` と `_currentIntersectingIndices` をスワップし、実際に `IsSelected` を書き換えた件数を返却する。
  - `Reset()`: セッション終了時にすべての内部集合をクリアする。
  - これにより、矩形の拡大・縮小、ドラッグ途中の `Ctrl` キー押下/解除、`Shift` 併用開始を含むすべてのケースで従来仕様と 100% 同一の選択結果を保ちつつ、**選択状態が変化したページのみを更新する $O(\log R + K)$ 処理**を実現する。

### 3.3 `GridView` へのキャッシュ・トラッカー統合
- **対象ファイル**:
  - `src/PDFBinder.App/Views/GridView.xaml.cs`
  - `src/PDFBinder.App/Views/GridView.xaml`（`GridScrollViewer` に `LostMouseCapture="OnGridLostMouseCapture"` を追加し、Alt+Tab 等でマウスキャプチャが失われた際もセッションとキャッシュを確実に解放）
- **変更内容**:
  1. `EnsureLayoutCache()` / `InvalidateLayoutCache()` を追加:
     - 現在の `(Document, Pages.Count, ThumbnailItemsControl.ActualWidth, ThumbnailItemsControl.ActualHeight, ViewModel.ThumbnailSize)` とキャッシュのシグネチャが一致していれば既存キャッシュを $O(1)$ で再利用する。
     - 未生成またはシグネチャ変化時（ウィンドウリサイズや非同期サムネイル読み込みによる行高さ変化等）のみ `ContainerFromIndex` + `TranslatePoint` で1回だけ再構築する。
  2. `CalculateInsertionTarget` を `EnsureLayoutCache().CalculateInsertionTarget(mousePos, count)` へ委譲し、メソッドを簡潔化する。
  3. `UpdateInsertionIndicator` で直前の `_currentDropTargetIndex` と `_lastIndicatorRect`（および `Visibility`）が同一であれば `Margin` / `Height` の再代入をスキップし、不要なレイアウト再計算を抑止する。
  4. ラバーバンド開始（`OnGridPreviewMouseLeftButtonDown`）・更新（`UpdateRubberBandSelection`）・終了（`EndRubberBandSelection`）を `_rubberBandTracker` と `_layoutCache` 経由へ置き換える。
  5. セッション終了時（`EndDrag`、`OnGridDragLeave`、`OnGridDrop`、`OnGridPreviewMouseLeftButtonUp`、`OnGridLostMouseCapture`、`Unloaded`）に必ず `InvalidateLayoutCache()` および `_rubberBandTracker.Reset()` を実行し、古いキャッシュや参照が残らないようにする。

---

## 4. 単体テスト計画

### 4.1 `tests/PDFBinder.Tests/Helpers/GridLayoutCacheTests.cs`（新規作成）
- 空コレクション（0ページ）時のフォールバック結果 `(0, Rect(10, 10, 4, 150))` 検証。
- 単一ページ時の左右分割判定、上下領域フォールバック、±8px 許容範囲の検証。
- 複数行（最終行が端数のグリッド）における行頭（`Math.Max(4, left + 3)`）・行中間（`left - 2`）・行末（`right - 7`）・最上部/最下部外の挿入インデックスとバー矩形の検証。
- 行境界（`row0.Bottom == row1.Top`）および行間ギャップ内の ±8px 許容判定の検証。
- `FindIntersectingIndices` の境界値検証（空矩形、サイズ 0×0 矩形、単一アイテム、複数行跨ぎ、範囲外）。
- 大規模グリッド（例: 1,000 ページ）において、`CalculateInsertionTarget` および `FindIntersectingIndices` の結果が単純 $O(N)$ 実装と完全一致することの検証。

### 4.2 `tests/PDFBinder.Tests/Helpers/RubberBandSelectionTrackerTests.cs`（新規作成）
- 通常ドラッグでの選択範囲拡大・縮小時に正しいページ群のみが選択状態になること。
- `PropertyChanged` イベントの監視により、選択状態が変化しないページへの不要な再代入・通知が 0 件であること（差分更新の検証）。
- `Ctrl` 押下時の追加選択で、ラバーバンドを縮小しても初期選択ページが維持されること。
- ドラッグ途中の `Ctrl` キー押下/解除（`false → true → false`）で初期選択の復元・解除が正しく追従すること。
- 初期選択がある状態（`Shift` 開始等）から `Ctrl` なしでドラッグした場合に従来仕様通り交差範囲のみ選択されること。
- `Reset()` 後に別セッションとしてクリーンに再利用できること。

---

## 5. 実装・検証手順
1. `src/PDFBinder.App/Helpers/GridLayoutCache.cs` および `src/PDFBinder.App/Helpers/RubberBandSelectionTracker.cs` を新規作成する（各メソッド 30〜50 行以内、日本語コメント完備）。
2. `src/PDFBinder.App/Views/GridView.xaml.cs` および `src/PDFBinder.App/Views/GridView.xaml` を最小差分で更新する。
3. `tests/PDFBinder.Tests/Helpers/GridLayoutCacheTests.cs` および `tests/PDFBinder.Tests/Helpers/RubberBandSelectionTrackerTests.cs` を作成する。
4. `dotnet test` を実行し、既存テストおよび新規テストが 100% PASS することを確認する。
5. `dotnet build` を実行し、警告・エラーゼロを確認する。
6. 必要に応じて関連ドキュメントを確認・同期し、Walkthrough（`docs/plans/issue-224-optimize-grid-mouse-move-walkthrough.md`）を作成してユーザーへ報告する。
