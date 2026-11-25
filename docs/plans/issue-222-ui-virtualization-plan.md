# 実装計画: Issue #222 グリッドビューおよび詳細ビュー連続スクロールモードにおけるUI仮想化・コンテナ生成負荷の改善

## 1. 概要
- **Issue**: #222 `[性能改善][影響度: 高] グリッドビューおよび詳細ビュー連続スクロールモードにおけるUI仮想化・コンテナ生成負荷の改善`
- **作業ブランチ**: `issue-222-ui-virtualization`
- **目的**:
  多ページPDF（200〜1,000ページ以上）を開いた際や、グリッドビュー・詳細ビュー連続スクロールモードへ切り替えた際に発生している「全ページ分のUIコンテナ一括生成・レイアウト計算・`DropShadowEffect` シェーダー負荷」を解消し、表示範囲周辺のみを実体化する **UI仮想化（UI Virtualization）＋コンテナリサイクル** を導入する。

---

## 2. 現状の課題と根本原因（コード調査結果）

1. **【追加発見】`MainWindow.xaml` L618 における `GridView` の常時 `Visible` 化バグ**:
   - `MainWindow.xaml` L618 が `Visibility="{Binding IsDetailViewActive, Converter={StaticResource InverseBooleanConverter}}"` となっており、`Visibility` 型ではなく `bool` 型を返すコンバーターが誤指定されている。
   - その結果、デフォルトの詳細ビュー表示中（`IsDetailViewActive == true`）であっても `GridView` が `Collapsed` にならず背面に **`Visibility.Visible` のまま常時存在** しており、PDFを開いた瞬間に全ページ分の `WrapPanel` コンテナ生成・レイアウト・`DropShadowEffect` 描画負荷が発生していた。
2. **詳細ビュー（`DetailEditorView`）の非仮想化 `ItemsControl` と常時バインディング**:
   - `DetailEditorView.xaml` L131-152 の `PagesItemsControl` は、標準の `ItemsControl` + `StackPanel` が外側の `NoAutoScrollScrollViewer` + `Grid`（`ScaleTransform` 付き）内に配置されているため UI 仮想化が一切機能しない。
   - さらにデフォルトの単一ページ表示モード（`PageViewMode == SinglePage`）でも `ItemsSource="{Binding Pages}"` が常時バインドされている。
   - `DetailEditorViewModel.InitializeDocument`（L426-429）では `Pages.Add(...)` をページ数分ループ呼び出ししており、大量の `CollectionChanged(Add)` 通知が発生している。
   - 連続表示へ切り替えると全ページ分の重いビジュアルツリー（`DropShadowEffect` 付き `Border` + `Image` ×3 + `EditorInkCanvas` + `InteractiveOverlayCanvas`）が一括生成され、数秒間のフリーズと大量メモリ消費を引き起こす。
3. **グリッドビュー（`GridView`）の非仮想化 `WrapPanel` と大量 `DropShadowEffect`**:
   - `GridView.xaml` L48-96 の `ThumbnailItemsControl` は外部 `ScrollViewer` 内の `ItemsControl` + `WrapPanel` であり、画面外の全ページ分のコンテナが一括生成される。
   - 各サムネイルの `PageBorder`（`<Image>` を内包する親要素）に直接 `DropShadowEffect` が設定されているため、カードごとにオフスクリーン描画＋リアルタイムブラーシェーダーが実行され、多数のカード表示・スクロール時のGPU描画負荷が高い。
   - また、ドラッグ元の半透明化を `container.Opacity = 0.4` でUIコンテナへ直接設定しているため、UI仮想化・コンテナリサイクル導入時に別ページへ半透明状態が誤継承されるリスクがある。

---

## 3. 改修設計方針

