# 実装計画: Issue #225 画像ページおよび空白ページの描画における不要なメモリ確保・フルデコードの改善

## 1. 概要
- **Issue**: #225 `[性能改善][影響度: 中] 画像ページ（JPG/PNG）および空白ページの描画における不要なメモリ確保・フルデコードの改善`
- **作業ブランチ**: `issue-225-optimize-image-and-blank-rendering`
- **目的**:
  `PdfiumRenderer` における画像ページ（JPG/PNG）および空白ページ（白紙ページ）のレンダリング処理を見直し、不要なフル解像度デコードや大容量 `byte[]` 配列（LOH）のアロケーションを削減することで、描画速度の向上とメモリ使用量・GC負荷の低減を実現する。

---

## 2. 現状の課題
1. **画像ページ描画（`PdfiumRenderer.RenderImagePage`）**:
   - サムネイル生成や詳細ビュー表示のたびに `File.ReadAllBytes(filePath)` で画像ファイル全体を `byte[]` に読み込み、`BitmapDecoder.Create` でフル解像度デコードした後に `CreateDetachedBitmap`（全ピクセルコピー）および `TransformedBitmap` による縮小を行っている。
   - 高解像度画像（例: 4000×3000px）のサムネイル生成時にも毎回フル解像度分のメモリ確保とデコード処理が発生する。
2. **空白ページ描画（`PdfiumRenderer.CreateBlankPageBitmap`）**:
   - 描画要求のたびに `new WriteableBitmap(w, h, ...)` および `new byte[w * h * 4]` を確保し、`Array.Fill` で全要素を白埋めしている。
   - 高倍率ズーム時などには単なる白紙のために数MB〜最大256MB（8192×8192px時）もの大容量メモリが LOH（ラージオブジェクトヒープ）に確保され、GC負荷・メモリ圧迫の原因となる。

---

## 3. 改修設計方針

### 3.1 `PdfiumRenderer.RenderImagePage` のストリーム読み込み＆縮小デコード化
- **対象ファイル**: `src/PDFBinder.Core/Services/PdfiumRenderer.cs`
- **変更内容**:
  1. `File.ReadAllBytes` + `MemoryStream` を廃止し、`FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)` でストリーム読み込みを行う（ファイルロック回避を維持）。
  2. `BitmapImage` を以下の設定で初期化する:
     - `CreateOptions = BitmapCreateOptions.IgnoreColorProfile`（既存挙動を維持）
     - `CacheOption = BitmapCacheOption.OnLoad`（`EndInit()` 完了時にストリームへの参照を即時解放し、ファイルロックを残さない）
     - `StreamSource = fs`
     - 回転適用前のターゲット幅・高さ（`Rotate90` / `Rotate270` の場合は `targetWidth` と `targetHeight` を入れ替えた値）を `DecodePixelWidth` / `DecodePixelHeight` に設定し、WIC デコーダー段階でターゲット解像度へ直接デコードさせる。
  3. 取得した `BitmapImage` を `Freeze()` し、Issue #170 で対応した別スレッドからの `TransformedBitmap.Freeze()`（即時幾何回転プレビュー）時のスレッドアフィニティ安全性も担保する（必要に応じて縮小デコード済みビットマップに対して安全にデタッチ・フリーズする）。
  4. `rotation != PageRotation.Rotate0` の場合は `BitmapTransformHelper.CreateRotatedBitmap` を適用し、万一デコード結果がターゲット寸法と一致しない場合のフォールバックとして `ScaleTransform` を維持する。

### 3.2 `PdfiumRenderer.CreateBlankPageBitmap` の省メモリ化
- **対象ファイル**: `src/PDFBinder.Core/Services/PdfiumRenderer.cs`
- **変更内容**:
  1. クラス内にフリーズ済みの **1×1 白色ピクセル `BitmapSource`**（`SingleWhitePixelBitmap`、4バイト固定・スレッド非依存）を静的フィールドとして1つだけ保持する。
  2. `CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)` では、`w` / `h` を算出（90度/270度時は縦横入替）した後、`w == 1 && h == 1` なら `SingleWhitePixelBitmap` をそのまま返却し、それ以外は `new TransformedBitmap(SingleWhitePixelBitmap, new ScaleTransform(w, h))` を生成して `Freeze()` して返却する。
  3. これにより、高倍率ズーム時（例: 8192×8192px）でも `w * h * 4` バイトの巨大配列・`WriteableBitmap` 確保を完全にゼロ（LOH アロケーションゼロ）にしつつ、既存の呼び出し元およびテストが期待する `PixelWidth`、`PixelHeight`、アスペクト比、`IsFrozen`、白ピクセル描画、`CompositeStrokes` 合成、別スレッド回転のすべてを 100% 互換維持する。

### 3.3 単体テストの追加・検証
- **対象ファイル**: `tests/PDFBinder.Tests/PdfiumRendererTests.cs`
- **追加テストケース**:
  1. **画像ページ縮小デコード＆非ロック検証**:
     - 高解像度画像（例: 1200×800px の PNG/JPEG）を小さなターゲットサイズ（例: 150×100px、および 90度回転時の 100×150px）で `RenderPageAsync` した場合に、正しい寸法・フリーズ状態で返却され、別スレッドからの追加回転変換（Issue #170 回帰防止）およびレンダリング直後のファイル上書き（ファイルロックなし）が成功すること。
  2. **空白ページ省メモリ化＆高解像度・白ピクセル・別スレッド回転検証**:
     - `CreateBlankPageBitmap` で大サイズ（例: 4096×4096px や 8192×8192px）を生成しても例外・大容量確保なく即座に完了し、`PixelWidth` / `PixelHeight` / `IsFrozen` が正しく、四隅・中央のピクセルが白 `(255, 255, 255, 255)` であること。
     - `Rotate0` / `Rotate90` / `Rotate180` / `Rotate270` の各回転および 0 以下の境界値入力に対して正しく寸法計算され、別スレッドからの `CreateRotatedBitmap` も安全に動作すること。

---

## 4. 実装・検証手順
1. `src/PDFBinder.Core/Services/PdfiumRenderer.cs` の `CreateBlankPageBitmap` および `RenderImagePage` を修正（30〜50行目安のメソッド分割・日本語コメント維持）。
2. `tests/PDFBinder.Tests/PdfiumRendererTests.cs` に新規単体テストを追加。
3. `dotnet test` を実行し、既存テスト（`MainViewModelImageTests`、`BitmapTransformHelperTests` を含む全テスト）および新規テストが 100% PASS することを確認。
4. `dotnet build` を実行し、警告・エラーゼロを確認。
5. 必要に応じて関連ドキュメント（`docs/basic_design.md` 等）を確認・同期し、Walkthrough（検証報告）を作成してユーザーへ報告。
