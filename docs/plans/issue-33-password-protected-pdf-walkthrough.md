# Issue #33 パスワード付きPDFの読み込み・表示・上書き保存 検証報告（Walkthrough）

## 1. 概要
Issue #33 に基づき、パスワードで保護されたPDFファイルを専用ダイアログから安全に対話的入力して開き、表示・手書き編集を行い、元のパスワード保護を維持したまま上書き保存（およびエクスポート・分割）できる機能を実装・検証しました。

---

## 2. 実装内容詳細

### 2.1 コアライブラリ層 (`PDFBinder.Core`)
- **`PdfPasswordRequiredException` の新設**:
  - パスワードが必要な場合、およびパスワード誤り時にスローされる例外クラスを作成。対象ファイルパスおよび `IsInvalidPassword` フラグを保持。
- **データモデルの拡張**:
  - `PdfDocumentModel.DocumentPassword`: 保存時に適用するパスワードを保持。
  - `PdfPageModel.SourcePassword`: 各ページの元ファイルを開くためのパスワードを保持。
- **`PdfService` のパスワード対応**:
  - `LoadDocumentAsync`: `PdfReader.Open` 時に暗号化例外をキャッチして `PdfPasswordRequiredException` をスロー。
  - `AppendDocumentAsync`: 結合時にもパスワードを受け取れるようオーバーロードを追加。
  - `SaveDocumentAsync`, `ExportPagesAsync`, `SplitAllPagesAsync`:
    - 各ページの読み込み時に `page.SourcePassword` を使用。
    - 保存時に `outputDoc.SecuritySettings.UserPassword` を設定し、元のパスワード保護を確実に維持。
  - 既存モックとの後方互換性を保つデフォルトインターフェース実装およびオーバーロードの整備。
- **`PdfiumRenderer` のパスワード対応**:
  - `RenderPageAsync` および `ExtractInteractiveDataAsync` にパスワード引数を追加。
  - `DocLib.Instance.GetDocReader` にパスワードを渡し、Docnet / PDFium 経由での高精細レンダリングおよび文字・リンク抽出に対応。

### 2.2 アプリケーション・ViewModel層 (`PDFBinder.App`)
- **`MainViewModel`**:
  - パスワード入力プロンプト制御用プロパティ（`IsPasswordPromptVisible`, `PasswordPromptFileName`, `PasswordPromptInput`, `PasswordPromptErrorMessage`, `IsPasswordInputMasked`）およびコマンド（`ConfirmPasswordPromptCommand`, `CancelPasswordPromptCommand`, `TogglePasswordMaskCommand`）を実装。
  - `LoadDocumentWithPasswordPromptAsync`:
    - パスワード付きPDFを検知した際に対話的にダイアログを表示。
    - 誤入力時はダイアログ内にエラーメッセージ（「パスワードが正しくありません。再度入力してください。」）を表示して再試行可能。
    - キャンセル時は安全に読み込みを中断（結合時はそのファイルのみスキップ）。
    - 単体テスト用の委譲プロパティ `PromptPasswordCallback` を設け、テスト容易性を確保。
  - ページアイテムのサムネイルおよびプレビューレンダリング呼び出し時に `page.SourcePassword` を連携。
- **`DetailEditorViewModel`**:
  - ページレンダリングおよびインタラクティブデータ抽出時に `item.Page.SourcePassword` を連携。
- **`MainWindow.xaml` / `MainWindow.xaml.cs`**:
  - インアプリ・オーバーレイ（`ModalBackdropGridStyle` + `ModalCardBorderStyle`）としてパスワード入力カードを配置（Z-Index: 1007）。
  - 鍵アイコン、ファイル名表示、パスワード入力欄（`PasswordBox` / `TextBox`）、目アイコンによる伏せ字/平文の切り替えボタン、インライン赤字エラー表示、OK/キャンセルボタン。
  - Enter キーで確定、Escape キーでキャンセルできるキーボードショートカット制御（`HandlePasswordPromptKeyDown`）。
  - 開いた瞬間にパスワード入力欄へ自動フォーカスを当てる処理を実装。

---

## 3. 検証結果

### 3.1 単体テスト実行結果 (`dotnet test`)
全テストスイートにおいて **627件 すべて 100% PASS** を確認しました。

```
C:\Git\PDFBinder\tests\PDFBinder.Tests\bin\Debug\net10.0-windows\PDFBinder.Tests.dll (.NETCoreApp,Version=v10.0) のテスト実行
合計 1 個のテスト ファイルが指定されたパターンと一致しました。

成功!   -失敗:     0、合格:   627、スキップ:     0、合計:   627、期間: 8 s - PDFBinder.Tests.dll (net10.0)
```

追加・検証された主なテストケース：
1. **`PdfServiceTests`**:
   - `LoadDocumentAsync_ProtectedPdfWithoutPassword_ThrowsPdfPasswordRequiredException`
   - `LoadDocumentAsync_ProtectedPdfWithWrongPassword_ThrowsPdfPasswordRequiredExceptionWithIsInvalidPassword`
   - `LoadDocumentAsync_ProtectedPdfWithCorrectPassword_LoadsSuccessfully`
   - `SaveDocumentAsync_ProtectedPdf_PreservesPasswordEncryption`
   - `AppendDocumentAsync_ProtectedPdf_AppendsSuccessfullyWithPassword`
   - `ExportPagesAsync_ProtectedPdf_PreservesPasswordEncryption`
2. **`PdfiumRendererTests`**:
   - `RenderPageAsync_ProtectedPdf_RendersCorrectlyWithPassword`
3. **`MainViewModelPasswordTests`**:
   - `LoadDocumentWithPasswordPromptAsync_WithCorrectPassword_LoadsDocument`
   - `LoadDocumentWithPasswordPromptAsync_WithWrongPasswordThenCorrect_RetriesWithError`
   - `LoadDocumentWithPasswordPromptAsync_WhenCancelled_ReturnsNull`
   - `PasswordPrompt_ConfirmCommand_ResolvesPromptTask`
   - `PasswordPrompt_CancelCommand_ResolvesNull`
   - `PasswordPrompt_ToggleMaskCommand_TogglesMaskState`

### 3.2 ビルド確認結果 (`dotnet build`)
警告・エラー0件でビルド成功を確認。

```
ビルドに成功しました。
    0 個の警告
    0 エラー
```

---

## 4. バージョンおよび関連ドキュメント更新
- **`Directory.Build.props`**: 新機能追加に伴い、バージョンを `0.11.5` から `0.12.0`（マイナー繰り上げ、パッチ0リセット）に更新。
- **`CHANGELOG.md`**: `[0.12.0]` のリリースノートをエンドユーザー向けに記述し、最上部に空の `[Unreleased]` を配置。
- **`docs/basic_design.md`**: `IPdfService`、`IPdfRenderer`、堅牢性セクションにパスワード保護PDF対応を反映。
- **`README.md`**: 機能一覧にパスワード保護PDF対応を追記。
- **`docs/PROJECT.md`**: 機能インベントリに `F76`（パスワード保護PDF対応）を完了として登録。
