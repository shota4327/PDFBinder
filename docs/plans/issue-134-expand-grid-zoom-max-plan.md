# 実装計画 - Issue #134: グリッド表示の拡大率上限拡大

## 1. 概要
グリッド表示（バインダー概要）におけるサムネイル表示の拡大率上限・下限・ズームステップを、詳細ビュー（ページ編集）の仕様（50%〜3200%）と完全に統一します。
また、高倍率拡大時でもサムネイルが鮮明に表示されるよう事前レンダリング解像度を倍増（720x1008）し、直感的な拡大縮小操作のために「Ctrl + マウスホイール」によるズームイン・ズームアウト操作を追加します。

## 2. 合意事項（インタビューおよびフィードバック結果）
1. **拡大率上限**: 詳細ビューと完全に同じ 3200%（32倍、基準220px換算で最大7,040px）。
2. **最小拡大率（下限）**: 詳細ビューと統一し 50%（0.5倍、基準220px換算で110px）。
3. **ズームステップ**: 詳細ビューと同一の目盛りステップ一覧（50%〜3200%）に沿って段階的に拡大・縮小。
4. **サムネイル解像度**: 高倍率拡大時でも鮮明な表示を保つため、サムネイル生成解像度を倍増（`ThumbnailRenderWidth = 720`, `ThumbnailRenderHeight = 1008`）。
5. **操作インターフェース**:
   - キーボード（Ctrl++, Ctrl+-, Ctrl+0）およびリボン／ステータスバーのズームボタン操作をサポート。
   - **【追加】グリッド表示での「Ctrl + マウスホイール」によるズームイン・ズームアウトに対応**（Ctrl+上回転で拡大、Ctrl+下回転で縮小）。※詳細ビュー（DetailEditorView）側にも同様に Ctrl+Wheel ズーム処理を追加し、アプリ全体で一貫した操作感を提供。
6. **共通ヘルパー設計**: ズームステップ一覧および目盛り計算ロジックを `ZoomHelper` に集約し、`DetailEditorViewModel` と `MainViewModel` から再利用する。

---

## 3. 変更対象ファイルと詳細設計

### 3.1 [NEW] `src/PDFBinder.App/Helpers/ZoomHelper.cs`
- ズーム関連の定数および目盛り遷移ロジックを共通化:
  - `MinZoom = 0.5`（50%）
  - `MaxZoom = 32.0`（3200%）
  - `ZoomSnapSteps`: `[0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 3.5, 4.0, 5.0, 6.0, 7.0, 8.0, 10.0, 12.0, 14.0, 16.0, 20.0, 24.0, 28.0, 32.0]`
  - `GetNextZoomIn(double currentZoom)`: 1段階拡大した次の目盛り倍率を返却（上限 `MaxZoom`）。
  - `GetNextZoomOut(double currentZoom)`: 1段階縮小した前の目盛り倍率を返却（下限 `MinZoom`）。

### 3.2 [MODIFY] `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- `MinZoom`, `MaxZoom`, `ZoomSnapSteps`, `GetNextZoomIn`, `GetNextZoomOut` を `ZoomHelper` を参照または委譲する形にリファクタリング。
- 既存の外部公開定数・メソッドは互換性のために `ZoomHelper` の参照プロパティ／フォワードメソッドとして維持。

### 3.3 [MODIFY] `src/PDFBinder.App/ViewModels/MainViewModel.cs`
- サムネイルサイズ定数および計算ロジックの改修:
  - `DefaultThumbnailSize = 220.0`
  - `MinThumbnailSize = DefaultThumbnailSize * ZoomHelper.MinZoom` (110.0px)
  - `MaxThumbnailSize = DefaultThumbnailSize * ZoomHelper.MaxZoom` (7040.0px)
  - `ThumbnailRenderWidth = 720` (360から倍増)
  - `ThumbnailRenderHeight = 1008` (504から倍増)
- `CanZoomInThumbnail`: `ThumbnailSize < MaxThumbnailSize - 0.01`
- `CanZoomOutThumbnail`: `ThumbnailSize > MinThumbnailSize + 0.01`
- `ZoomInThumbnail()`:
  - 現在の倍率 `currentZoom = ThumbnailSize / DefaultThumbnailSize` を算出。
  - `nextZoom = ZoomHelper.GetNextZoomIn(currentZoom)` を取得。
  - `ThumbnailSize = Math.Round(nextZoom * DefaultThumbnailSize, 2)` に設定。
- `ZoomOutThumbnail()`:
  - 現在の倍率 `currentZoom = ThumbnailSize / DefaultThumbnailSize` を算出。
  - `nextZoom = ZoomHelper.GetNextZoomOut(currentZoom)` を取得。
  - `ThumbnailSize = Math.Round(nextZoom * DefaultThumbnailSize, 2)` に設定。

### 3.4 [MODIFY] `src/PDFBinder.App/Views/GridView.xaml` & `GridView.xaml.cs`
- `GridScrollViewer`（または `GridView`）に `PreviewMouseWheel` イベントハンドラーを追加。
- `Keyboard.Modifiers.HasFlag(ModifierKeys.Control)` を判定し、Ctrlキー押下時は通常スクロールを抑制（`e.Handled = true`）して `ViewModel.ZoomInThumbnailCommand` / `ZoomOutThumbnailCommand` を実行。

### 3.5 [MODIFY] `src/PDFBinder.App/Views/DetailEditorView.xaml.cs`
- 既存の `OnScrollViewerPreviewMouseWheel` 内で `Keyboard.Modifiers.HasFlag(ModifierKeys.Control)` を判定し、Ctrlキー押下時はページ送りではなく `ViewModel.ZoomInCommand` / `ZoomOutCommand` を実行して `e.Handled = true` とする（グリッド・詳細ビュー双方の操作感統一）。

### 3.6 [NEW / MODIFY] 単体テスト
- [NEW] `tests/PDFBinder.App.Tests/Helpers/ZoomHelperTests.cs`
  - `GetNextZoomIn`, `GetNextZoomOut` の境界値、ステップ進行、上限（3200%）、下限（50%）の検証。
- [MODIFY] `tests/PDFBinder.App.Tests/ViewModels/MainViewModelTests.cs`
  - グリッド表示のズームイン・ズームアウト・リセットの動作検証（110px〜7040px、ステップごとのサイズ遷移）。

### 3.7 [MODIFY] ドキュメント更新
- `docs/basic_design.md`: グリッド表示のズーム範囲（50%〜3200%）、サムネイル解像度（720x1008）、Ctrl+Wheel ズーム操作の記載を更新。

---

## 4. 検証計画

### 4.1 自動テスト
- `dotnet test` を実行し、既存テストおよび新規作成テストがすべて PASS することを確認。

### 4.2 ビルド確認
- `dotnet build` を実行し、警告およびエラーなく正常にコンパイルが完了することを確認。