WPF 標準の `VirtualizingStackPanel` は外部 `ScrollViewer`（物理ピクセルスクロール）や `ScaleTransform` ズーム、`ItemsHostGrid` 上のラバーバンド選択・挿入バーと両立しないため、**各ページの正確な論理寸法（`PdfPageModel.DisplayWidth` / `DisplayHeight`）をコンテナ未生成のまま O(1)〜O(N) 数値計算できる特性** を活かし、既存の `NoAutoScrollScrollViewer` / `GridScrollViewer`（`OverlayScrollViewerStyle`・タッチパン/ピンチ・オートスクロール・ラバーバンド）を 100% 維持したまま真の UI 仮想化を実現する **ビューポート連動型カスタム `VirtualizingPanel`** を設計・導入する。

単一責任の原則（SRP）とテスト容易性を徹底するため、レイアウト計算ロジックを UI 非依存のヘルパークラスへ分離する。

### 3.1 コレクション一括更新ヘルパー＆ `DetailEditorViewModel` の最適化
- **新規作成**: `src/PDFBinder.App/Helpers/BulkObservableCollection.cs`
  - `ObservableCollection<T>` を継承し、`ReplaceAll(IEnumerable<T> items)` で内部リストを一括置換して単一の `NotifyCollectionChangedAction.Reset` のみを発火する軽量コレクション。
