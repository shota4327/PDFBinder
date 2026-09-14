# Issue #65: ウィンドウ合わせ・幅合わせのスクロールバー表示不具合修正 Walkthrough

## 概要
詳細エディタビュー（`DetailEditorView`）において、単一ページ表示で「ウィンドウにあわせる（FitToWindow）」を選択した際にもスクロールバーが表示されてしまう問題、および「幅にあわせる（FitToWidth）」選択時に縦スクロールバー出現によって水平スクロールバーが誘発される不具合を修正しました。
また、連続表示モードにおける先頭ページの上部余白および最終ページの下部余白を除去し、ページ間の余白のみが均一（30px）に保たれるよう余白構造を最適化しました。

---

## 変更内容

### 1. View（XAML）の余白構造の分離と最適化
- **[`DetailEditorView.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
  - `DetailPageItemTemplate` のルート要素にハードコードされていた `Margin="0,15"` を削除。
  - 連続表示用の `PagesItemsControl` の `ItemContainerStyle` を設定し、`Setter Margin="0,0,0,30"` を基本としつつ、`IsLastPage == True` の場合は `Margin="0"` に切り替えるトリガーを設定。
  - **効果**:
    - 単一ページ表示時は外側 `DetailScrollViewer` の `Padding="30"` のみとなり、上下左右 30px の均等な余白を実現。
    - 連続表示時も、先頭ページの上部余白および最終ページの下部余白が重複せず外側Padding（30px）のみとなり、中間ページ間のみが均一な 30px 間隔で配置される。

### 2. ViewModel 計算ロジック・ページ端点管理の改善
- **[`DetailPageItemViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailPageItemViewModel.cs)**:
  - `IsFirstPage` および `IsLastPage` プロパティを追加。
- **[`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)**:
  - `UpdatePageEdgeFlags()` を追加し、ドキュメント読み込み時に各ページの端点フラグ（先頭・末尾）を自動更新。
  - `ScrollBarWidth = 18.0`（スクロールバー幅見込み値）および `SafetyBuffer = 2.0`（WPFのレイアウト丸め誤差対策バッファ）の定数を導入。
  - `ApplyFitMode()` をリファクタリングし、`ApplyFitToWindow` / `ApplyFitToWidth` / `CheckVerticalScrollOverflow` に責務分割。
  - **FitToWindow**:
    - 利用可能領域（`Viewport - Padding - SafetyBuffer`）から `scale = Math.Min(scaleX, scaleY)` を計算し、縦横ともにスクロールバーが一切出現せず 1 画面内に完全に収まるように制御。
  - **FitToWidth**:
    - 拡大後のコンテンツ高さ（連続表示時は先頭・末尾余白を除外した `(N - 1) * 30.0` のページ間マージンで正確に計算）がビューポート高さを超えるかを判定。
    - 縦スクロールが発生する場合は `ScrollBarWidth` を差し引いた幅に合わせて倍率を算出し、水平スクロールバーの発生を防止。

### 3. ドキュメントの同期
- **[`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md)**: 表示オプション（FitToWindow / FitToWidth）のスクロールバー補正・セーフティバッファ仕様を追記。
- **[`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md)**: 機能インベントリ（F37, F50）を更新。

---

## 検証結果

### 自動テスト（xUnit）
- 実行コマンド: `dotnet test`
- 結果: **155件 全テスト PASS（成功: 155、失敗: 0、スキップ: 0）**
  - `FitMode_SinglePageMode_FitToWindow_FitsCompletelyInsideViewport`: 単一ページ表示時に余白を含めたコンテンツ寸法がビューポート内に完全に収まることを検証する新規テストを追加。
  - `InitializeDocument_SetsFirstAndLastPageFlagsCorrectly`: 先頭・最終ページフラグが正しく設定されることを検証する新規テストを追加。
  - 既存の表示モード・ズーム再計算テストも新ロジックに対応させてすべて通過。

### ビルド確認
- 実行コマンド: `dotnet build`
- 結果: **0 警告、0 エラーでビルド成功**
