# Issue #49 実装計画: 閉じる前の未保存変更確認ダイアログ表示

## 1. 概要
PDFドキュメントに未保存の変更（ページの追加・並び替え・削除・回転・白紙追加、手書き描画・編集など）が存在する場合、アプリケーション終了時（ウィンドウを閉じる操作）および「別のPDFを開く」操作時に、ユーザーに対して保存を確認するダイアログ（「はい（保存）」「いいえ（破棄）」「キャンセル（中断）」）を表示し、誤った変更の喪失を防止します。
また、単にPDFを開いて閲覧しただけで何も変更していない場合は、ダイアログを出さずに即座に終了する既存の軽快な動作を維持します。

---

## 2. 要件と仕様
1. **未保存判定（Dirty Tracking）**:
   - PDF読み込み直後: `IsModified == false`
   - 手書きの追加・編集: `InkStrokes.StrokesChanged` により `IsModified = true`。書いた文字を消去した場合も含め、一度でも編集されれば変更ありとみなす。
   - ページの回転: 時計回り・反時計回り・180度回転により `IsModified = true`。
   - ページの追加・結合・削除・並び替え・白紙追加: `Pages.CollectionChanged` により `IsModified = true`。
   - Undo/Redoによる操作: 操作が行われた時点で `IsModified = true` を維持。
   - 保存完了時: `doc.ResetModifiedState()` により `IsModified = false` にリセット。
   - サムネイル生成処理（`EnsureThumbnailsGeneratedAsync`）: 従来 `page.IsModified = false` としていた箇所を、専用フラグ `page.IsThumbnailDirty` を用いて制御し、ドキュメントの未保存状態を勝手にクリアしないよう分離。
2. **ダイアログ表示タイミング**:
   - **アプリ終了時（タイトルバー [X] ボタン、Alt+F4、CloseWindowCommand）**: 未保存の変更がある場合のみダイアログ表示。
   - **別ファイルを開く時（`OpenDocumentAsync`）**: 現在のドキュメントに未保存の変更がある場合のみダイアログ表示。
3. **ダイアログの選択肢と動作**:
   - **はい（保存して続行）**:
     - ファイルパスが存在する場合は上書き保存。
     - 名称未設定（ファイルパス未指定）の場合は「名前を付けて保存」ダイアログを表示。
     - 保存ダイアログでキャンセルされた場合、または保存に失敗した場合は、終了・ファイルオープンを中断（元の状態にとどまる）。
     - 保存成功後、終了または新しいファイルのオープンを実行。
   - **いいえ（保存せずに破棄）**:
     - 変更内容を保存せず、そのまま終了または新しいファイルのオープンを実行。
   - **キャンセル（操作中断）**:
     - 終了または新しいファイルのオープンを取り消し、現在の編集状態を維持。

---

## 3. 変更対象ファイルと設計

### 3.1 `PDFBinder.Core`
- **`Models/PdfPageModel.cs`**:
  - `IsThumbnailDirty` プロパティを追加（サムネイル再生成用フラグ）。
  - ストローク変更時（`InkStrokes.StrokesChanged`）および回転変更時（`OnRotationChanged`）に `IsModified = true` と `IsThumbnailDirty = true` を設定。
- **`Models/PdfDocumentModel.cs`**:
  - `Pages.CollectionChanged` 時に各ページの `PropertyChanged` イベントを監視/解除。
  - 各ページの `IsModified` 変更時にドキュメント全体の `IsModified = true` に連動。
  - `ResetModifiedState()` メソッドを追加（ドキュメントおよび全ページの `IsModified` / `IsThumbnailDirty` を `false` に初期化）。
  - `Clear()` 時に `ResetModifiedState()` を呼び出し。
- **`Services/PdfService.cs`**:
  - `LoadDocumentAsync` 完了時に `model.ResetModifiedState()` を実行。
  - `SaveDocumentAsync` 完了時に `doc.ResetModifiedState()` を実行。

### 3.2 `PDFBinder.App`
- **`ViewModels/MainViewModel.cs`**:
  - `EnsureThumbnailsGeneratedAsync` の判定・リセット対象を `IsThumbnailDirty` に変更（`IsModified` を破壊しない）。
  - `ExecuteSaveAsync`、`SaveDocumentAsync`、`SaveDocumentAsAsync` を `Task<bool>` に変更し、保存成功/キャンセル/失敗の成否を返却可能に改修。
  - 確認ダイアログのハンドラ（`Func<string, SaveConfirmationResult>? ConfirmSaveModifiedDocument` または `IDialogService`）を導入し、テスト容易性を確保。
  - 未保存変更確認メソッド `ConfirmSaveAndCloseAsync()` / `CheckAndConfirmSaveAsync()` を実装。
  - `OpenDocumentAsync` に未保存確認処理を組み込み。
- **`MainWindow.xaml.cs`**:
  - `Window.Closing` イベントハンドラを追加。
  - `vm.Document.IsModified && vm.Document.Pages.Count > 0` の場合、`e.Cancel = true` として非同期確認 `vm.ConfirmSaveAndCloseAsync()` を実行。続行可であればフラグを立ててウィンドウをクローズ。

### 3.3 `PDFBinder.Tests`
- **`PDFBinder.Tests/UnsavedChangesTests.cs`**:
  - ページ追加・削除・並び替えでの `IsModified` 検証。
  - 手書きストローク変更時の `IsModified` 検証（文字消去時も含む）。
  - 回転変更時の `IsModified` 検証。
  - `ResetModifiedState()` によるリセット検証。
  - `MainViewModel` での保存確認ダイアログ分岐（はい・いいえ・キャンセル）のテスト。

---

## 4. 検証手順
1. `dotnet test`: 全単体テストがパスすることを確認。
2. `dotnet build`: エラー・警告がないことを確認。
3. 実装確認:
   - PDFを開いて何もしないで [X] -> ダイアログ出ずに即終了。
   - PDFを開いて1ページ回転して [X] -> ダイアログ表示 -> [キャンセル] で終了中止 -> [いいえ] で終了。
   - 手書き追加して [X] -> [はい] で保存され終了。
