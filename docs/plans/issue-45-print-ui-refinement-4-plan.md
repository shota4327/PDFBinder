# Issue #45 印刷ダイアログ UI/UXデザイン調整（第4弾）実装計画

## 概要
ユーザーからの追加フィードバックに基づき、印刷ダイアログの用紙設定の横並び化、ボタン幅の統一、表記文言の修正、プリンターフォントサイズ拡大、およびポップアップの左揃え修正を実施します。

---

## 決定事項（ユーザー確認済み）
1. **用紙の向きと用紙サイズの横並び化**:
   - これまで縦に並んでいた「用紙の向き」と「用紙サイズ」の各ブロックを、Gridを用いて2列で横並びに配置（左列: 用紙の向き、右列: 用紙サイズ）。
2. **ボタン横幅の統一（160px）**:
   - 印刷範囲（すべて、現在のページ、ページ指定）および印刷モード（サイズに合わせる、1ページに集約、冊子(製本)）の各ボタンの横幅をすべて `Width="160"`、`HorizontalContentAlignment="Center"` に統一し、整列した美しいレイアウトにする。
3. **文言の修正**:
   - 「1Pに集約」の表記を「1ページに集約」に変更。
4. **プリンタープルダウンの文字拡大**:
   - プリンター選択プルダウンの文字サイズを `FontSize="14"` に拡大し、主要設定項目としての視認性を向上。
5. **プルダウンポップアップの左揃え修正**:
   - Windowsのタッチ/右手利き設定（`SystemParameters.MenuDropAlignment == true`）に起因するポップアップの右揃え（左展開）現象を、`App.xaml.cs` での初期化補正（`EnsureLeftAlignedPopups`）およびスタイルでの `PlacementTarget` 明示により解消し、常にトグルボタンの左端に合わせて右側に展開するように修正。

---

## 変更ファイル・内容

### 1. `src/PDFBinder.App/App.xaml.cs`
- アプリケーション起動時（`OnStartup`）に `EnsureLeftAlignedPopups()` を実行し、`SystemParameters.MenuDropAlignment` をリフレクションで `false` に補正。これにより全ポップアップ・ドロップダウンを確実に左揃えにする。

### 2. `src/PDFBinder.App/App.xaml`
- `ModernDarkComboBoxStyle`:
  - `PART_Popup` に `PlacementTarget="{Binding ElementName=ToggleButton}"` を明示。
  - 必要に応じて最小幅 `MinWidth="220"` を設定し、安定した左揃えドロップダウン表示を実現。

### 3. `src/PDFBinder.App/MainWindow.xaml`
- プリンター ComboBox:
  - `FontSize="14"` を指定。
- 用紙の向きと用紙サイズ:
  - 2列の `Grid`（`ColumnDefinition Width="*"`, `Width="16"`, `Width="*"`）に横並びで配置。
- 印刷範囲・印刷モード:
  - 各 `RadioButton` に `Width="160"` および `HorizontalContentAlignment="Center"` を指定。
  - 「1Pに集約」を「1ページに集約」に変更。

### 4. ドキュメント
- `docs/basic_design.md` の 6.8 節の表記を更新。

---

## 検証計画
### 自動テスト
- `dotnet test`: 既存の全単体テスト（305件）がすべて PASS することを確認。
- `dotnet build`: 警告・エラーがないことを確認。

### 手動検証・UI確認
- 印刷ダイアログを開き、以下を確認:
  1. 用紙の向きと用紙サイズが横並びになっていること。
  2. 印刷範囲・印刷モードのボタン横幅が 160px で均一に揃っていること。
  3. 「1ページに集約」と表示されていること。
  4. プリンター選択のフォントサイズが大きくなっていること。
  5. プリンターのポップアップがボタンの左端に揃って開くこと。
