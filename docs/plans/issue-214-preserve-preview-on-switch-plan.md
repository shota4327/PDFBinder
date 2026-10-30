# Issue #214 実装計画: ファイル切り替え時のプレビューおよび表示設定の完全保持

## 1. 概要と目的
現在、複数ファイルを開いて作業している際、ファイル切り替えを行うと以下の課題が発生しています。
1. **切り替え遷移時の一瞬のページ飛び・誤表示**:
   - 切り替え元に戻ってきた際、直前まで見ていたファイルで表示していたページ番号（またはスクロール位置に対応するページ）が一瞬表示されてから、本来のページに戻るようなチラつき・引っ掛かりがある。
2. **拡大率の意図しないリセット（「ウィンドウに合わせる」への巻き戻り）**:
   - 一方のファイルで拡大（300%など）して作業していても、別のファイル（「ウィンドウに合わせる」状態）へ切り替えてから戻ると、手動拡大したはずのファイルも「ウィンドウに合わせる」サイズへリセットされてしまう。
3. **表示方法（連続表示・単一ページ表示）やプレビュー描画の再生成遅延**:
   - ファイルごとに単一ページ表示と連続表示を使い分けている場合や、レンダリング済み背景画像が切り替え時に破棄されるため、戻った際にプレビューが再描画されるまで引っ掛かりや待ち時間が発生する。

本改修では、**/grill-me** で決定した方針に基づき、**各 `DocumentSession` が固有の `DetailEditorViewModel` インスタンスを丸ごと保持する設計**へ刷新し、拡大率・表示モード（FitMode / PageViewMode）・正確なスクロール位置・高解像度プレビューキャッシュをファイル単位で完全に分離・保持することで、遅延・チラつきのない瞬時切り替え（完全0遅延プレビュー）を実現します。

---

## 2. 現状の構造と根本原因分析

### 2.1 単一の DetailEditorViewModel を全セッションで共用・再初期化している問題
- **現状**:
  - `MainViewModel` 内に単一の `DetailEditorViewModel`（`DetailEditor`）が存在し、ファイル切り替えのたびに `InitializeDocument` を呼び出して全ページの `DetailPageItemViewModel` を一旦破棄（`item.Dispose()` で高解像度背景画像を破棄）し、再生成しています。
  - そのため、切り替え元に戻るたびに高解像度レンダリングパイプラインを最初から走らせ直す必要が生じ、プレビューの表示に引っ掛かりが発生していました。

### 2.2 切り替え遷移中の ScrollViewer イベントによるカレントページ誤検知
- **現状**:
  - `DetailScrollViewer`（WPF）の `ScrollChanged` イベント内で `UpdateCurrentPageFromScroll()` が常時稼働しています。
  - ファイル切り替え時、`DetailEditorView` の DataContext または Pages が差し替えられる途中で、ScrollViewer に残存している直前のファイルのスクロールオフセット（例: 5000px）に対してページ判定が走り、切り替え元ファイルの該当オフセットにある別のページ（例: 8ページ目）が `ViewModel.CurrentPage` に誤代入されていました。
  - その後、遅延実行される `ScrollToPage` によって本来のページ（例: 3ページ目）へ戻されるため、「一瞬別のページが表示されてから戻る」チラつきが発生していました。

### 2.3 手動ズーム時の FitMode 状態遷移の未整備
- **現状**:
  - ピンチズームや一部のズーム操作において、`FitMode` が `FitToWindow` のまま `Zoom` 倍率のみが書き換えられる経路が存在していました。
  - その結果、ファイル切り替え時やウィンドウリサイズ時に「`FitMode == FitToWindow`」と判定され、せっかく拡大した倍率（300%など）がウィンドウサイズに合わせて強制的に再計算されてしまっていました。

---

## 3. 実装方針

### 3.1 DocumentSession ごとの DetailEditorViewModel 保持
1. **`DocumentSession` の拡張**:
   - `DocumentSession` に `DetailEditorViewModel DetailEditor { get; }` プロパティを追加。
   - `DocumentSession` 生成時に自身の `Document` 専用の `DetailEditorViewModel` を初期化・バインド。
   - セッション固有の正確なスクロール位置（`SavedVerticalOffset`, `SavedHorizontalOffset`）を保持。
   - `DocumentSession.Dispose()` 時に `DetailEditor.Dispose()` を呼び出し、安全かつ確実に画像リソースやイベントを解放。
2. **`MainViewModel` の責務軽量化**:
   - `MainViewModel.DetailEditor` は、`ActiveSession?.DetailEditor` を返すプロパティ（または ActiveSession 変更時に差し替え）とする。
   - ファイル切り替え時は `InitializeDocument` を呼び出すのではなく、単に `DetailEditor` の参照（DataContext）を切り替えるだけで完了させる。

### 3.2 手動ズーム時の FitMode 即時遷移の徹底
1. **`DetailEditorViewModel` のズーム操作の統一**:
   - `SetZoom(double zoom)` やピンチズーム（`EditorInkCanvas`）、拡大・縮小コマンド（`ZoomInCommand`, `ZoomOutCommand`）を含むすべての手動ズーム操作において、即座に `FitMode = DetailViewFitMode.None;` へ遷移させる。
   - これにより、ウィンドウリサイズやファイル切り替えが発生しても、設定された手動倍率（300%等）が完全に保護される。

### 3.3 切り替え時のスクロール誤検知抑止とピクセル単位の即時復元
1. **スクロール判定の一時抑止フラグの導入**:
   - `DetailEditorView` に切り替え中フラグ（`_isRestoringSession`）を導入。
   - DataContext 変更時、旧セッションの `DetailScrollViewer.VerticalOffset` / `HorizontalOffset` を旧 ViewModel に保存。
   - 新セッションの DataContext 適用時、`_isRestoringSession = true` として `OnScrollViewerScrollChanged` 内のカレントページ誤判定を遮断。
   - 新セッションの保持している `SavedVerticalOffset` / `SavedHorizontalOffset` を直ちに `DetailScrollViewer` に適用し、完了後に抑止を解除。
   - これにより、切り替え遷移中の一瞬のページ飛び・誤表示を完全に根絶。

---

## 4. 影響範囲
- `src/PDFBinder.App/Models/DocumentSession.cs`
- `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- `src/PDFBinder.App/ViewModels/MainViewModel.cs`
- `src/PDFBinder.App/Views/DetailEditorView.xaml.cs`
- `src/PDFBinder.App/Controls/EditorInkCanvas.cs`
- `tests/PDFBinder.Tests/ViewModels/MainViewModelMultiFileTests.cs`
- `tests/PDFBinder.Tests/ViewModels/DetailEditorViewModelTests.cs`

---

## 5. テスト計画
1. **単体テストの拡充**:
   - `MainViewModelMultiFileTests`:
     - ファイルA（連続表示・手動300%）とファイルB（単一表示・ウィンドウフィット）を行き来した際、それぞれの `PageViewMode`、`FitMode`、`Zoom` が一切崩れず完全に保持されることの検証。
     - ファイル切り替え時に旧ファイルのページ描画アイテム（`Pages`）が破棄されず、高解像度背景画像キャッシュがそのまま維持されることの検証。
     - セッション破棄時（`CloseDocument`）に `DetailEditor` が適切に解放されることの検証。
   - `DetailEditorViewModelTests`:
     - `SetZoom` 実行時に `FitMode` が自動的に `DetailViewFitMode.None` に遷移することの検証。
2. **全体検証**:
   - `dotnet test` で全テストが 100% PASS すること。
   - `dotnet build` で警告・エラーがないこと。