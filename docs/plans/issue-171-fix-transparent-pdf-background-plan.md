# 実装計画: 透明背景PDFがグリッドビューで黒背景として表示される問題の解消

## 1. 概要・背景
PDFファイル（特にTeX、Word、問題用紙などのベクターPDF）には、ページ全体を覆う白色背景の矩形が描画されていない（PDF仕様上、用紙背景が透明扱いとなる）ものが多数存在します。
本アプリでは、PDFium（Docnet.Core）を用いてレンダリングする際、未描画部分が透明ピクセル（`A=0, R=0, G=0, B=0`）のまま出力されます。

- **詳細ビュー（DetailEditorView）**: 背面に `<Border Background="White">` が存在するため、透過して白色用紙として正常に表示される。
- **グリッドビュー（GridView）**: サムネイル枠（`PageBorder`）の背景色がダークテーマ色 `SurfaceBackgroundBrush`（#252526）になっているため、透明ピクセルを通して暗色が透けて黒く表示されてしまう。

本改修では、レンダラー（`PdfiumRenderer`）側で白背景合成（Alpha Blending over White）を行い、かつグリッドビュー（`GridView.xaml`）のカード背景も書類用紙色である白に統一することで、すべてのビューや印刷・サムネイル処理において一貫して自然な白背景表示を実現します。

---

## 2. 変更対象ファイル
1. `src/PDFBinder.Core/Services/PdfiumRenderer.cs`:
   - Docnet から取得した `rawBytes` に対して、未描画・半透明ピクセルを白色（#FFFFFF）の上にアルファ合成するヘルパーメソッド `CompositeOverWhite` を追加・呼び出し。
2. `src/PDFBinder.App/Views/GridView.xaml`:
   - サムネイル用カード枠 `PageBorder` の `Background` を `White` に変更。
3. `tests/PDFBinder.Tests/PdfiumRendererTests.cs`:
   - 背景矩形のない透明PDFをレンダリングした際に、背景が不透明な白（RGBAすべて255）として出力されることを検証する単体テストを追加。
4. `Directory.Build.props`:
   - 不具合修正に伴うパッチバージョンインクリメント（`0.1.1` → `0.1.2` 等）。
5. `CHANGELOG.md`:
   - エンドユーザー向けに変更内容を記述。

---

## 3. 実装詳細

### 3.1 PdfiumRenderer.cs
```csharp
// rawBytes 取得直後に白背景合成を実行
CompositeOverWhite(rawBytes);
```
Docnet は Premultiplied Alpha 形式の BGRA バイト列を返却するため、白色合成の計算式は以下となります:
- $C_{out} = C_{src} + (255 - A_{src})$ （$0 \le C_{out} \le 255$）
- $A_{out} = 255$

整数演算のみで高速に処理でき、浮動小数点計算や除算を必要としないため、レンダリング速度への影響は極めて軽微（1ms未満）です。

### 3.2 GridView.xaml
`PageBorder` の `Background="{StaticResource SurfaceBackgroundBrush}"` を `Background="White"` に変更します。

---

## 4. 検証手順
1. `dotnet test`: 全単体テストが PASS すること（追加テスト含む）。
2. `dotnet build`: 警告・エラーなくビルドが成功すること。
