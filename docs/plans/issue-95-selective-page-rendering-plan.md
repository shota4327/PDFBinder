# Issue #95 選択的ページ動的レンダリング（メモリ削減・表示高速化）実装計画

## 1. 概要と背景
現在、詳細ビュー（手書きエディタ）では、ズーム倍率変更時やウィンドウリサイズ時、初回読み込み時において、ドキュメント内の**全ページ（`Pages` コレクションの全アイテム）**をループして順次レンダリングしています。
このため、ページ数が多いPDFファイルでは以下の課題がありました：
1. **メモリ（RAM/VRAM）消費の増大**: 画面に表示されていないページまで高解像度ビットマップが生成・保持される。特に大幅拡大時（600%超〜最大3200%）に膨大なメモリを消費する。
2. **CPU/GPU負荷と完了遅延**: ユーザーが閲覧していないページのラスタライズに処理時間が奪われる。

本改修では、表示モードおよびズーム倍率に応じて**レンダリング対象ページを必要最小限に絞り込む（選択的レンダリング）**とともに、**初回読み込み時は最大10ページまでを先行レンダリング**し、11ページ以降は必要となったタイミングでのみオンデマンド生成することで、メモリ消費量を劇的に抑制し、初期表示およびズーム・ページ送りのレスポンスを大幅に向上させます。

---

## 2. 確定仕様（ユーザーインタビュー結果）

1. **ドキュメント初回読み込み時（新規オープン時）**:
   - **最大10ページ分（1〜10ページ）を先行レンダリング**:
     - 最優先で現在ページ（通常は1ページ目）を即時レンダリングし、その後残りの2〜10ページを順次レンダリングする。
   - **11ページ目以降**:
     - 初回読み込み時にはレンダリングを行わず、ユーザーがスクロールまたはページ送りで表示対象としたタイミングでのみレンダリングする。
2. **単一ページ表示モード（SinglePage）**:
   - **ズーム倍率 600% 以下（`Zoom <= 6.0`）**:
     - 現在ページ（`CurrentPage`）＋ 前後1ページ（`index - 1`, `index + 1`）のみをレンダリングする（現在ページ最優先）。
   - **ズーム倍率 600% 超（`Zoom > 6.0`）**:
     - 現在表示中のページ（`CurrentPage`）のみをレンダリングする（前後ページは除外）。
3. **連続スクロール表示モード（Continuous）**:
   - ズーム倍率に関わらず、現在スクロールビューアの表示領域（ビューポート）に交差している**「画面に見えている（可視）ページのみ」**をレンダリングする（画面中央に最も近いページ最優先）。
4. **非表示ページ（対象外ページ）の画像・メモリ保持方針**:
   - 非表示ページが過去に生成した背景画像（`PageBackground`）およびストロークキャッシュ（`StrokeCache`）は**クリア（null化）せず、そのままメモリ上に保持**する。
   - ページ送りやスクロールで新しく表示された際、新しい解像度での再生成が完了するまでは既存の画像をプレビューとして維持（ダブルバッファリング）し、白抜けやチラつきを完全に防止する。
5. **レンダリングのトリガーとタイミング**:
   - **ズーム倍率変更 / ウィンドウサイズ変更時**: 従来通り **150ms デバウンス**
   - **ページ送り（単一表示）時**: 新しいページへの遷移時は **即時（immediate）** レンダリング
   - **スクロール（連続表示）時**: スクロール中は **150ms デバウンス**（スクロール停止時に可視ページをレンダリング）
   - **ページ回転時**: 対象ページを即時レンダリング
6. **レンダリング優先順序と重複スキップ**:
   - 現在の対象ページの中で `CurrentPage`（またはビューポート中央に最も近いページ）を最優先で最初にレンダリングし、その後隣接・周辺ページをレンダリングする。
   - すでに現在の目標解像度（幅・高さ）および回転角度でレンダリング済みのページは、重複してラスタライズを行わずスキップする。

---

## 3. 影響範囲と設計方針

### 3.1 [`DetailPageItemViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailPageItemViewModel.cs)
- レンダリング済み状態を追跡するプロパティを追加：
  - `LastRenderedWidth`: 最後にレンダリングした画像幅
  - `LastRenderedHeight`: 最後にレンダリングした画像高さ
  - `LastRenderedRotation`: 最後にレンダリングした回転角度
