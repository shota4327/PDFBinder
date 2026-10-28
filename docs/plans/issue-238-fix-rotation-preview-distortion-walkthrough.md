# 検証報告: Issue #238 回転後のプレビューが崩れる不具合の修正

## 1. 修正概要
ページ回転後に詳細エディタで新しい用紙サイズに対して回転前のプレビューがストレッチ配置されて引き伸ばされた表示となる不具合（Issue #238）を修正しました。

### 根本原因
- 詳細ビューの動的レンダリングで使用される `PdfiumRenderer.RenderPageWithInteractiveDataAsync` において、`PageRotation rotation` 引数を受け取っていながら、生成されたビットマップに対して幾何回転（`TransformedBitmap`）を適用する処理が欠落していました。
- 同様に `PdfiumRenderer.RenderPagesBatchAsync` においても回転処理が欠落していました。
- これにより、回転操作後にバックグラウンド動的レンダリングが完了したタイミングで、回転前の向き（0度回転）のビットマップで `PageBackground` が上書きされ、縦横比が入れ替わった用紙領域（`DisplayWidth` × `DisplayHeight`）に `Stretch="Fill"` で引き伸ばされてプレビューが崩れていました。

---

## 2. 実施した変更内容

### 2.1 `src/PDFBinder.Core/Services/PdfiumRenderer.cs`
- **`RenderPageWithInteractiveDataAsync`**:
  - `BitmapSource.Create` 完了後、`rotation != PageRotation.Rotate0` の場合に `TransformedBitmap` による幾何回転および `Freeze()` を適用する処理を追加しました。
- **`RenderPagesBatchAsync`**:
  - 各ページのビットマップ生成後、`rotation != PageRotation.Rotate0` の場合に幾何回転を適用して結果ディクショナリへ格納するよう修正しました。

### 2.2 単体テストの追加 (`tests/PDFBinder.Tests/PdfiumRendererTests.cs`)
- `RenderPageWithInteractiveDataAsync_WithRotation90_RotatesBitmapCorrectly`:
  - 400x600の縦長PDFに対し、`PageRotation.Rotate0`（縦長）と `PageRotation.Rotate90`（横長）でレンダリングを実行し、90度回転後のビットマップの PixelWidth と PixelHeight が正しく反転していることを検証。
- `RenderPagesBatchAsync_WithRotation90_RotatesAllBitmapsCorrectly`:
  - 複数ページPDFに対し、`PageRotation.Rotate90` でのバッチレンダリングを実行し、全ページのビットマップが正しく90度回転（幅 > 高さ）していることを検証。

### 2.3 バージョンおよびドキュメント更新
- `Directory.Build.props`: `0.14.0` → `0.14.1` にインクリメント
- `CHANGELOG.md`: `## [0.14.1] - 2026-10-28` リリースノート（エンドユーザー向け表記）を追加
- `docs/PROJECT.md`: F11（ページ回転）に Issue #238 の対応を反映

---

## 3. 検証結果

### 3.1 単体テスト実行結果
```
C:\Git\PDFBinder\tests\PDFBinder.Tests\bin\Debug\net10.0-windows\PDFBinder.Tests.dll (.NETCoreApp,Version=v10.0) のテスト実行
成功!   -失敗:     0、合格:   668、スキップ:     0、合計:   668、期間: 8 s - PDFBinder.Tests.dll (net10.0)
```
- 新規追加したテストを含む **全668テストが 100% PASS** することを確認しました。

### 3.2 ビルド検証結果
```
ビルドに成功しました。
    0 個の警告
    0 エラー
```
- 警告およびエラーが 0 件であることを確認しました。
