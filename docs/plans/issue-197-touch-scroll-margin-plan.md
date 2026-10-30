# 実装計画: 詳細エディタの余白エリアにおけるタッチスクロール・ピンチズーム対応

- **Issue**: [#197 手によるスクロールを周りの余白エリアでもできるようにする](https://github.com/shota4327/PDFBinder/issues/197)
- **作成日**: 2026-10-30
- **ブランチ**: `issue-197-touch-scroll-margin`

---

## 1. 課題の概要と現状
詳細手書きエディタ（`DetailEditorView`）において、PDFページ上（`EditorInkCanvas` 内）では指での1本指ドラッグによるスクロール（パン）および2本指ピンチによる拡大・縮小（ズーム）が動作する。
しかし、ページの周囲にある余白エリア（マージン領域や連続表示時のページ間余白）を指でタッチ・操作しても、タッチイベントを受け取るハンドラーが存在しないため、スクロールおよび拡大・縮小が一切行われない。

## 2. /grill-me インタビューによる決定事項
1. **対象とする操作**:
   - タッチ操作（タッチスクリーンでの指1本ドラッグスクロール＆指2本ピンチ拡大縮小）を対象とする。
2. **対象画面**:
   - 詳細エディタビュー（`DetailEditorView`）のみを対象とする（余白領域での操作性を改善）。
3. **境界を跨ぐジェスチャー**:
   - ページ上と余白エリアを問わずシームレスに統一動作させる（片方の指がページ内、もう片方が余白にある場合でもピンチズーム可能、1本指パンもシームレス）。
4. **端到達時の動作**:
   - 既存のページ内タッチ操作と同様、純粋なスクロール（パン）動作とし、端到達での自動ページめくりは行わない（誤動作防止）。
5. **パームリジェクション連携**:
   - スタイラスペン使用時（ペン描画中・近接ホバー中・クールダウン中）は、余白エリアのタッチ操作も抑制し、筆記時の手のひら接地による画面の誤スクロールを防止する。

## 3. アーキテクチャと設計方針
### 3.1 タッチ追従コントローラー（`DetailTouchTracker`）の新設
- タッチデバイスIDごとの現在座標、開始座標、スロップ判定（`TouchSlopThreshold = 8.0px`）、ピンチ距離・中心点計算、およびスタイラスパームリジェクション状態を管理する専用クラス `DetailTouchTracker` を `PDFBinder.App.Helpers`（または `Controls`）に新設。
- `PinchZoomHelper.Calculate` を活用し、2本指ピンチズーム時の新しい倍率およびスクロールオフセットを正確に計算。
- スタイラスペン検知（`StylusDown`, `StylusMove`, `StylusInRange`, クールダウン時間 150ms）によるパームリジェクション判定を一元化。

### 3.2 `DetailEditorView` でのタッチイベント統合
- `DetailScrollViewer` レベルで `PreviewTouchDown`, `PreviewTouchMove`, `PreviewTouchUp`, `TouchLeave`, `LostTouchCapture` および `PreviewStylusDown`, `StylusInRange`, `StylusOutOfRange` をハンドリング。
- `DetailScrollViewer` はページ全体および周囲余白を含む親コンテナであるため、余白エリアでのタッチもページ上でのタッチもすべて統合して追従。
- 指操作時は `e.Handled = true` を設定して不要なインク描画やマウス昇格を防止。
- スタイラスペン操作時はイベントを消費せず子要素（`EditorInkCanvas`）へ通過させ、滑らかなペン描画を保証。

## 4. 変更対象ファイル
1. **新規作成**:
   - `src/PDFBinder.App/Helpers/DetailTouchTracker.cs`: タッチパン・ピンチズーム・パームリジェクション計算ロジック
   - `tests/PDFBinder.Tests/DetailTouchTrackerTests.cs`: 余白エリア操作・ページ跨ぎピンチ・パームリジェクションの単体テスト
2. **修正**:
   - `src/PDFBinder.App/Views/DetailEditorView.xaml.cs`: `DetailScrollViewer` へのタッチイベント購読およびスクロール・ズーム連携
   - `Directory.Build.props`: バージョンインクリメント（`0.15.0` → `0.16.0`）
   - `CHANGELOG.md`: エンドユーザー向けリリースノートの追記

## 5. 品質・検証計画
1. `dotnet test`: すべての単体テスト（新規追加テスト含む）が 100% PASS することを確認。
2. `dotnet build`: 警告やエラーなくビルドが成功することを確認。
