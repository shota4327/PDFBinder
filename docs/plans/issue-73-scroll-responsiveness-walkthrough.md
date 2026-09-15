# Issue #73 スクロール・ページ送りの反応遅延改善 検証報告

## 概要
詳細エディタの単一ページ表示モードにおいて、マウスホイールを素早く回した際に反応が遅れたり入力が間引かれていた問題（Issue #73）に対し、従来の250ms固定クールダウンを撤廃し、**Delta値累積方式（`WheelPageTurnTracker`）**を導入して高速回転時の即時追従性を向上させました。

---

## 主な変更点

### 1. `WheelPageTurnTracker` の新設
- `src/PDFBinder.App/Helpers/WheelPageTurnTracker.cs`:
  - マウスホイールの回転量（Delta）累積、タイムアウト判定、ページめくり数計算をカプセル化した独立ヘルパークラス。
  - Windows標準の1ノッチ値（120）を閾値とし、閾値到達ごとに即座に1ページ送り要求を返却。
  - 250msのアイドル時間経過時の端数リセット、逆方向スクロール時のリセット、境界以外（拡大時のページ内スクロール中）でのリセットに対応。

### 2. `DetailEditorView` のホイール処理刷新
- `src/PDFBinder.App/Views/DetailEditorView.xaml.cs`:
  - `PageTurnCooldown` による固定時間（250ms）の間引き処理を撤廃。
  - `WheelPageTurnTracker` を用いて、回転量に応じた即時ページめくりを実行。
  - メソッド行数目安（30〜50行）を遵守し、`OnScrollViewerPreviewMouseWheel` と `ExecutePageTurns` に処理を分割。

### 3. 単体テストの追加
- `tests/PDFBinder.Tests/WheelPageTurnTrackerTests.cs`:
  - 1ノッチ（120 / -120）による通常ページめくり判定
  - 高速連続スクロール（短時間での複数回回転）での即時追従判定
  - 1イベントでの複数ノッチ（一括 -240）処理
  - 微小Delta（トラックパッド等の40刻み）の累積到達判定
  - 250msアイドル経過による端数リセット判定
  - 逆回転検知時の累積値リセット判定
  - ページ内スクロール中（`isAtEdge = false`）でのリセット判定
  - `Reset()` の完全初期化動作

### 4. ドキュメントの更新
- `docs/basic_design.md`: 単一ページ表示におけるホイールDelta累積および高速追従仕様の追記
- `docs/PROJECT.md`: 機能インベントリ（F39）の更新およびテスト件数（195件）の反映
- `README.md`: 主な機能の単一ページ表示説明に高速ホイール追従を追記

---

## 検証結果

### 1. ビルド検証
- `dotnet build`: 警告0、エラー0でビルド成功。

### 2. 単体テスト検証
- `dotnet test`: **195件全テスト合格（0失敗）**。
  ```
  成功!   -失敗:     0、合格:   195、スキップ:     0、合計:   195、期間: 1 s - PDFBinder.Tests.dll (net10.0)
  ```
