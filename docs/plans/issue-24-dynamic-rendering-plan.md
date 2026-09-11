# Issue #24 詳細ビューにおけるズーム連動動的レンダリング実装計画

## 1. 概要と目的
詳細ビュー（手書きエディタ）において、固定高解像度ビットマップの拡大縮小表示（現行方式）から、現在のズーム倍率および画面DPIに連動した**オンデマンド・デバウンス動的レンダリング（方式B）**へと刷新します。
これにより、縮小表示時の「細い線の消失」「文字のかすれ」および大幅拡大時の「ぼやけ」を根本から解消します。

同時に、ユーザーからの要請に基づき、**「高速連続ズーム操作時のチラつき（フリッカー）」**および**「非同期処理の競合（レースコンディション）」**を徹底的に防止する堅牢なアーキテクチャを実装します。

---

## 2. 課題と設計方針

### 2.1 縮小表示時の文字かすれ・細線消失の解消
- **原因**: 3倍（216 DPI）の高解像度ビットマップをWPFのGPUパイプライン（バイリニア補間）で縮小表示していたため、1ピクセル未満の線が間引きされて消失し、文字のコントラストが低下していた。
- **対策**: 現在の `Zoom` 倍率に応じたジャストサイズを計算し、PDFium（Docnet.Core）に直接ラスタライズさせる。PDFium内のSkiaベクターエンジンがフォントヒンティングと細線保護（Thin-line handling）を適用するため、縮小表示時でもくっきりとクリアに描画される。

### 2.2 高速操作時のチラつき（フリッカー）防止対策
- **ダブルバッファリング**: 新しいビットマップがバックグラウンドで完全に生成され、`Freeze()` されるまで既存の `PageBackground` を絶対に破棄（nullクリア）しない。
- **高品質補間（`BitmapScalingMode.HighQuality`）**: `DetailEditorView.xaml` の `<Image>` に `RenderOptions.BitmapScalingMode="HighQuality"` を指定。操作中の拡大縮小でもバイキュービック等の滑らかな補間を効かせ、新画像への切り替え時の視覚的落差を最小限に抑える。

### 2.3 非同期競合（レースコンディション）防止対策
- **CancellationTokenSource による先行タスクの即時キャンセル**: ズーム変更が連続して発生した際、以前のリクエストを即座にキャンセル（`Cancel()` & `Dispose()`）。
- **世代番号（Generation Tracking）による追い越し防止**: スレッド安全なインクリメント値（`_renderGeneration`）を各リクエストに付与し、最新のリクエストの結果のみが `PageBackground` に反映されることを保証。古いリクエストが遅延完了しても無視・破棄する。
- **デバウンス制御（150ms）**: 連続操作中は再描画タスクの起動を遅延させ、ユーザーが指を止めた瞬間にのみレンダリングを実行（CPU・VRAM消費の抑制）。

---

## 3. 変更対象コンポーネントと詳細設計

### 3.1 [`IPdfRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/IPdfRenderer.cs) / [`PdfiumRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs)
- `RenderPageAsync` メソッドに `CancellationToken cancellationToken = default` 引数を追加。
- タスク開始時および重い処理の前後に `cancellationToken.ThrowIfCancellationRequested()` を配置し、キャンセル時に速やかに中断する。

### 3.2 [`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- ズーム倍率変更時のプログレッシブレンダリング基盤を追加：
  - `_renderCts`: 現在実行中のレンダリングキャンセル用トークンソース
  - `_renderGeneration`: レンダリングの世代番号（競合防止用）
  - `DebounceDelayMs`: デバウンス待機時間（デフォルト 150ms。テスト時は 0ms 等に変更可能）
  - `OnZoomChanged(double value)`: ズーム変更時にデバウンスタイマーを開始。
  - `ScheduleDynamicRender(bool immediate)`: デバウンスまたは即時レンダリングのスケジューリング。
  - `CalculateRenderDimensions()`: 現在の `Zoom`、`DisplayWidth`、`DisplayHeight`、およびDPIスケールから最適なピクセル寸法を算出（下限200px、上限4096pxの安全ガード付き）。
  - `Dispose()` またはクリーンアップ処理によるリソースの安全解放。

### 3.3 [`DetailEditorView.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- 背景 `<Image>` に `RenderOptions.BitmapScalingMode="HighQuality"` を指定。

### 3.4 単体テスト追加
- [`DetailEditorViewModelTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs) を新規作成：
  - 初期表示時のレンダリング検証
  - ズーム変更時のデバウンス動作検証
  - 連続ズーム操作時のキャンセル・世代管理（レースコンディション防止）の検証
  - チラつき防止（ダブルバッファリングで途中に null にならないこと）の検証

---

## 4. 検証計画

### 4.1 自動テスト
- `dotnet test`: 既存テストおよび新規作成した `DetailEditorViewModelTests` がすべて 100% PASS することを確認。

### 4.2 ビルド検証
- `dotnet build`: 警告・エラーなく成功することを確認。

### 4.3 動作確認項目
- 詳細エディタで 50% などの縮小表示にした際、細線が消失せず文字がくっきり描画されること。
- 拡大操作時、操作停止後に自動的に鮮明化すること。
- マウスホイールやボタンの高速連打時にも画面が白く点滅（フリッカー）せず、滑らかに最新倍率へ移行すること。