- ヘルパーメソッド `bool IsRenderedAt(int targetWidth, int targetHeight, PageRotation rotation)` を提供し、再レンダリングが必要か判定可能にする。

### 3.2 [`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- **対象ページ抽出メソッド `GetTargetPagesToRender(bool isInitialLoad = false)` の追加**:
  - `isInitialLoad == true` の場合:
    - `Pages.Take(10)` を対象とする。
    - `CurrentPageItem` を先頭に配置し、残りのページ（2〜10ページ目）を後続に並べたリストを返す。
  - `isInitialLoad == false` の場合:
    - **SinglePage**:
      - `Zoom <= 6.0`: `CurrentPageItem` を先頭に、`index - 1`、`index + 1` の順でリストアップ。
      - `Zoom > 6.0`: `CurrentPageItem` のみをリストアップ。
    - **Continuous**:
      - `VisiblePagesProvider?.Invoke()`（Viewから提供されるデリゲート）を呼び出し、ビューポート内の可視ページ一覧を取得（フォールバックは `CurrentPageItem`）。
      - `CurrentPageItem` を先頭にし、残りの可視ページを順次レンダリング。
- **初回ロード制御 `LoadInitialDocumentBackgroundAsync()` の追加**:
  - `InitializeDocument()` の最後で `isInitialLoad: true` を指定して動的レンダリングを即時（immediate）起動。
- **連続スクロール用デバウンス制御 `ScheduleContinuousScrollRender()` の追加**:
  - スクロール時に150msデバウンスで可視ページレンダリングをスケジュール。
- **ページ変更ハンドラ `OnCurrentPageChanged` の改修**:
  - 単一表示モードでカレントページが切り替わった際、新ページおよび前後ページを `immediate: true` で即時レンダリング。
- **`PerformDynamicRenderAsync` の改修**:
  - `Pages.ToList()` の全件ループを `GetTargetPagesToRender(isInitialLoad)` に変更。
  - 既に同一寸法・回転でレンダリング済みのページは無駄な再レンダリングをスキップ。

### 3.3 [`DetailEditorView.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml.cs)
- ViewModel の `VisiblePagesProvider` に `GetVisiblePagesInViewport` を登録。
- `GetVisiblePagesInViewport()`:
  - `PagesItemsControl` の各コンテナの `TransformToVisual(DetailScrollViewer)` 座標を調べ、ビューポート高さ `[0, ViewportHeight]` と交差しているページを抽出。
- `OnScrollViewerScrollChanged`:
  - 連続表示モードでスクロール発生時、`ViewModel.ScheduleContinuousScrollRender()` を呼び出し。

---

## 4. 単体テスト計画

[`DetailEditorViewModelTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorViewModelTests.cs) に以下のテストケースを追加・検証：
1. **初回読み込み時の対象ページ判定（10ページ制限）**:
   - 20ページのドキュメントを初期化した際、対象ページが先頭10ページのみ（先頭はカレントページ）であり、11〜20ページ目はレンダリング対象に含まれないこと。
2. **単一表示モード（600%以下）の対象ページ判定**:
   - 15ページあるドキュメントで、ページ12を表示中かつZoom=2.0のとき、対象ページが「ページ12、ページ11、ページ13」の3ページのみであること。
3. **単一表示モード（600%超）の対象ページ判定**:
   - Zoom=7.0のとき、対象ページが「ページ12」のみであること。
4. **連続表示モードの対象ページ判定**:
   - `VisiblePagesProvider` から返された可視ページのみがレンダリング対象となること。
5. **重複レンダリングのスキップ検証**:
   - 同一倍率・回転でレンダリング済みのページに対して再度レンダリングを実行した際、`IPdfRenderer.RenderPageAsync` の呼び出しがスキップされること。
6. **非表示ページの背景画像保持の検証**:
   - 対象外となったページが以前持っていた `PageBackground` が破棄されず維持されること。

---

## 5. 検証手順

1. `dotnet test` により全テストが 100% PASS することを確認。
2. `dotnet build` により警告・エラーなくビルドが成功することを確認。