- **修正**: `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
  - `Pages` の実体を `BulkObservableCollection<DetailPageItemViewModel>` とする（公開プロパティ型 `ObservableCollection<DetailPageItemViewModel>` は後方互換のため維持）。
  - `InitializeDocument` で全 `DetailPageItemViewModel` の生成および `IsCurrent` / `IsFirstPage` / `IsLastPage` 設定後に `ReplaceAll` を 1 回だけ実行し、N 回の個別 `CollectionChanged` 発火を 1 回へ削減。
  - `PageViewMode` に `[NotifyPropertyChangedFor(nameof(IsContinuousMode))]` を追加し、`public bool IsContinuousMode => PageViewMode == DetailPageViewMode.Continuous;` を公開（XAML `DataTrigger` の簡潔化・高速化）。

### 3.2 レイアウト計算ヘルパーの新規作成（純粋ロジック・完全単体テスト対応）
- **新規作成**: `src/PDFBinder.App/Helpers/ContinuousPageLayoutCalculator.cs`
  - 各ページの `(DisplayWidth, DisplayHeight)` とページ間余白（`PageSpacing = 30.0`）から累積Y座標配列（`pageTops`）・全体幅（`TotalWidth`）・全体高さ（`TotalHeight`）を高速構築（1,000ページで約 5µs）。
  - `CalculateRealizationRange(verticalOffset, viewportHeight, zoom, topMargin, bottomMargin, bufferViewports, minBufferPages)`: 二分探索（$O(\log N)$）により可視範囲＋前後バッファ（前後1ビューポート分かつ最低前後2ページ）のコンテナ実体化インデックス範囲 `[FirstIndex, LastIndex]` を算出。
  - `GetPageScrollOffset(pageIndex, zoom, topMargin)`: 任意の仮想化済み（未実体化）ページへのジャンプ先 `VerticalOffset` を $O(1)$ で正確に算出。
  - `GetViewportRelativeBounds(pageIndex, zoom, verticalOffset, viewportHeight, topMargin, bottomMargin)`: 任意のページにおけるビューポート相対 `PageBounds` をコンテナの有無によらず $O(1)$ で算出（`ViewportLayoutHelper.FindVisibleItems` の線形フォールバックを根絶し、常に $O(\log N)$ を維持）。
- **新規作成**: `src/PDFBinder.App/Helpers/ThumbnailGridLayoutCalculator.cs`
  - 静的メソッド `CalculateImageHeight(double thumbnailSize, double displayWidth, double displayHeight)` を提供（アスペクト比に基づく画像高さを算出。XAML の `ThumbnailHeightConverter` と完全共有し、サムネイル非同期読み込み前後のレイアウトガタつきも同時に解消）。
  - `WrapPanel` と完全互換の水平折り返し行配置（混在用紙サイズ・回転後の縦横比変化にも対応）を数値計算のみで構築し、`TotalWidth` / `TotalHeight`・全アイテムの `Rect`・行メタデータを保持。
  - `CalculateRealizationRange(verticalOffset, viewportHeight, bufferHeight)`: 行に対する二分探索（$O(\log R)$）で可視行＋上下バッファ行に属するアイテム範囲 `[FirstIndex, LastIndex]` を算出。
  - `BuildItemBounds(Vector offset)`: 画面外の仮想化済みページを含む全ページの `GridItemBounds` リストを $O(N)$ の純粋数値演算で構築（`GridLayoutCache` へ直接供給）。

### 3.3 仮想化パネル基盤および専用パネルの新規作成
- **新規作成**: `src/PDFBinder.App/Controls/VirtualizingPanelBase.cs`
  - WPF `VirtualizingPanel` を継承する共通抽象基底クラス（各メソッド 30〜50 行以内）。
  - 祖先 `ScrollViewer` の自動検出および `ScrollChanged` イベントの安全な購読・解除（`Loaded` / `Unloaded` および初回 `MeasureOverride` 前の遅延アタッチ）。
  - スクロール時に計算された実体化範囲 `[firstIndex, lastIndex]` が前回と同一であれば `InvalidateMeasure()` を呼ばない設計とし、スクロール中の不要なレイアウト再計算を完全に抑止。
  - `IItemContainerGenerator` / `IRecyclingItemContainerGenerator` を用いた範囲実体化（`RealizeRange`）および範囲外コンテナのリサイクル（`CleanUpItems`）。
  - `OnItemsChanged` において `Remove` / `Replace` / `Move`（`ReorderPagesCommand` の `ObservableCollection.Move` に対応）/ `Reset` すべてのコレクション変更アクションに対して `InternalChildren` を整合的に更新。
- **新規作成**: `src/PDFBinder.App/Controls/VirtualizingPageStackPanel.cs`（`VirtualizingPanelBase` 継承）
  - 詳細ビュー連続スクロールモード専用パネル。`PageSpacing`（既定 30.0）、`TopMargin`（既定 20.0）、`BottomMargin`（既定 62.0）、`Zoom`（`AffectsMeasure`）の各依存関係プロパティを持つ。
  - `ContinuousPageLayoutCalculator` を用いて画面内＋前後バッファの数ページ（通常 3〜7 ページ程度）のみ実体化し、各ページを中央揃えで配置する。
  - `DetailEditorView.xaml.cs` からレイアウト計算結果を参照できるメソッドを提供。
- **新規作成**: `src/PDFBinder.App/Controls/VirtualizingThumbnailWrapPanel.cs`（`VirtualizingPanelBase` 継承）
  - グリッドビュー専用仮想化折り返しパネル。`ThumbnailSize`（`AffectsMeasure`）、`CardChromeWidth`（既定 30.0）、`CardChromeHeight`（既定 30.0）の各依存関係プロパティを持つ。
  - `ThumbnailGridLayoutCalculator` を用いて可視行＋上下バッファ行のサムネイル（通常 20〜35 枚程度）のみ実体化する。
  - コレクション内 `PdfPageModel` の `DisplayWidth` / `DisplayHeight` 変更（画面外ページの回転等）を安全に監視（解除徹底）してレイアウトを自動追従更新する。
  - `BuildAllItemBounds(Visual relativeTo)` を公開し、仮想化中も全ページ座標を `GridLayoutCache` へ提供。

### 3.4 詳細ビュー（`DetailEditorView`）の仮想化統合とリサイクル安全性確保
- **修正**: `src/PDFBinder.App/Views/DetailEditorView.xaml`
  - `DetailPageShadowEffect` として `DropShadowEffect` を `UserControl.Resources` に 1 つ定義（`PresentationOptions:Freeze="True"` により全ページ間で不変共有し、コンテナごとのインスタンス生成を排除。仮想化により同時アクティブ数も 1〜5 枚程度に限定）。
  - `PagesItemsControl` の `Style` + `DataTrigger` により、`IsContinuousMode == False`（単一ページ表示中）は `Visibility="Collapsed"` かつ `ItemsSource="{x:Null}"` とし、`IsContinuousMode == True` の時のみ `ItemsSource="{Binding Pages}"` をバインド。
  - `PagesItemsControl.ItemsPanel` を `controls:VirtualizingPageStackPanel` へ置き換え（`PageSpacing="30"`, `TopMargin="20"`, `BottomMargin="62"`, `Zoom="{Binding DataContext.Zoom, ...}"`）。ページ間余白はパネル側が担うため `ItemContainerStyle` の `Margin` トリガーを削除。
- **修正**: `src/PDFBinder.App/Views/DetailEditorView.xaml.cs`
  - `ScrollToPageInContinuousMode`: `ContainerFromItem` 依存を廃止し、`ContinuousPageLayoutCalculator` で算出した正確な Y オフセットへ直接スクロール（`DetailScrollViewer.UpdateLayout()` 後に `ScrollToVerticalOffset` 実行）。未実体化の 350 ページ目等へのジャンプも 1 回で即座に着地。
  - `GetPageBounds(int index)`: `ContainerFromItem` + `TransformToVisual` の代わりに `ContinuousPageLayoutCalculator.GetViewportRelativeBounds` を使用し、仮想化中も常に $O(1)$ 境界取得・$O(\log N)$ 可視ページ検出を維持。
- **修正**: `src/PDFBinder.App/Controls/EditorInkCanvas.cs`
  - コンテナリサイクル安全性の強化: `OnLoaded` 時に `PageItem` のストローク同期（`SyncStrokesWithCurrentMode`）を保証し、`OnUnloaded` 時に一時的なドラッグ／パン／直線描画状態および `_parentScrollViewer` キャッシュを確実にリセットする。

### 3.5 グリッドビュー（`GridView`）の仮想化統合・シャドウ分離最適化・`MainWindow` 修正
- **修正**: `src/PDFBinder.App/MainWindow.xaml`
  - L618 の `GridView.Visibility` のコンバーター誤指定（`InverseBooleanConverter`）を正しい `InverseBooleanToVisibilityConverter` へ修正し、詳細ビュー表示中に `GridView` が背面に常時展開される不具合を解消。
- **修正**: `src/PDFBinder.Core/Models/PdfPageModel.cs`
  - ドラッグ中の半透明表示をコンテナリサイクル安全にするため、UI 一時状態プロパティ `[ObservableProperty] private bool _isBeingDragged;` を追加（`IsModified` には影響しない）。
- **修正**: `src/PDFBinder.App/Converters/CommonConverters.cs` および `src/PDFBinder.App/App.xaml`
  - `ThumbnailHeightConverter : IMultiValueConverter` を追加・登録（`ThumbnailGridLayoutCalculator.CalculateImageHeight` へ委譲）。
- **修正**: `src/PDFBinder.App/Views/GridView.xaml`
  - `ThumbnailItemsControl` の `ItemsSource` を `IsDetailViewActive == False` の時のみ `Document.Pages` へバインドする `Style` + `DataTrigger` を導入（詳細ビュー表示中のコンテナ管理コストを完全ゼロ化）。
  - `ItemsPanel` を `controls:VirtualizingThumbnailWrapPanel` へ置き換え（`ThumbnailSize` をバインド）。
  - 各カードの `DropShadowEffect`（通常時・選択時）を `<UserControl.Resources>` に `PresentationOptions:Freeze="True"` で不変共有定義し、子要素を持たない影専用の背面 `Border` へ分離する（従来の柔らかい影・選択時の青い光彩の見た目をそのまま維持しつつ、サムネイル画像ごとオフスクリーンブラー処理される負荷およびコンテナごとの Effect 生成を排除）。
  - `<Image>` の `Height` を `ThumbnailHeightConverter` で明示バインド。
  - `PageBorder` の `Opacity` を `IsBeingDragged == True` の `DataTrigger` で `0.4` に制御。
- **修正**: `src/PDFBinder.App/Views/GridView.xaml.cs`
  - `StartDrag` / `EndDrag` / `Unloaded` で `_draggedContainers` への直接 `Opacity` 操作を廃止し、`_draggedPages` の `IsBeingDragged` フラグ切替へ移行（コンテナリサイクル完全対応）。
  - `EnsureLayoutCache()` を更新し、`VirtualizingThumbnailWrapPanel.BuildAllItemBounds(ItemsHostGrid)` から全ページ座標を取得して `GridLayoutCache` を構築（仮想化で画面外になったページも含め、ラバーバンド範囲選択のオートスクロールやドラッグ挿入バーが 100% 正常動作することを保証）。

---

## 4. 単体テスト計画

新規・既存ロジックの正常系・境界値・大規模ページ（500〜1,000ページ）および WPF 実パネル動作を自動テストで網羅する。

1. **`tests/PDFBinder.Tests/Helpers/BulkObservableCollectionTests.cs`（新規作成）**
   - `ReplaceAll` 実行時に `CollectionChanged` が `Reset` 1 回のみ発火し、全要素と `Count` が一致すること。
   - 空コレクションへの置換・空コレクションからの置換の境界値検証。
2. **`tests/PDFBinder.Tests/Helpers/ContinuousPageLayoutCalculatorTests.cs`（新規作成）**
   - 混在ページサイズ（縦A4 / 横A3 / 回転後）での累積 Y 座標・`TotalWidth`・`TotalHeight` の検証。
   - ズーム倍率（5%〜3200%）に応じた `CalculateRealizationRange` の二分探索結果と前後バッファ保証の検証。
   - `GetPageScrollOffset` および `GetViewportRelativeBounds`（画面内に収まる中央揃えケース・スクロール発生ケース・先頭/末尾ページ）の正確性検証。
   - 1,000 ページ構成での可視範囲・ジャンプ位置検証。
3. **`tests/PDFBinder.Tests/Helpers/ThumbnailGridLayoutCalculatorTests.cs`（新規作成）**
   - `CalculateImageHeight` の正常値・横長・縦長・0/負寸法フォールバックの検証。
   - 折り返し行構築（等幅・混在アスペクト比・極大ズームによる1列化）と `TotalHeight` の検証。
   - `CalculateRealizationRange` の行単位二分探索および上下バッファ検証。
   - 500 ページ構成での `BuildItemBounds` と `GridLayoutCache` 連携（ドラッグ挿入位置・ラバーバンド交差判定が画面外ページ含め完全一致すること）。
4. **`tests/PDFBinder.Tests/Controls/VirtualizingPanelsTests.cs`（新規作成・STA スレッド検証）**
   - `VirtualizingPageStackPanel` を持つ `ItemsControl` に 300 ページをバインドし、実体化コンテナ数（`InternalChildren.Count`）が全件（300）ではなく可視＋バッファの少数（例: 15 未満）に抑えられること、スクロールに応じてコンテナがリサイクル・差し替えされること。
   - `VirtualizingThumbnailWrapPanel` を持つ `ItemsControl` に 500 ページをバインドし、実体化コンテナ数が少数に抑えられること、`ObservableCollection.Move` / `Remove` / `Insert` / `Reset` 実行後に例外なく正しい順序でレイアウトされること。
   - `DetailEditorViewModel.InitializeDocument` で 500 ページ初期化時に `CollectionChanged` が 1 回のみ発火すること、および `IsContinuousMode` 通知が正しく連動すること。

---

## 5. 実装・検証手順
1. ヘルパークラス群（`BulkObservableCollection`, `ContinuousPageLayoutCalculator`, `ThumbnailGridLayoutCalculator`）を新規作成する。
2. 仮想化パネル群（`VirtualizingPanelBase`, `VirtualizingPageStackPanel`, `VirtualizingThumbnailWrapPanel`）を新規作成する。
3. `PdfPageModel`, `CommonConverters`, `App.xaml`, `MainWindow.xaml`, `DetailEditorViewModel`, `DetailEditorView`, `EditorInkCanvas`, `GridView` を最小差分で更新する。
4. 単体テスト一式を作成・実行し、`dotnet test` で全テスト 100% PASS および `dotnet build` で警告・エラーゼロを確認する。
5. `docs/basic_design.md` 等の関連ドキュメントを同期し、`docs/plans/issue-222-ui-virtualization-walkthrough.md` を作成してユーザーへ報告する。