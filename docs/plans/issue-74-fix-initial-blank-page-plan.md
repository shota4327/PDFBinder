# Issue #74: PDF読み込み時に最初のページが真っ白で表示される不具合の修正計画

## 1. 概要・背景
PDFファイルを読み込んだ際、手書きエディタ（詳細ビュー）に切り替わった直後の1ページ目の背景が真っ白（ブランク）で表示され、次ページへ切り替えて戻る等の操作を行って初めて正常にレンダリングされる不具合が報告されています（Issue #74）。

### 原因調査結果
1. **`MainViewModel.OpenDocumentAsync` における多重初期化**:
   - `Document = doc;` の実行によりプロパティ変更通知 `OnDocumentChanged` がトリガーされ、`DetailEditor?.InitializeDocument(newValue);` が実行される。
   - その直後、`OpenDocumentAsync` 内で `DetailEditor?.InitializeDocument(doc);` が再度呼び出されていた。
2. **`DetailEditorViewModel.InitializeDocument` における通知不備と参照の乖離**:
   - 1回目の呼び出しで `Pages` コレクションが生成され、`CurrentPage` に1ページ目が代入され、`CurrentPageItem`（1ページ目の `DetailPageItemViewModel` インスタンス A）が確定し、UI側（`SinglePageContainer`）のバインディングが確立される。
   - 2回目の呼び出しで `Pages.Clear()` によりインスタンス A が破棄され、新たにインスタンス B が追加される。
   - しかし、`CurrentPage = document.Pages.FirstOrDefault();` で設定される `CurrentPage` の値が1回目と全く同一であるため、`CommunityToolkit.Mvvm` のプロパティセッターが「値変更なし」と判定し、`CurrentPage` および `[NotifyPropertyChangedFor(nameof(CurrentPageItem))]` の変更通知（`PropertyChanged`）が一切発火しない。
   - その結果、WPFのUIコンテナは破棄されたインスタンス A（背景未生成のまま取り残された古いViewModel）を参照し続け、非同期レンダリング完了通知がインスタンス B にのみ届くため、画面上は白い枠（白紙）のままとなる。
   - ページを切り替えると `CurrentPage` が変化してインスタンス B へのバインディングが再評価されるため、正常に表示されるようになる。

---

## 2. ユーザー確認・合意済み事項（/grill-me にて決定）
1. **修正方針**:
   - `DetailEditorViewModel.InitializeDocument` におけるプロパティ通知保証（防御的改修）と、`MainViewModel.OpenDocumentAsync` における重複呼び出し削除の両方を実施する。
2. **`InitializeDocument` 実行時のカレントページ選択ポリシー**:
   - 既存の `CurrentPage` が新しいドキュメント内に存在する場合はそのページを維持し、存在しない場合または新規ファイル読み込み時は先頭ページを選択する。
   - これにより、白紙追加・結合・削除等の操作時にも不自然に先頭ページへ巻き戻るのを防止する。
3. **ビューのアクティブ化順序**:
   - `MainViewModel.OpenDocumentAsync` において、`IsDetailViewActive = true` を `Document = doc` の代入前に設定する（ビューが先にアクティブ化され、レイアウトおよびビューポート計算がスムーズに行われる）。

---

## 3. 変更対象ファイルと詳細設計

### 1. `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- `InitializeDocument(PdfDocumentModel document)` の見直し:
  - 呼び出し前のカレントページ（またはそのID/ページ番号）を記憶。
  - `Pages.Clear()` で再構築。
  - 新ドキュメント内から維持すべきページ（以前のカレントページと一致するもの、なければ先頭ページ）を特定。
  - `_currentPage` を一旦クリア（`null`）した上で目的のページを代入し、確実に `CurrentPage` のプロパティ変更通知および依存プロパティ（`CurrentPageItem`, `PageBackground`, `CurrentPageIndex`, `CurrentPageNumber`, `CanGoToPreviousPage`, `CanGoToNextPage` 等）の通知を発火させる。
  - 明示的に `OnPropertyChanged(nameof(CurrentPageItem));` および `OnPropertyChanged(nameof(PageBackground));` を呼び出し、UIバインディングの即時再評価を100%保証する。

### 2. `src/PDFBinder.App/ViewModels/MainViewModel.cs`
- `OpenDocumentAsync(string? filePath = null)` の見直し:
  - `IsDetailViewActive = true;` を `Document = doc;` の前に移動。
  - `Document = doc;` による `OnDocumentChanged` 経由での初期化に一元化し、重複していた直後の `DetailEditor?.InitializeDocument(doc);` を削除。

### 3. `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs` (および関連テスト)
- `InitializeDocument` を連続で呼び出した場合でも、`CurrentPageItem` が最新の `Pages` 内のインスタンスと一致し、背景画像設定が正しく反映されることを検証する単体テストを追加。
- ページ存在時に `InitializeDocument` を実行した際、カレントページが正しく維持される挙動を検証する単体テストを追加。

---

## 4. 検証計画

### 1. 自動テスト
- `dotnet test`: 既存の159件のテストおよび新設テストがすべて PASS することを確認。

### 2. ビルド検証
- `dotnet build`: 警告・エラーなく正常終了することを確認。

### 3. ドキュメント更新
- `GEMINI.md` に従い、作業完了時に `docs/plans/issue-74-fix-initial-blank-page-walkthrough.md` を作成・記録する。
