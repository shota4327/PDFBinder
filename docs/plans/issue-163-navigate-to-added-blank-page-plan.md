# 実装計画書: Issue #163 白紙ページを追加したときは追加したページを表示する（詳細ビュー）

## 1. 概要
詳細ビュー表示中に「白紙ページを追加（Ctrl+B / ツールバーボタン）」を実行した際、新しく追加された白紙ページが即座に表示（単一ページ表示時はページ切り替え、連続スクロール表示時は該当ページへのスクロール）されるように改善します。
また、白紙ページ追加直後に「元に戻す（Undo）」を行った際にも、削除された白紙ページの直前に表示していたページが適切に維持・再選択されるように `DetailEditorViewModel.InitializeDocument` のページ復元ロジックを強化します。

## 2. 背景・課題
- 現在の `MainViewModel.AddBlankPage()` では、ページ挿入後に `DetailEditor?.InitializeDocument(Document)` を呼び出している。
- `DetailEditorViewModel.InitializeDocument` は「以前のカレントページが存在する場合は維持する」というロジックになっているため、詳細ビューでページを表示している最中に白紙ページを挿入しても、挿入前のページが表示されたままとなり、追加された白紙ページへ自動遷移しない。
- また、白紙ページを追加した後に Undo した場合、直前のカレントページ（削除された白紙ページ）がドキュメントから存在しなくなるため、フォールバックとして先頭ページ（1ページ目）に飛んでしまう。

## 3. 要件と設計方針
ヒアリング（/grill-me）にて決定した方針：
1. **詳細ビュー表示中のみ自動切り替え**:
   - 詳細ビュー表示中（`IsDetailViewActive == true`）に白紙ページを追加した場合、追加された白紙ページへ切り替え・スクロールする。
   - グリッドビュー表示中では、既存の選択状態を変更しない。
2. **Undo時の元のページへの復帰**:
   - 白紙ページ追加直後に詳細ビューで「元に戻す（Undo）」を実行した場合、先頭ページへ飛ぶのではなく、白紙ページ追加前の元のページ（挿入位置の直前ページ）を表示する。

## 4. 変更対象ファイルと改修内容

### 4.1 `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
- `InitializeDocument(PdfDocumentModel document, PdfPageModel? preferredPage = null)` メソッドのシグネチャを拡張:
  - 引数に `PdfPageModel? preferredPage = null` を追加。
  - カレントページの決定ロジックを以下のように改修:
    1. `preferredPage` が指定されており、`document.Pages` に存在する場合は優先して選択。
    2. `preferredPage` が未指定または見つからない場合、直前の `CurrentPage` が `document.Pages` に存在すればそれを維持。
    3. 直前の `CurrentPage` が存在しない場合（Undoによる削除等）、直前のインデックス `previousIndex` を用いて、`Math.Clamp(previousIndex - 1, 0, document.Pages.Count - 1)`（または直前の有効なインデックス位置）のページを選択。
    4. ドキュメントが空でなければ `document.Pages.FirstOrDefault()` をフォールバックとする。
  - `targetPage` が決定した際、`ScrollToPageRequested?.Invoke(targetPage)` を呼び出して、View層（スクロールビューアー）にも遷移を通知。

### 4.2 `src/PDFBinder.App/ViewModels/MainViewModel.cs`
- `AddBlankPage()`:
  - ページ挿入後、詳細ビュー表示中（`IsDetailViewActive == true`）の場合:
    - `DetailEditor?.InitializeDocument(Document, blank);` を呼び出し、追加された `blank` ページをターゲットとして表示・スクロールさせる。
    - グリッドビュー表示中は従来通り `DetailEditor?.InitializeDocument(Document);` を呼び出す（選択状態は変更しない）。

### 4.3 `tests/PDFBinder.Tests/`
- 新規または既存の単体テストクラス（`DetailEditorViewModelTests.cs` / `ViewModelsTests.cs` 等）に以下のテストケースを追加:
  1. 詳細ビュー表示中に `AddBlankPage()` を実行すると、`DetailEditor.CurrentPage` が追加された白紙ページになり、`ScrollToPageRequested` が発火すること。
  2. 詳細ビュー表示中に白紙ページを追加した後 `Undo()` を実行すると、元のページ（直前のページ）がカレントページとして復元されること。
  3. グリッドビュー表示中に `AddBlankPage()` を実行した場合、既存ページの選択状態が影響を受けないこと。

## 5. 検証手順
1. `dotnet test`: すべての既存テストおよび新規追加テストが 100% PASS することを確認。
2. `dotnet build`: 警告・エラーなくビルドが成功することを確認。
