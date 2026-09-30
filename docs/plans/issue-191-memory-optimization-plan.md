# Issue #191 メモリ管理・リソース解放の最適化とリーク解消 実装計画書

## 1. 概要
ドキュメントクローズ時のメモリ高止まり、およびページ閲覧・操作時のメモリ急増を抑制・最適化するため、以下の施策を安全に実装する。

1. **提案1: 詳細エディタの動的背景アンロード（LRU / ウィンドウ方式）**
   - 単一ページ表示時: カレントページの前後3ページ（計最大7ページ）を保持し、範囲外の背景画像（`PageBackground`）をアンロード。
   - 連続スクロール表示時: 可視ページ群 ＋ その外側前後3ページを保持。
   - 手書きストロークが存在するページは `StrokeCache` を維持して線画消失を防止。
   - 事前レンダリングはカレントページおよび直近（前後1ページ）を優先。
2. **提案2: ドキュメントクローズ時の明示的リソース解放とリーク解消**
   - `DocumentSession : IDisposable` の実装と各ページビットマップの明示的解放。
   - `GridView._initialSelection`（矩形選択ページの強参照保持リーク）の `.Clear()`。
   - `InteractiveOverlayCanvas` および `EditorInkCanvas` のイベント購読対称化。
   - ファイル・タブを閉じるごとにバックグラウンド Full GC（`GC.Collect(2, GCCollectionMode.Forced, false)`）を実行。
3. **提案3: サムネイル解像度の適正化と先行生成数の整合**
   - サムネイル生成解像度を `480 × 672 px`（現行 720×1008 から 55.5% 削減、4K/200% DPI でもドットバイドット鮮明）に変更。
   - サムネイル先行生成数は現状維持（初期50ページ、詳細前後10ページ）。
   - 詳細エディタの初期高解像度先行生成数を `InitialLoadMaxPageCount = 4`（先頭4ページ）に整合。
4. **追加対応: エージェント作業ディレクトリの `.gitignore` 登録**
   - `.agents/` および `.agent/` ディレクトリを `.gitignore` に追加登録し、自動生成される引き継ぎ情報等のバージョン管理混入を防止。

---

## 2. 変更対象コンポーネントと詳細設計

### 2.1 提案2: クローズ時リソース解放・リーク解消
1. **`DocumentSession.cs`**:
   - `IDisposable` を実装。
   - `Dispose()` にて `Document.PropertyChanged -= OnDocumentPropertyChanged;` を解除し、`UndoRedoService.Clear()` を呼び出し、各ページの `Thumbnail = null` を解放。
2. **`DetailPageItemViewModel.cs`**:
   - `Dispose()` を拡充し、`Page.PropertyChanged -= OnPagePropertyChanged;`、`UnloadBackground()`、`InteractiveData = null;`、`PageJumpRequested = null;` を確実に実行。
3. **`DetailEditorViewModel.cs`**:
   - `ClearPageItems()` にてコレクションクリア前に各アイテムの `item.Dispose()` を確実に実行。
4. **`MainViewModel.cs`**:
   - `CloseDocumentAsync` の解放フロー厳格化:
     1. `await CancelAndAwaitThumbnailsAsync();` で進行中タスクの中断・待機。
     2. `Documents.Remove(target);`
     3. `ActiveSession` の安全な切り替え（新セッションまたは `null`）。
     4. `target.Dispose();` の呼び出し。
     5. 全ドキュメントが 0 件になった場合は `DetailEditor?.InitializeDocument(new PdfDocumentModel())` でキャンバス初期化。
     6. タスク終了後に `Task.Run(() => GC.Collect(2, GCCollectionMode.Forced, false))` を実行し、閉じたドキュメントのヒープを回収。
5. **`GridView.xaml.cs`**:
   - `OnGridPreviewMouseLeftButtonUp` にて `_initialSelection.Clear();` を実行し、矩形選択ページの永続リークを解消。
