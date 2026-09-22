# [実装計画] Issue #132 印刷時に用紙よりひと回り小さく（約75%に）縮小されて印刷される不具合の修正

## 概要
印刷実行時、「用紙サイズに合わせる」などの設定を行っているにもかかわらず、印刷結果が用紙（A4/A3等）に対してひと回り小さく（約75%に縮小されて）印刷されてしまう問題を修正します。

原因は、WPFの印刷パイプライン（`FixedDocument`, `FixedPage`, `DocumentPaginator`）が 96 DPI（DIU: Device Independent Units）の仮想ピクセル空間で動作するのに対し、[`WpfPrintService.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Services/WpfPrintService.cs) で用紙寸法を算出する際に PDF のポイント単位である 72 DPI の値（A4: 595.28 × 841.89 pt）をそのまま設定していたためです（$72 / 96 = 0.75$ となり、ちょうど 75% に縮小）。

ユーザーとの合意事項に基づき、**方針A（100% 原寸フィット・中央配置）** を採用し、A4用紙への通常印刷およびA3用紙への冊子中綴じ印刷の双方が縮小されることなく原寸等倍で用紙いっぱいに綺麗に印刷されるように修正します。

---

## 変更対象ファイルと詳細計画

### 1. `PDFBinder.App/Services/WpfPrintService.cs`
- **用紙サイズ計算の修正（72 DPI → 96 DPI）**:
  - `GetPaperDimensionsInPoints` メソッドを `GetPaperDimensionsInDips` に改名・改修し、WPFの標準単位（96 DPI: 1/96 インチ）に基づく正確なピクセル値を算出：
    - A4: $210 \text{ mm} / 25.4 \times 96 \approx 793.70 \text{ px}$, $297 \text{ mm} / 25.4 \times 96 \approx 1122.52 \text{ px}$
    - A3: $297 \text{ mm} / 25.4 \times 96 \approx 1122.52 \text{ px}$, $420 \text{ mm} / 25.4 \times 96 \approx 1587.40 \text{ px}$
  - `FixedDocument.DocumentPaginator.PageSize` および `FixedPage.Width / Height` に 96 DPI 値を適用。
- **画像配置・スケーリングの整合性確保**:
  - `CreatePlacedImage` において、`normalizedBounds` から各スロットの寸法（96 DPI基準）を算出し、`Image` の `Stretch = Stretch.Uniform` により原稿のアスペクト比を保ったまま 100% 原寸（A4→A4、A4→A3冊子半分）で用紙中央に綺麗にフィットさせます。
- **メソッドのテスト容易性確保**:
  - 用紙寸法計算ロジック（`GetPaperDimensionsInDips`）を `internal static` として公開（`[InternalsVisibleTo("PDFBinder.Tests")]` は既存設定を確認・必要に応じて適用）、単体テストから直接検証可能にします。

### 2. `tests/PDFBinder.Tests/WpfPrintServiceTests.cs` [NEW]
- **単体テストの新規作成**:
  - A4 縦向き・横向きの 96 DPI 寸法が期待値（793.70 × 1122.52 px）と一致することを検証。
  - A3 縦向き・横向きの 96 DPI 寸法が期待値（1122.52 × 1587.40 px）と一致することを検証。
  - 冊子形式（A3横）における左右各スロットの幅・高さが A4 縦向き寸法（793.70 × 1122.52 px）と完全一致（100% 原寸）することを検証。

### 3. ドキュメントの同期更新
- `docs/basic_design.md`: 印刷パイプラインにおける 96 DPI 座標系および原寸フィット仕様の明記。
- `docs/PROJECT.md`: Issue #132 の反映。

---

## 検証計画

### 自動テスト（Automated Tests）
- `dotnet test`
  - 新規作成した `WpfPrintServiceTests` を含むすべての単体テストが 100% PASS することを確認。

### ビルド確認
- `dotnet build`
  - 警告およびエラーなくビルドが成功することを確認。

### 手動・論理的検証
- A4原稿印刷時: 幅 793.70 px、高さ 1122.52 px となり、縮小率 1.0（100% 原寸）で FixedPage に配置されることの検証。
- A3冊子印刷時: 各スロットが幅 793.70 px、高さ 1122.52 px となり、A4原寸と完全一致することの検証。
