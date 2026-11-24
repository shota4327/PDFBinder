# Issue #226 実装計画: 直線ツールプレビュー描画時の毎フレームPen/Brush生成およびCurrentPageItem線形探索の改善

## 概要
Issue #226 に基づき、詳細手書きエディタにおける以下の描画・参照オーバーヘッドを改善します。

1. `EditorInkCanvas.OnRender` で直線ドラッグ中に毎フレーム新規生成していた未フリーズ `Pen` / `SolidColorBrush` をキャッシュ＆`Freeze()` 化し、フレームごとのオブジェクト生成およびWPFレンダリングスレッドへの転送コストを削減する。
2. `DetailEditorViewModel.CurrentPageItem`（および `CurrentPageIndex`）参照時の毎回の線形探索（`FirstOrDefault` / `for` ループ）をキャッシュとインデックスヒントによる $O(1)$ 取得へ改善する。
3. `DetailEditorViewModel.CheckVerticalScrollOverflow` の全ページ走査（LINQ `Sum`）をループ早期終了に改善し、多ページ連続表示時の不要な走査・アロケーションを削減する。

---

## 変更対象ファイルと設計詳細

### 1. `src/PDFBinder.App/Controls/EditorInkCanvas.cs`
- **キャッシュフィールドの追加**:
  - `private Pen? _cachedLinePreviewPen;`
  - `private Color _cachedLinePreviewColor;`
  - `private double _cachedLinePreviewWidth;`
- **`ApplyDrawingAttributes()` でのキャッシュ無効化**:
  - 描画色・太さ・ツールモード・ページアイテム変更時に呼ばれる `ApplyDrawingAttributes()` 内で `_cachedLinePreviewPen = null;` を設定し、不要な旧参照を破棄する。
- **フリーズ済みプレビュー `Pen` 取得ヘルパー `GetOrCreateLinePreviewPen()` の追加**:
  - `DefaultDrawingAttributes` と `ToolMode` から描画色（蛍光ペン時は Alpha=120）および線幅を算出する。
  - キャッシュ済みの `_cachedLinePreviewPen` が存在し、色・太さが一致していればそのまま再利用して返す（フレームごとのアロケーションなし）。
  - 未生成または属性不一致の場合のみ `SolidColorBrush` と `Pen`（`StartLineCap = PenLineCap.Round`, `EndLineCap = PenLineCap.Round`）を新規生成し、両方を `Freeze()` した上でフィールドへキャッシュして返す。
- **`OnRender(DrawingContext dc)` の変更**:
  - 毎フレームの `new Pen(new SolidColorBrush(color), attr.Width)` を `GetOrCreateLinePreviewPen()` の呼び出しへ置き換える。
- **テスト用ヘルパーの追加（`#region テスト用ヘルパー`）**:
  - `internal Pen GetLinePreviewPenForTesting() => GetOrCreateLinePreviewPen();`
  - `internal Pen? CachedLinePreviewPenForTesting => _cachedLinePreviewPen;`

### 2. `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- **`CurrentPageItem` / `CurrentPageIndex` の $O(1)$ キャッシュ機構**:
  - キャッシュ用フィールドを追加:
    - `private DetailPageItemViewModel? _cachedCurrentPageItem;`
    - `private int _cachedCurrentPageIndex = -1;`
    - `private bool _isCurrentPageCacheValid;`
  - 両コンストラクタで `Pages.CollectionChanged += OnPagesCollectionChanged;` を購読し、`Dispose(bool disposing)` で確実に購読解除する。
  - `OnPagesCollectionChanged` および `OnCurrentPageChanged` の先頭で `_isCurrentPageCacheValid = false;` を設定してキャッシュを無効化する。
  - `EnsureCurrentPageCache()` メソッドを追加:
    1. `_isCurrentPageCacheValid` が `true` なら即座に返却。
    2. `Pages.Count == 0` なら `(null, -1)` をキャッシュ。
    3. `CurrentPage != null` の場合、まず `int hintIndex = CurrentPage.PageNumber - 1;` を $O(1)$ で検証し、範囲内かつ `Pages[hintIndex].Page == CurrentPage` であれば即確定。
    4. ページ番号とインデックスが一致しない特殊ケース（テスト等）のみフォールバック線形探索を実行。
    5. `CurrentPage` が `null` または `Pages` 内に見つからない場合は、従来仕様通り `_cachedCurrentPageIndex = -1`、`_cachedCurrentPageItem = Pages[0]` とする。
  - `CurrentPageItem` および `CurrentPageIndex` の getter から `EnsureCurrentPageCache()` を経由してキャッシュ値を返却する。
- **`CheckVerticalScrollOverflow` の早期終了化**:
  - 連続表示時の `Pages.Sum(p => p.Page.DisplayHeight)` を `for` ループへ変更し、累積高さが `availableHeight` を超えた時点で即座に `return true;` するよう改善する。

---

## 単体テスト計画

### 1. `tests/PDFBinder.Tests/DetailEditorStraightLineTests.cs`
以下のテストケースを追加（STAスレッド上で実行）:
- 直線プレビュー用 `Pen` およびその `Brush` が `Freeze()` 済み（`IsFrozen == true`）であること。
- 同一の描画属性で複数回呼び出した場合に、同一の `Pen` インスタンスが再利用されること（`Assert.Same`）。
- `DrawingColor`、`StrokeThickness`、`ToolMode`（通常ペン ⇔ 蛍光ペン）を変更した場合にキャッシュが更新され、新しい色（蛍光ペン時は Alpha=120）・太さのフリーズ済み `Pen` が返ること。

### 2. `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs`
以下のテストケースを追加:
- 多ページドキュメントで `CurrentPage` を切り替えた際、`CurrentPageItem` および `CurrentPageIndex` が正しく一致する要素・インデックスを返すこと。
- `CurrentPage` が `null` または `Pages` に存在しない場合、`CurrentPageItem` が先頭ページへフォールバックし、`CurrentPageIndex` が `-1` を返す従来仕様を維持していること。
- `Pages` コレクションの変更（追加・削除・クリア）後に `CurrentPageItem` / `CurrentPageIndex` のキャッシュが正しく追従すること。
- `PageNumber` がインデックスと一致しない場合でもフォールバック探索で正しく取得できること。
- 連続表示時の幅フィット（`CheckVerticalScrollOverflow` 経由）で、垂直スクロール発生時・非発生時のズーム計算結果が従来通り正確であること。

---

## 検証手順
1. `dotnet test` を実行し、新規追加テストを含む全単体テストが 100% PASS することを確認する。
2. `dotnet build` を実行し、警告・エラーがないことを確認する。
