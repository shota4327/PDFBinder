# 手書き文字の再編集可能な保存・復元 (Issue #89) 検証報告 (Walkthrough)

## 実装概要 (Overview)
保存した PDF ファイルを再度開いた際に、手書き文字が消しゴムで消せなくなる問題を解消するため、**「アピアランス付き専用注釈（`/AP /N`） ＋ ISFメタデータ埋め込み ＆ 背景レンダリング時分離」** 方式を実装しました。
これにより、**他社製 PDF リーダー（Adobe Acrobat、Microsoft Edge 等）では高精細なベクター手書きとして閲覧・印刷可能**でありながら、**PDF Binder では筆圧・色・太さ・蛍光ペンを含め 100% 元通りに消しゴム消去・追記・再保存が可能**になりました。

---

## 変更内容 (Changes Made)

### 1. コアモデル層 (`PDFBinder.Core/Models`)
- [`PdfBinderInkAnnotation.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Models/PdfBinderInkAnnotation.cs) [NEW]
  - `PdfAnnotation` を継承した専用注釈クラスを新設。
  - `SerializeStrokes`: WPF `StrokeCollection` を完全可逆な ISF バイナリ（Base64形式）にシリアライズ。
  - `DeserializeStrokes`: Base64 から `StrokeCollection` を 100% の描画属性で復元。
  - `CreateAppearanceStream`: `XForm` および `XGraphics.DrawLines` を用いて、他社リーダー表示用の `/AP /N` Form XObject ストリームを生成。
  - `RemoveBinderInkAnnotations` / `HasBinderInkAnnotation`: ページ内の自前注釈を検出・クリーンアップ。

### 2. PDF入出力サービス層 (`PDFBinder.Core/Services`)
- [`PdfService.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfService.cs) [MODIFY]
  - **保存処理**: 本文コンテンツストリーム（`/Contents`）への焼き込みを廃止し、既存の自前注釈をクリーンアップした上で、最新ストロークが存在する場合に `PdfBinderInkAnnotation`（アピアランス＋ISF）を `/Annots` 配下に配置するよう改修。
  - **読み込み処理**: `LoadDocumentAsync` 時にページの `/Annots` を走査し、`/PdfBinderInk` を検出した場合は ISF から `StrokeCollection` を復元して `pageModel.InkStrokes` に設定。

### 3. PDFレンダリングサービス層 (`PDFBinder.Core/Services`)
- [`PdfiumRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs) [MODIFY]
  - `RenderPageAsync`: 対象ページに `/PdfBinderInk` が存在する場合、メモリ上で該当注釈のみを除外した PDF バイト列を Docnet（PDFium）に渡してレンダリング。
  - `pageReader.GetImage(RenderFlags.RenderAnnotations)`: 他社製注釈（テキストコメント・付箋・ハイライト等）は 100% 忠実に背景描画しつつ、PDF Binder の手書きのみを背景画像から除外（最前面の `EditorInkCanvas` のみに展開することで、消しゴム消去時のゴースト・二重描画を根絶）。

### 4. 単体テスト層 (`PDFBinder.Tests`)
- [`ReEditableInkAnnotationTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ReEditableInkAnnotationTests.cs) [NEW]
  - `SaveAndReload_PreservesInkStrokesWithAllAttributes`: ペン・蛍光ペンの色・太さ・座標・属性が保存・再読込で完全復元されることを検証。
  - `EraseStrokesAndResave_UpdatesInkStrokesAndPdfFile`: 復元ストロークの一部消去（消しゴム）および全消去が上書き保存に正しく反映されることを検証。
  - `ThirdPartyAnnotation_PreservedWhenSavingAndReloadingBinderInk`: 外部PDFのリンク注釈とPDF Binderの手書き注釈が共存・保持されることを検証。
  - `RenderPageAsync_ExcludesBinderInkFromRasterizedBackground`: 背景レンダリング画像に手書き線が焼き込まれず、完全に分離されていることを検証。

### 5. ドキュメント更新
- [`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md): 第7項を手書き注釈の再編集可能保存仕様に更新。
- [`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md): F46 機能およびテスト件数（240件全PASS）を更新。

---

## 検証結果 (Verification Results)

### 自動テスト結果
```pwsh
dotnet test
```
```text
C:\Git\PDFBinder\tests\PDFBinder.Tests\bin\Debug\net10.0-windows\PDFBinder.Tests.dll (.NETCoreApp,Version=v10.0) のテスト実行
VSTest のバージョン 18.0.1 (x64)

テスト実行を開始しています。お待ちください...
合計 1 個のテスト ファイルが指定されたパターンと一致しました。

成功!   -失敗:     0、合格:   240、スキップ:     0、合計:   240、期間: 1 s - PDFBinder.Tests.dll (net10.0)
```
全 240 件の単体テストがすべて 100% 成功しました。

### ビルド結果
```pwsh
dotnet build
```
```text
ビルドに成功しました。
    0 個の警告
    0 エラー

経過時間 00:00:01.33
```
警告およびエラーなくビルドが成功しました。
