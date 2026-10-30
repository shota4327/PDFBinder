# Issue #214 検証報告書: ファイル切り替え時のプレビューおよび表示設定の完全保持

## 1. 実施内容の概要
複数ファイルを開いて作業している際、ファイル切り替え時に発生していた「一瞬のページ飛び・誤表示」「拡大率の手動設定（300%など）がウィンドウフィットに戻ってしまう問題」「プレビュー画像や表示レイアウトの再生成遅延・引っ掛かり」を完全に解消しました。

### 主な改修点
1. **`DocumentSession` ごとの `DetailEditorViewModel` 保持**:
   - 各ドキュメントセッションが固有の `DetailEditorViewModel` インスタンスを丸ごと保持する設計へ刷新しました。
   - ファイル切り替え時にページアイテムやレンダリング済み背景キャッシュ（`PageBackground`）を破棄・再生成せず、単に DataContext の参照を切り替える構成としたことで、高解像度プレビューの完全0遅延切り替えを実現しました。
   - ドキュメントを閉じた際は `session.Dispose()` 経由で確実に `DetailEditor.Dispose()` を実行し、全ビットマップリソースを解放します。
2. **手動ズーム操作時の `FitMode` 即時 `None` 遷移**:
   - `SetZoom(double)`、ピンチズーム、および拡大・縮小コマンドによる手動操作時、直ちに `FitMode = DetailViewFitMode.None;` に移行するよう統一しました。
   - ウィンドウリサイズ時やファイル切り替え時でも、ユーザーが意図して指定した倍率（例: 300%）が「ウィンドウに合わせる」などの設定によって上書きされず、厳密に維持されます。
3. **スクロール誤検知の抑止とピクセル単位の即時スクロール復元**:
   - `DetailEditorView` に `_isRestoringScroll` 制御フラグを導入し、DataContext 切り替え遷移中に ScrollViewer の残存オフセットによって `UpdateCurrentPageFromScroll()` がカレントページを誤検知・誤上書きしてしまう問題を遮断しました。
   - セッション離脱時にピクセル単位のスクロールオフセット（`SavedVerticalOffset`, `SavedHorizontalOffset`）を保存し、復帰時に正確なオフセットへ即時復元することで、ページ飛びやチラつきを完全に根絶しました。

---

## 2. 検証結果

### 2.1 自動テスト結果
- **単体テストスイート (`dotnet test`)**:
  - 全 686 テストすべて **100% PASS**（既存 684 件 + 新規追加 2 件）
- **追加した単体テスト**:
  1. `MainViewModelMultiFileTests.SwitchDocument_PreservesDetailEditorInstances_AndKeepsBackgroundAndScrollOffsets`:
     - ファイルA（連続表示・手動300%・スクロール位置保持・プレビューキャッシュ保持）とファイルB（単一表示・ウィンドウフィット）を往復切り替えした際、それぞれの ViewModel インスタンス、設定、プレビュー画像、スクロール位置が一切崩れず完全に保持されることを検証。
     - セッション破棄時（`CloseDocument`）にアクティブセッションが正しく追従し、リソースが正常に解放されることを検証。
  2. `DetailEditorViewModelTests.SetZoom_AndZoomCommands_TransitionFitModeToNone`:
     - `SetZoom`、`ZoomInCommand`、`ZoomOutCommand` 実行時に `FitMode` が自動的に `DetailViewFitMode.None` へ遷移し、設定した倍率が保護されることを検証。

### 2.2 ビルド検証
- `dotnet build`: 0 警告、0 エラーで正常終了。

---

## 3. 変更ファイル一覧
- `src/PDFBinder.App/Models/DocumentSession.cs`: ドキュメント専用の `DetailEditorViewModel` 保持、表示設定プロパティの委譲と通知
- `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`: スクロールオフセット保持（`SavedVerticalOffset`, `SavedHorizontalOffset`）、ズーム操作時の `FitMode` 自動 None 移行、PDFレンダラー動的設定（`SetPdfRenderer`）
- `src/PDFBinder.App/ViewModels/MainViewModel.cs`: `CreateDocumentSession` によるセッション生成の統一、`OnActiveSessionChanged` での `DetailEditor` 差し替えとビューポート同期、不要な再初期化の排除
- `src/PDFBinder.App/Views/DetailEditorView.xaml.cs`: 切り替え遷移時のスクロール誤検知抑止（`_isRestoringScroll`）、スクロールオフセットの保存およびピクセル単位の即時復元
- `tests/PDFBinder.Tests/ViewModels/MainViewModelMultiFileTests.cs`: ファイル切り替え時のプレビュー・表示状態・スクロール位置保持に関する検証テスト追加
- `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs`: ズーム操作時の FitMode None 遷移テスト追加
- `Directory.Build.props`: バージョン更新 (`0.14.2` → `0.15.0`)
- `CHANGELOG.md`: リリースノート追記（エンドユーザー向け記述）
- `docs/basic_design.md`: 基本設計書の `DocumentSession` 記述同期
- `docs/PROJECT.md`: 機能インベントリ更新 (#214)