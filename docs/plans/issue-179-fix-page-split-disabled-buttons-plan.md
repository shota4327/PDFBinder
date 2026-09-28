# 実装計画: ページ分割・ページ構成変更時の削除・分割コマンド有効化不具合の解消 (Issue #179)

## 1. 概要
ドキュメントのページ分割（全ページ半分分割 `SplitPagesHalfAsync`）を実行した際、分割後にページ数が10ページを超える場合（元のページ数が6ページ以上で分割後12ページ以上になる場合など）や、新規作成から白紙追加を行っていった際に、リボンツールバーの「削除」ボタン（`DeleteSelectedPagesCommand`）や「全分割」ボタン（`SplitAllPagesCommand`）などの一部機能が無効化（グレーアウト）されたままになる不具合を解消します。

---

## 2. 原因分析
1. **コマンド実行可否判定（CanExecute）の依存関係**:
   - `CanDeleteSelectedPages`: `HasOpenDocuments && !IsImageDocumentActive && Document.PageCount > 1`
   - `CanSplitAllPages`: `HasOpenDocuments && !IsImageDocumentActive && Document.PageCount > 0`
   - `CanSplitPagesHalf`: `HasOpenDocuments && !IsImageDocumentActive && Document.PageCount > 0`
   これらのプロパティは `Document.PageCount` に直接依存しています。

2. **コマンド再評価通知（NotifyCanExecuteChanged）の欠落**:
   - `CommunityToolkit.Mvvm` の `[RelayCommand]` は、自動的に WPF の `CommandManager.RequerySuggested` を購読せず、明示的に `Command.NotifyCanExecuteChanged()` を呼び出さない限り WPF 側のボタン有効状態（`IsEnabled`）が更新されません。
   - 現状の `MainViewModel` では、ドキュメントのページ構成が変化（`Document.Pages.CollectionChanged`）した際に呼び出される `UpdateDocumentNavigationProperties()` において、ナビゲーション系コマンド（`GoToPreviousPageCommand`, `GoToNextPageCommand`, `ShowPrintDialogCommand`）のみが通知されており、ページ数に依存する `DeleteSelectedPagesCommand`、`SplitAllPagesCommand`、`SplitPagesHalfCommand` は通知されていませんでした。

3. **ページ分割処理中の一時的空状態による無効化の固定化**:
   - `SplitPagesHalfAsync` 実行時、`ReplaceAllPagesCommand.Execute()` 内で `_doc.Pages.Clear()` が実行され、一時的に `Document.PageCount` が 0 になります。
   - この `Pages.Clear()` によるコレクションの Reset イベント発生時に WPF 側のバインディングやコマンド評価が走り、`Document.PageCount > 1`（0 > 1 = false）と評価されてボタンが無効化されます。
   - その後、分割後の全ページ（12ページなど）がコレクションに追加され `UpdatePageNumbers()` で `PageCount` が更新されても、`DeleteSelectedPagesCommand.NotifyCanExecuteChanged()` が一切呼ばれないため、無効化された状態のまま固定化されていました。

4. **白紙追加時における初期無効状態の残存**:
   - 未読み込み状態から「白紙追加」を実行した場合、セッション生成時点では `PageCount == 0` であり、「削除」コマンドは無効として初期化されます。
   - 2ページ目以降を追加しても `DeleteSelectedPagesCommand.NotifyCanExecuteChanged()` が呼ばれないため、ページ数が増えても削除ボタンが無効のまま残り続けていました。

---

## 3. 改修設計と変更内容

### 3.1 `MainViewModel.cs` の修正
1. **ページ数依存コマンド状態の同期メソッド拡充**:
   - `UpdateDocumentPageProperties()`（または既存の `UpdateDocumentNavigationProperties()` の拡充）において、以下のプロパティ通知およびコマンド再評価を追加:
     ```csharp
     OnPropertyChanged(nameof(CanSplitAllPages));
     OnPropertyChanged(nameof(CanSplitPagesHalf));
     OnPropertyChanged(nameof(CanDeleteSelectedPages));
     OnPropertyChanged(nameof(CanAddBlankPage));
     OnPropertyChanged(nameof(CanExportSelectedPages));
     OnPropertyChanged(nameof(CanAppendDocument));

     SplitAllPagesCommand.NotifyCanExecuteChanged();
     SplitPagesHalfCommand.NotifyCanExecuteChanged();
     DeleteSelectedPagesCommand.NotifyCanExecuteChanged();
     AddBlankPageCommand.NotifyCanExecuteChanged();
     ExportSelectedPagesCommand.NotifyCanExecuteChanged();
     AppendDocumentCommand.NotifyCanExecuteChanged();
     ```
2. **ページコレクション変更イベントでの確実な実行**:
   - `OnDocumentPagesCollectionChanged`: ページの追加・削除・置換・リセット時に上記同期メソッドを必ず呼び出す。
3. **`Document.PropertyChanged` での PageCount 監視**:
   - `OnDocumentPropertyChanged`: `e.PropertyName == nameof(PdfDocumentModel.PageCount)` を監視し、ページ数更新時に上記同期メソッドを呼び出す。
4. **各ページ操作コマンド完了時での明示的呼び出し**:
   - `SplitPagesHalfAsync`: 分割完了・Undo履歴登録後にコマンド状態を明示的に更新。
   - `AddBlankPage`: 白紙追加完了後にコマンド状態を明示的に更新。
   - `DeleteSelectedPages`: 削除完了後にコマンド状態を明示的に更新（1ページ以下になった際の無効化も即座に反映）。

---

## 4. 単体テスト計画
`tests/PDFBinder.Tests/MainViewModelSplitHalfTests.cs`（または新規テストクラス）に以下の検証テストを追加します:
1. **白紙追加時の削除コマンド有効化テスト**:
   - 未読み込み状態から白紙追加を行い、1ページ時は `CanDeleteSelectedPages == false`、2ページ追加以降は `DeleteSelectedPagesCommand.CanExecute(null) == true` となること。
2. **6ページ以上のページ分割後のコマンド状態テスト**:
   - 6ページのドキュメント（実PDFまたは白紙）を作成し、`SplitPagesHalfCommand.ExecuteAsync(null)` を実行。
   - 分割後（12ページ）において、`DeleteSelectedPagesCommand.CanExecute(null) == true`、`SplitAllPagesCommand.CanExecute(null) == true`、`SplitPagesHalfCommand.CanExecute(null) == true` がすべて `true` になること。
3. **ページ削除による境界値テスト**:
   - 2ページのドキュメントから1ページ削除した際、`DeleteSelectedPagesCommand.CanExecute(null) == false` に即座に遷移すること。
4. **Undo / Redo 時のコマンド状態復元テスト**:
   - 分割や削除の Undo / Redo を実行した際にも、ページ数に応じたコマンドの有効・無効状態が正しく追従すること。

---

## 5. ドキュメントおよびバージョン更新
1. `Directory.Build.props`: パッチバージョンをインクリメント（`0.8.2` → `0.8.3`）。
2. `CHANGELOG.md`: `## [0.8.3] - 2026-09-28` を追加し、エンドユーザー向けリリースノートを記述。最上部に空の `## [Unreleased]` を配置。
3. `docs/basic_design.md` & `docs/PROJECT.md`: 必要に応じてステータスや記述を同期。
4. `docs/plans/issue-179-fix-page-split-disabled-buttons-walkthrough.md`: 検証結果を記録。
