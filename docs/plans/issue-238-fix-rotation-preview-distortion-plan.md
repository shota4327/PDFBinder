# 実装計画: Issue #238 回転後のプレビューが崩れる不具合の修正

## 1. 概要と問題の分析

### 1.1 問題の現象
- PDFページを右回転（90度回転）または他の角度へ回転させた後、新しい用紙サイズ（縦横が入れ替わった領域）に対して回転前のプレビュー画像が `Stretch="Fill"` で引き伸ばされて表示される。

### 1.2 原因の特定
- 詳細エディタ（`DetailEditorView.xaml`）では、ページ表示コンテナ（`Grid`）のサイズが回転後の `DisplayWidth` / `DisplayHeight` に更新される。
- ページ回転時、`DetailPageItemViewModel.ApplyInstantRotation` で一時的に画像が回転された直後、バックグラウンドの動的レンダリングパイプライン（`ScheduleDynamicRender` / `RunDetailViewPipelineAsync`）が起動する。
- 詳細エディタでの高解像度レンダリングには `IPdfRenderer.RenderPageWithInteractiveDataAsync` が使用されている。
- しかし、`PdfiumRenderer.RenderPageWithInteractiveDataAsync` において、引数 `PageRotation rotation` を受け取っているにもかかわらず、**取得した BitmapSource に対して幾何回転（`RotateTransform`）を適用する処理が欠落**していた。
- その結果、回転前の向き（0度回転）のビットマップが返却され、`DetailPageItemViewModel.PageBackground` にセットされていた。
- `DetailEditorView.xaml` では `<Image Source="{Binding PageBackground}" Stretch="Fill" />` で表示されるため、横長になった用紙領域に縦長の回転前ビットマップが引き伸ばされ、プレビューが著しく歪んで表示されていた。
- 同様に、`PdfiumRenderer.RenderPagesBatchAsync` においても引数 `PageRotation rotation` に対する幾何回転処理が欠落していた。

---

## 2. 修正方針

### 2.1 `PdfiumRenderer.cs` の修正
1. **`RenderPageWithInteractiveDataAsync`**:
   - `BitmapSource.Create` で作成したビットマップに対し、`rotation != PageRotation.Rotate0` の場合に `BitmapTransformHelper.CreateRotatedBitmap`（または `TransformedBitmap` + `RotateTransform`）を適用して幾何回転を行うよう修正する。
2. **`RenderPagesBatchAsync`**:
   - 同様に `BitmapSource.Create` で作成したビットマップに対し、`rotation != PageRotation.Rotate0` の場合に幾何回転を適用して結果ディクショナリに格納するよう修正する。

### 2.2 単体テストの追加・検証
- `tests/PDFBinder.Tests/PdfiumRendererTests.cs`:
  - `RenderPageWithInteractiveDataAsync` に `PageRotation.Rotate90` を渡した際、生成されるビットマップが正しく90度回転（幅と高さが反転）していることを検証するテストケースを追加。
  - `RenderPagesBatchAsync` に `PageRotation.Rotate90` を渡した際、各ページのビットマップが正しく回転していることを検証するテストケースを追加。

---

## 3. 実施手順
1. **実装計画の作成・記録**: 本計画書（アーティファクトおよび `docs/plans/issue-238-fix-rotation-preview-distortion-plan.md`）の作成。
2. **コード修正**:
   - `src/PDFBinder.Core/Services/PdfiumRenderer.cs` の `RenderPageWithInteractiveDataAsync` および `RenderPagesBatchAsync` に回転処理を追加。
3. **テスト追加**:
   - `tests/PDFBinder.Tests/PdfiumRendererTests.cs` に回転検証テストを追加。
4. **ビルドおよびテスト実行**:
   - `dotnet test` で全テストが PASS することを確認。
   - `dotnet build` でビルド警告・エラーがないことを確認。
5. **検証報告（Walkthrough）の作成**:
   - `docs/plans/issue-238-fix-rotation-preview-distortion-walkthrough.md` の作成。
