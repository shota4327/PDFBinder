# Issue #33 パスワード付きPDFの読み込み・表示・上書き保存 実装計画

## 1. 概要
Issue #33 に基づき、パスワードで保護されたPDFファイルをパスワード入力により開いて閲覧（高解像度レンダリング・詳細手書き編集）し、開いた際のパスワードを維持したまま上書き保存（およびエクスポート・分割）できるようにします。

---

## 2. 合意された仕様・要件
1. **パスワード入力UI / フロー**:
   - パスワード保護されたPDFを開こうとした際、専用のモーダルダイアログ（`PasswordPromptDialog`）を表示する。
   - ダイアログ構成: 対象ファイル名、パスワード入力欄（`PasswordBox` / マスク解除用 `TextBox`）、目アイコンによる表示切替、インラインエラーメッセージ、OK / キャンセルボタン（Enter / Esc ショートカット対応）。
   - 正しいパスワードが入力されるまで再試行可能。キャンセル時は読み込みを中止する。
2. **既存ドキュメントへの追加（結合）**:
   - 既存のバインダーにパスワード付きPDFを追加（結合）する際も、同様にパスワード入力を要求し、認証成功時にページを差し込む。キャンセル時はそのファイルのみ追加をスキップする。
3. **パスワード保護の維持（保存時）**:
   - 開く際に入力されたパスワードは `PdfDocumentModel.DocumentPassword` および `PdfPageModel.SourcePassword` として保持する。
   - 上書き保存時（`SaveDocumentAsync`）、選択ページのエクスポート（`ExportPagesAsync`）、全ページ分割（`SplitAllPagesAsync`）において、元のパスワードで再度暗号化して保存する。
   - メインドキュメントに追加されたページの保存パスワードは、メインドキュメントのパスワード設定を継承する。

---

## 3. アーキテクチャおよび設計方針

### 3.1 MVVM / サービス分離
- **`IPasswordPromptService` / `PasswordPromptService`**:
  - `Task<string?> PromptPasswordAsync(string fileName, string? errorMessage = null)`
  - UI層（WPFダイアログ）と ViewModel層 を疎結合にするサービス。
  - 単体テスト時はモックやスタブで差し替え可能。
- **`PdfPasswordRequiredException`**:
  - `PdfService` でパスワードが必要、または指定されたパスワードが誤っている場合にスローする専用例外。
  - `IsInvalidPassword`: パスワード未指定による要求か、入力パスワードが誤っていたかを示すフラグ。

### 3.2 サービス層の改修 (`PDFBinder.Core`)
- **`PdfDocumentModel` / `PdfPageModel`**:
  - `PdfDocumentModel.DocumentPassword`: 保存時に適用するパスワード（`string?`）。
  - `PdfPageModel.SourcePassword`: 該当ページの元PDFを開くためのパスワード（`string?`）。
- **`IPdfService` / `PdfService`**:
  - `LoadDocumentAsync(string filePath, string? password = null)`
  - `AppendDocumentAsync(PdfDocumentModel targetDoc, string filePath, int insertIndex = -1, string? password = null)`
  - `PdfReader.Open(stream, password, PdfDocumentOpenMode.Import)` を使用。
  - パスワードが必要な場合に `PdfPasswordRequiredException` をスロー。
  - 保存時（`BuildAndSavePdf`）:
    - ソースドキュメントの読み込み時に各ページの `SourcePassword` を使用。
    - パスワードが存在する場合、`outputDoc.SecuritySettings.UserPassword = password;` を設定して暗号化保存。
- **`IPdfRenderer` / `PdfiumRenderer`**:
  - `RenderPageAsync`: `string? password = null` パラメータを追加。
  - `GetInteractiveDataAsync`: `string? password = null` パラメータを追加。
  - Docnet の `DocLib.Instance.GetDocReader(pathOrBytes, password, dimensions)` を使用してパスワード付きPDFを正しくデコード。

### 3.3 アプリケーション・ViewModel層の改修 (`PDFBinder.App`)
- **`PasswordPromptDialog.xaml` / `PasswordPromptDialog.xaml.cs`**:
  - モダンなカードUI・ダイアログスタイル。
  - パスワードのマスク表示 / 平文表示の切り替え（目アイコン）。
  - キーボード操作（Enter で決定、Escape でキャンセル）。
- **`MainViewModel.cs`**:
  - ドキュメントオープン時およびファイル追加時、`PdfPasswordRequiredException` をキャッチして `IPasswordPromptService` によるループ処理を実施。
  - 認証成功でドキュメント読み込み完了、キャンセルで中断。
  - ページアイテム（`DetailPageItemViewModel` 等）へのパスワード引き渡しとレンダリング連携。

---

## 4. テスト計画
1. **動的パスワード保護PDF生成による単体テスト**:
   - `PdfSharp` を用いて、パスワード付きPDFを動的に生成（UserPassword 設定）。
2. **`PdfServiceTests`**:
   - パスワード未指定で読み込みを試行した際に `PdfPasswordRequiredException` がスローされること。
   - 誤ったパスワードで読み込みを試行した際に `PdfPasswordRequiredException`（`IsInvalidPassword = true`）がスローされること。
   - 正しいパスワードで読み込みが成功し、ページ数・サイズが正しく取得できること。
   - パスワード付きPDFの編集・上書き保存後、生成されたPDFが元のパスワードで保護されていること（パスワードなしで開けず、パスワードありで開けること）。
3. **`PdfiumRendererTests`**:
   - パスワードなしではレンダリングが白紙（または失敗）になり、正しいパスワードを渡すことで正常にビットマップがレンダリングされること。
4. **`MainViewModelTests`**:
   - モック `IPasswordPromptService` を使用し、パスワード入力ループ（誤り入力後の再入力成功、キャンセル処理）が想定通り動作すること。

---

## 5. 完了基準
1. `dotnet test` で既存テストおよび新規追加テストがすべて 100% PASS すること。
2. `dotnet build` でエラー・警告なくビルドが成功すること。
3. `GEMINI.md` のコーディング原則（メソッド行数目安、日本語コメント、MVVM分離、最小差分）を遵守すること。