6. **`InteractiveOverlayCanvas.cs` & `EditorInkCanvas.cs`**:
   - `InteractiveOverlayCanvas`: `OnLoaded` / `OnUnloaded` での二重購読を撤廃し、`OnPageItemChanged` のみで PropertyChanged 購読を一元管理。
   - `EditorInkCanvas`: `Unloaded` 時に `PageItem.Page.InkStrokes.StrokesChanged` の購読を確実に解除。

### 2.2 提案1: 詳細エディタの動的背景アンロード
1. **`DetailPageItemViewModel.cs`**:
   - `UnloadBackground()` メソッドを追加:
     - `PageBackground = null;`
     - 手書きが存在しない（`Page.InkStrokes.Count == 0`）場合のみ `StrokeCache = null;`（線画維持）。
     - `LastRenderedWidth = 0; LastRenderedHeight = 0; LastRenderedRotation = PageRotation.Rotate0;`
2. **`DetailEditorViewModel.cs`**:
   - `EvictOffscreenPageBackgrounds()` メソッドを追加:
     - 単一ページ表示時: `CurrentPageIndex` の前後3ページ（計最大7ページ）を保持集合とする。
     - 連続スクロール表示時: `VisiblePagesProvider` の可視ページ群 ＋ その外側前後3ページを保持集合とする。
     - 保持集合外で `PageBackground != null` の全アイテムに対して `item.UnloadBackground()` を呼び出す。
   - `PerformDynamicRenderAsync` 完了時、およびページ切り替え時に `EvictOffscreenPageBackgrounds()` を呼び出す。
   - 事前レンダリングは従来通り `GetSinglePageModeTargetPages`（カレント＋前後1ページ）を優先。

### 2.3 提案3: サムネイル解像度と先行生成数の適正化
1. **`MainViewModel.cs`**:
   - `ThumbnailRenderWidth = 480;`（720から変更）
   - `ThumbnailRenderHeight = 672;`（1008から変更）
   - `InitialPreloadThumbnailPageCount = 50;`（現状維持）
   - `DetailViewThumbnailWindowRadius = 10;`（現状維持）
2. **`DetailEditorViewModel.cs`**:
   - `InitialLoadMaxPageCount = 4;`（10から4へ変更、前後3ページ保持枠と整合）
3. **`tests/PDFBinder.Tests/ViewModelsTests.cs`**:
   - `MainViewModel_ThumbnailRenderConstants_AreConfiguredProperly` のアサート値を `480` および `672` に更新。

### 2.4 環境・構成管理の改善
1. **`.gitignore`**:
   - `.agents/` および `.agent/` を追加し、マルチエージェント作業履歴の自動除外を設定。

---

## 3. テスト計画

### 3.1 単体テスト（新規追加および更新）
- `DetailEditorDynamicEvictionTests.cs`:
  - 単一表示モードで4ページ以上離れたページの `PageBackground` がアンロードされること。
  - 前後3ページ以内のページは `PageBackground` が保持されること。
  - 手書きストロークが存在するページはアンロード時も `StrokeCache` が維持されること。
  - 連続スクロールモードで可視ページ群＋前後3ページが保護されること。
- `DocumentResourceCleanupTests.cs`:
  - `CloseDocumentAsync` 実行後に `DocumentSession` が Dispose され、各ページの `Thumbnail` が null 化されること。
  - `GridView` のラバーバンド操作後に `_initialSelection` がクリアされていること。
  - `DetailEditorViewModel.ClearPageItems` 実行時に `DetailPageItemViewModel.Dispose` が呼び出されること。
- 既存テストの更新:
  - `ViewModelsTests.cs`: サムネイル解像度定数のアサート更新。

### 3.2 ビルドおよびリグレッション検証
- `dotnet test`: 既存 539 件 ＋ 新規テストがすべて 100% PASS すること。
- `dotnet build`: 警告・エラー 0 件で成功すること。

---

## 4. ドキュメント更新計画
- `docs/basic_design.md`: メモリ最適化方針、サムネイル解像度、動的エビクションの記述追加・更新。
- `Directory.Build.props`: パッチバージョンインクリメント（`0.3.0` $\rightarrow$ `0.3.1`）。
- `CHANGELOG.md`: エンドユーザー向けリリースノートの追記（`[0.3.1] - 2026-09-29`）。
