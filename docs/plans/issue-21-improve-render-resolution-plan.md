# 実装計画: レンダリング解像度の向上および手書きストロークのサムネイル合成反映

## 概要
手書きエディタ（詳細ビュー）およびサムネイル一覧（グリッドビュー）のレンダリング解像度を引き上げ、ズームイン時や高DPI環境での鮮明な表示を実現します。
また、手書き編集内容（ペン・蛍光ペン・直線）をサムネイル上に縮小合成する仕組みを導入し、グリッド一覧に戻った際に手書きメモが一目で確認できるようにします。
これは将来予定されている「詳細ビューの縦連続スクロール化（デフォルト化）」を見据えた、UI仮想化・メモリ肥大化防止の分離設計基盤となります。

## ユーザーレビュー事項
> [!NOTE]
> - **手書きエディタ解像度**: 従来の 108 DPI相当（1.5倍）から **216 DPI相当（72 pt * 3.0倍）** へ引き上げます。A4サイズで約 1785 × 2526 ピクセル（メモリ約 18 MB/ページ）となり、ズーム2〜3倍でも文字がぼやけなくなります。
> - **サムネイル解像度**: スライダーの現在値にかかわらず、**最大表示サイズ（幅 360 px、高さ 504 px）** を基準に固定レンダリングします。拡大しても高DPIディスプレイでもくっきり表示されます。
> - **手書きサムネイル合成**: 詳細エディタからグリッドビューへ切り替えたタイミングで、WPF標準の `DrawingVisual` + `RenderTargetBitmap` を用いて手書きストローク（`InkStrokes`）をサムネイル上に縮小描画・合成します。手書き操作中のペン追従性には一切影響を与えません。

## 変更内容

### 1. コアサービス層 (`PDFBinder.Core`)
#### [MODIFY] [`IPdfRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/IPdfRenderer.cs)
- ストローク合成用のメソッド定義を追加：
  ```csharp
  BitmapSource CompositeStrokes(
      BitmapSource baseImage,
      System.Windows.Ink.StrokeCollection strokes,
      double originalPageWidth,
      double originalPageHeight);
  ```

#### [MODIFY] [`PdfiumRenderer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/PdfiumRenderer.cs)
- `CompositeStrokes` の実装：
  - `DrawingVisual` と `RenderTargetBitmap` を使用して、背景ビットマップ上に `StrokeCollection.Draw(dc)` をサムネイル比率に合わせて縮小描画。
  - ストロークが空の場合はそのまま `baseImage` を返却する最適化。

---

### 2. アプリケーション・ViewModel層 (`PDFBinder.App`)
#### [MODIFY] [`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- 背景PDFのレンダリング解像度倍率を `3.0`（216 DPI相当）に引き上げ：
  ```csharp
  const double EditorRenderScale = 3.0;
  int targetWidth = (int)(CurrentPage.Width * EditorRenderScale);
  int targetHeight = (int)(CurrentPage.Height * EditorRenderScale);
  ```

#### [MODIFY] [`MainViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- サムネイル生成時のレンダリング解像度定数（幅 360px、高さ 504px）を導入し、`GenerateThumbnailsAsync` および白紙追加時に適用。
- `ClosePageDetail()` メソッドを改善：
  - 詳細エディタを閉じる際、編集されたページについて `CompositeStrokes` を呼び出して手書きストロークを反映したサムネイルを即座に更新・反映。

---

### 3. 単体テスト層 (`PDFBinder.Tests`)
#### [MODIFY] [`PdfiumRendererTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/PdfiumRendererTests.cs)
- `CompositeStrokes` によるストローク合成処理のテストケースを追加（空ストローク、ストロークあり時のビットマップ生成検証）。

#### [MODIFY] [`ViewModelsTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ViewModelsTests.cs)
- `DetailEditorViewModel` のレンダリング呼び出しサイズ検証。
- `MainViewModel.ClosePageDetail` 実行時に手書きストロークがサムネイルに合成反映される動作の検証。

---

## 検証計画

### 自動テスト
- `dotnet test tests/PDFBinder.Tests/PDFBinder.Tests.csproj` を実行し、既存テストおよび新規追加テストが全件 PASS することを確認。
- `dotnet build -c Release` を実行し、警告・エラーなく正常にビルドできることを確認。

### 手動検証（動作確認）
1. アプリを起動し、サンプルPDFを読み込む。
2. サムネイル一覧でスライダーを最大（360px）まで拡大し、サムネイルの文字や線が鮮明に表示されることを確認。
3. ページをダブルクリックして詳細エディタを開き、ズームスライダーを 200%〜300% に拡大した状態で背景PDFの文字がぼやけず鮮明に読めることを確認。
4. ペンや蛍光ペンで文字や図を手書きし、「グリッドに戻る」ボタンをクリック。
5. グリッド一覧のサムネイル上に、今手書きした内容が正しく縮小描画されて表示されていることを確認。
