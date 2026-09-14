# Issue #52 追加改修-2 Walkthrough: 表示オプション切り替え時の即時反映

## 概要
表示オプション（100%、ウィンドウに合わせる、幅に合わせる）のラジオボタンをクリックして切り替えた瞬間、およびPDFファイルを読み込んだ直後にも、即座に最適な拡大率が再計算・適用されるように改善を行いました。
（※幅ボタン等のアイコン修正については、別途Google Fontsへの移行作業にて一括対応するため本スコープから除外しています）

---

## 主な変更内容

### 1. 表示オプション（FitMode）変更時の即時再計算
- **`DetailEditorViewModel.cs`**:
  - `partial void OnFitModeChanged(DetailViewFitMode value)` を追加。
  - ラジオボタンの TwoWay バインディングにより `FitMode` が更新された瞬間、即座に `ApplyFitMode()` を呼び出し、ウィンドウリサイズを待たずにその場で拡大率を再計算・画面へ反映。

### 2. PDFファイル初回読み込み直後の即時フィット適用
- **`DetailEditorViewModel.cs`**:
  - `InitializeDocument(PdfDocumentModel document)` において、ページ生成・カレントページ確定直後に `ApplyFitMode()` を実行。
  - ファイルを開いた初回表示時から、デフォルト設定である「ウィンドウに合わせる」が最適な倍率で即座に適用されるように対応。

---

## 検証結果

### 1. 単体テスト（xUnit）
`PageNavigationAndViewOptionsTests.cs` に新規テスト 2 件を追加し、以下を含む全146件のテストが100%合格することを確認しました。

- `FitModePropertySetter_ShouldImmediatelyRecalculateZoom`: `FitMode` プロパティの直接変更（ラジオボタン操作相当）時に即座に `Zoom` が再計算されること
- `InitializeDocument_ShouldApplyFitModeImmediately`: `InitializeDocument` 実行時に新しいドキュメントのページ寸法に合わせて即座に拡大率が適用されること

```text
成功!   -失敗:     0、合格:   146、スキップ:     0、合計:   146、期間: 921 ms - PDFBinder.Tests.dll (net10.0)
```

### 2. ビルド検証
```text
dotnet build
ビルドに成功しました。
    0 個の警告
    0 エラー
```
