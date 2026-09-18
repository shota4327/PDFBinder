# [検証報告] Issue #132 印刷時に用紙よりひと回り小さく（約75%に）縮小されて印刷される不具合の修正

## 概要
印刷実行時、「用紙サイズに合わせる」などの設定を行っているにもかかわらず、印刷結果が用紙（A4/A3等）に対してひと回り小さく（約75%に縮小されて）印刷されてしまう問題を修正しました。

原因は、WPF印刷パイプライン（`FixedDocument`, `FixedPage`, `DocumentPaginator`）が 96 DPI（DIU: Device Independent Units）の論理ピクセル空間で動作するのに対し、[`WpfPrintService.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Services/WpfPrintService.cs) において PDF ポイント単位である 72 DPI の値（A4: 595.28 × 841.89 pt）を用紙寸法として直接渡していたためでした（$72 / 96 = 0.75$ となり 75% 縮小）。

ユーザーとの合意に基づき、**方針A（100% 原寸フィット・中央配置）** を採用して用紙寸法計算を 96 DPI に改修し、A4用紙への通常印刷およびA3用紙への冊子中綴じ印刷の双方が縮小されることなく原寸等倍（100%）で用紙中央に綺麗に印刷されるようにしました。

---

## 実施した変更内容

### 1. `PDFBinder.App/Services/WpfPrintService.cs`
- `GetPaperDimensionsInPoints` を `GetPaperDimensionsInDips` に改名・改修し、WPFの標準単位（96 DPI: 1/96 インチ）に基づく正確なピクセル値を返すよう修正：
  - A4: $210 \text{ mm} / 25.4 \times 96 \approx 793.70 \text{ px}$, $297 \text{ mm} / 25.4 \times 96 \approx 1122.52 \text{ px}$
  - A3: $297 \text{ mm} / 25.4 \times 96 \approx 1122.52 \text{ px}$, $420 \text{ mm} / 25.4 \times 96 \approx 1587.40 \text{ px}$
- `FixedDocument.DocumentPaginator.PageSize` および `FixedPage.Width / Height` に 96 DPI 値を適用。
- `GetPaperDimensionsInDips` を `internal static` として公開し、単体テストから直接検証可能に設計。

### 2. `tests/PDFBinder.Tests/WpfPrintServiceTests.cs` [NEW]
- 以下の単体テストを新規作成：
  - A4 縦向き・横向きの 96 DPI 寸法精度検証
  - A3 縦向き・横向きの 96 DPI 寸法精度検証
  - A3横向きの幅の半分（左右1スロット）が A4縦向き寸法と完全一致し、冊子中綴じ印刷時に 100% 原寸等倍が維持されることの検証

### 3. ドキュメントの同期更新
- [`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md): 印刷パイプラインにおける 96 DPI 座標系および原寸等倍配置仕様を追記。
- [`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md): 機能インベントリ F58 に Issue #132 および 96 DPI 原寸等倍印刷を追記。

---

## 検証結果

### 1. 単体テスト（xUnit）
* コマンド: `dotnet test`
* 結果: **全355件 PASS（失敗 0件）**
```
テスト実行を開始しています。お待ちください...
合計 1 個のテスト ファイルが指定されたパターンと一致しました。

成功!   -失敗:     0、合格:   355、スキップ:     0、合計:   355、期間: 3 s - PDFBinder.Tests.dll (net10.0)
```

### 2. ビルド確認
* コマンド: `dotnet build`
* 結果: **警告 0件、エラー 0件でビルド成功**
```
ビルドに成功しました。
    0 個の警告
    0 エラー

経過時間 00:00:02.17
```

### 3. 寸法計算・スケーリングの検証
- **通常A4印刷**: A4用紙サイズが 793.70 × 1122.52 px（96 DPI）となり、従来の 75% 縮小から 100% 原寸等倍へと正しくスケールアップされました。
- **A3冊子印刷**: A3横用紙（1587.40 × 1122.52 px）を左右半分に分割した各スロットが 793.70 × 1122.52 px となり、A4原寸と完全一致して縮小なしで出力されることが数学的・テスト的に保証されました。
