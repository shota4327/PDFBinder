# Issue #78: ビュー内フォーカス時のショートカット不発解消および矢印キーによるページ送りショートカット追加 実装計画書

## 1. 概要
`DetailEditorView`（手書きキャンバスやスクロールビューア）にフォーカスがある状態で `PageUp` / `PageDown` などのキーボードショートカットが機能せず、ステータスバー等の特定コントロールにフォーカスがある場合のみ動作する問題を解決します。
また、ユーザー要望に基づき、修飾キーなしの矢印キー（↑ / ← で前のページへ、↓ / → で次のページへ）によるページ移動ショートカットを新設し、手書き作業中やビューア閲覧中でも快適にページめくりを行えるようにします。

---

## 2. 根本原因の分析
1. **ScrollViewerによるキーイベント消費**:
   - `DetailEditorView` で使用されている `NoAutoScrollScrollViewer`（`ScrollViewer` のサブクラス）は、WPF標準のキーボードナビゲーション処理（`ScrollViewer.OnKeyDown`）により、`PageUp`, `PageDown`, `Up`, `Down`, `Left`, `Right` などのキー入力を内部でスクロール処理として消費し、`e.Handled = true` を設定します。
   - このため、キーイベントのバブリングが `ScrollViewer` で停止し、ウィンドウレベルの `Window.InputBindings`（`GoToPreviousPageCommand` / `GoToNextPageCommand`）に到達しませんでした。
2. **InkCanvas等の子要素でのフォーカス保持**:
   - 手書き操作時、フォーカスは `EditorInkCanvas` または `ScrollViewer` 内部に留まり、上記1の原因によりショートカットが不発となっていました。
3. **矢印キーのバインディング未定義**:
   - 矢印キー（↑↓←→）によるページ移動は `InputBindings` に定義されておらず、キーボードによる直感的なページ送りがサポートされていませんでした。

---

## 3. 実装方針と設計詳細

### 3.1 `MainWindow.xaml.cs` での先行検知・ショートカットルーティング（`OnPreviewKeyDown`）
WPFのトンネリングイベント（`PreviewKeyDown`）は、子コントロールがイベントを消費する前にトップレベル（`Window`）で先行検知できる特性を持ちます。
1. **テキストボックス編集時の除外（ユーザー確認済み）**:
   - フォーカス要素またはイベント発生元が `TextBoxBase`（ページ番号入力欄など）の場合は先行処理を行わず、ネイティブなテキスト編集（左右矢印キーでのカーソル移動、Deleteキーでの文字削除、文字入力等）を優先します。
2. **矢印キーおよびPgUp/PgDnによるページ移動処理**:
   - 詳細ビュー表示中（`vm.IsDetailViewActive == true`）かつ修飾キーなし（`Keyboard.Modifiers == ModifierKeys.None`）の場合:
     - `Key.PageUp`, `Key.Up`, `Key.Left`: `vm.CanGoToPreviousPage` が true なら `vm.GoToPreviousPageCommand.Execute(null)` を実行し `e.Handled = true`。
     - `Key.PageDown`, `Key.Down`, `Key.Right`: `vm.CanGoToNextPage` が true なら `vm.GoToNextPageCommand.Execute(null)` を実行し `e.Handled = true`。
3. **InkCanvas選択モード時のストローク削除保護**:
   - `ToolMode == Select` で `InkCanvas` 上のストロークが選択されている状態で `Delete` キーが押された場合は、ページの削除ではなくインクストロークの削除を優先させるため、コマンド実行をスキップしてネイティブ処理に委ねます。
4. **既存 `InputBindings` の確実な実行ルーティング**:
   - その他のキー（Ctrl+Z, Ctrl+Y, Ctrl+S, Ctrl+O, ズーム, 回転, 削除等）についても、子コントロールで不意に握りつぶされないよう、`MainWindow.InputBindings` に合致するバインディングを先行判定して確実に実行します。

### 3.2 `NoAutoScrollScrollViewer.cs` でのキー消費抑止
- `NoAutoScrollScrollViewer` の `OnKeyDown` をオーバーライドし、`PageUp`, `PageDown`, `Up`, `Down`, `Left`, `Right` が渡された場合に `base.OnKeyDown` による内部スクロール消費を行わないようにします。これにより、二重防壁としてキーの伝播が遮断されることを恒久的に防ぎます。

### 3.3 `MainWindow.xaml` へのキーバインディング追加
- XAMLの `<Window.InputBindings>` に、矢印キー（↑, ←, ↓, →）によるページ移動バインディングを明示的に登録します:
  - `<KeyBinding Key="Up" Command="{Binding GoToPreviousPageCommand}" />`
  - `<KeyBinding Key="Left" Command="{Binding GoToPreviousPageCommand}" />`
  - `<KeyBinding Key="Down" Command="{Binding GoToNextPageCommand}" />`
  - `<KeyBinding Key="Right" Command="{Binding GoToNextPageCommand}" />`

---

## 4. 変更対象ファイル
1. [MODIFY] `src/PDFBinder.App/Controls/NoAutoScrollScrollViewer.cs`
   - `OnKeyDown` をオーバーライドし、ページ移動関連キー（PgUp, PgDn, Up, Down, Left, Right）の内部消費を抑止。
2. [MODIFY] `src/PDFBinder.App/MainWindow.xaml`
   - `<Window.InputBindings>` に Up, Left, Down, Right キーバインディングを追加。
3. [MODIFY] `src/PDFBinder.App/MainWindow.xaml.cs`
   - `OnPreviewKeyDown` を拡張し、フォーカス状態に依存しないショートカット実行ロジック（TextBox除外、矢印キー/PgUp/PgDn移動、既存ショートカット先行実行）を実装。
4. [NEW] `tests/PDFBinder.Tests/ShortcutKeyNavigationTests.cs`
   - `MainWindow` のショートカットキー先行処理およびページ送りキー（PgUp/PgDn/矢印キー）の挙動、テキストボックスフォーカス時の除外を検証する単体テストを追加。
5. [MODIFY] `docs/basic_design.md`
   - キーボードショートカット一覧に矢印キーによるページ送りを追記・同期。

---

## 5. 検証手順
1. **自動テスト**:
   - `dotnet test`: 既存162件に加え新規テストを含むすべての単体テストがパスすること。
2. **ビルド検証**:
   - `dotnet build`: 警告・エラーなく成功すること。
