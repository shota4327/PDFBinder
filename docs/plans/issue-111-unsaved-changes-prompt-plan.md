# 実装計画書: 手書きがあるファイルを開いた後の編集で保存確認ダイアログが表示されない不具合の修正 (Issue #111)

## 1. 概要
手書き注釈が含まれるPDFファイルを開いた後、ページ詳細エディタで手書きストロークを追加・編集・消去しても、未保存の変更（`IsModified`）が正しく検知されず、アプリ終了時や別ファイル読み込み時に「変更内容を保存しますか？」という確認ダイアログが表示されない不具合（Issue #111）を解消します。

---

## 2. 原因分析
- `PdfService.RestoreInkStrokesIfPresent` において、保存された手書き注釈を復元する際に `pageModel.InkStrokes = PdfBinderInkAnnotation.DeserializeStrokes(base64);` によるプロパティ再代入が行われていた。
- しかし、`PdfPageModel` はコンストラクタ実行時に初期生成された `new StrokeCollection()` に対してのみ `InkStrokes.StrokesChanged` イベントハンドラを購読していた。
- そのため、手書き注釈を含むPDFを開いた時点で `InkStrokes` インスタンスが差し替えられ、新しいコレクションに対するイベントハンドラが未登録状態となり、以降の手書き追加・消去で `IsModified` および `IsThumbnailDirty` が更新されなくなっていた。

---

## 3. 変更計画

### 3.1 `PDFBinder.Core/Models/PdfPageModel.cs`
- `InkStrokes` プロパティにバッキングフィールド `_inkStrokes` を導入する。
- セッターにおいて、旧コレクションから `StrokesChanged` イベントハンドラを解除し、新コレクションへイベントハンドラを安全に登録する。
- ストローク変更時のハンドラ `OnInkStrokesChanged` で `IsModified = true` および `IsThumbnailDirty = true` を設定する。

### 3.2 `PDFBinder.Core/Services/PdfService.cs`
- `RestoreInkStrokesIfPresent` メソッドにおいて、`pageModel.InkStrokes` に直接代入するのではなく、既存の `pageModel.InkStrokes.Clear()` およびデシリアライズしたストロークの `Add`（既存コレクション再利用）を行う。
- これによりプロパティセッターの安全性担保とコレクションインスタンスの不変性・再利用の双方を適用する。

### 3.3 単体テストの追加 (`PDFBinder.Tests/UnsavedChangesTests.cs`)
- **テスト観点1: 手書き復元直後の未変更状態**
  - 手書きストロークが復元された直後（`ResetModifiedState` 適用後）は `IsModified == false` であること。
- **テスト観点2: 復元後の手書き編集・消去時の変更検知**
  - 手書き復元後のページに対してストロークを追加または削除した際、`page.IsModified == true`、`page.IsThumbnailDirty == true`、および `doc.IsModified == true` に更新されること。
- **テスト観点3: 保存確認プロンプトの発火確認**
  - 手書き復元後にストロークを編集した場合、`MainViewModel.ConfirmSaveAndProceedAsync` を呼び出した際に確認プロンプト（ConfirmSavePrompt）が確実に呼び出されること。

---

## 4. 検証計画
1. **自動テスト**:
   - `dotnet test` を実行し、既存テストおよび新規追加テストがすべて 100% パスすることを確認。
2. **ビルド検証**:
   - `dotnet build` を実行し、エラーおよび警告なくビルドが成功することを確認。
