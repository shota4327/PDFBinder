# Issue #60 拡大率上限の拡張（300%から3200%）実装計画

## 概要
詳細エディタにおける拡大率の上限を現在の 300%（3.0倍）から 3200%（32.0倍）へ拡張します。
これに伴い、高倍率時の操作性を考慮した適応型ズームステップ（スナップ方式）の導入、ピンチズーム上限の拡張、および背景PDF動的レンダリングの最大寸法（MaxRenderDimension）を 8192px へ拡張します。

## 決定事項（インタビュー結果）
1. **最大ズーム倍率**:
   - `DetailEditorViewModel.MaxZoom`: `3.0` (300%) → `32.0` (3200%)
   - `PinchZoomHelper.DefaultMaxZoom`: `3.0` (300%) → `32.0` (3200%)
2. **ズームイン・ズームアウトのステップ刻み**:
   - 標準スナップ目盛りリスト:
     `0.5 (50%), 0.75 (75%), 1.0 (100%), 1.25 (125%), 1.5 (150%), 1.75 (175%), 2.0 (200%), 2.5 (250%), 3.0 (300%), 3.5 (350%), 4.0 (400%), 5.0 (500%), 6.0 (600%), 7.0 (700%), 8.0 (800%), 10.0 (1000%), 12.0 (1200%), 14.0 (1400%), 16.0 (1600%), 20.0 (2000%), 24.0 (2400%), 28.0 (2800%), 32.0 (3200%)`
   - ズームイン時: 現在の倍率より大きく最も近い目盛りにスナップ（上限: 32.0）
   - ズームアウト時: 現在の倍率より小さく最も近い目盛りにスナップ（下限: 0.5）
3. **背景PDFレンダリング最大寸法**:
   - `DetailEditorViewModel.MaxRenderDimension`: `4096` → `8192`
   - 超高解像度（3200%）時でも極めて精細なビットマップテクスチャを生成可能にする。
4. **ドキュメントの更新**:
   - `docs/basic_design.md` のズーム倍率仕様を「50%〜3200%」に更新。

## 影響範囲と変更対象ファイル

### 1. `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- `MaxZoom = 32.0` への更新
- `MaxRenderDimension = 8192` への更新
- ズームスナップ計算用ヘルパーロジック（`ZoomSnapSteps` 定義および `GetNextZoomIn` / `GetNextZoomOut`）の実装
- `ZoomIn()` / `ZoomOut()` の適応型スナップロジックへの更新

### 2. `src/PDFBinder.App/Helpers/PinchZoomHelper.cs`
- `DefaultMaxZoom = 32.0` への更新

### 3. `docs/basic_design.md`
- ズーム倍率・表示仕様（50%〜3200%）の更新

### 4. `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs` & `ViewModelsTests.cs` & `PinchZoomHelperTests.cs`
- `ViewModelsTests.cs`: `vm.SetZoom(5.0)` クランプテストの更新（MaxZoomが32.0になったため、例えば `vm.SetZoom(40.0)` で `MaxZoom` クランプを確認）
- `PinchZoomHelperTests.cs`: `DefaultMaxZoom` クランプテストの更新
- `DetailEditorViewModelTests.cs`: 新たなズームステップ・スナップロジック、ZoomIn/ZoomOutのテストケース追加

## 検証計画
### 自動テスト
- `dotnet test` の実行（既存155件 + 新規テストケースの全合格）
- 境界値検証（0.50未満への縮小、32.00超過への拡大、端数ズーム値からのスナップ）
- `dotnet build` による警告・エラーなしの確認
