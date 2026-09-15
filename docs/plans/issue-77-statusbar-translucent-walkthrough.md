# Issue #77 ステータスバーの半透明化およびサイズ拡大 検証報告（Walkthrough）

## 変更概要
Issue #77 の要件に基づき、下部ステータスバーを半透明オーバーレイ配置（下端ドック型、半透明白 `#99FFFFFF`）とし、高さを約5px拡大（MinHeight 42px、Padding 12,5）するとともに、内部のボタン（28x28px）およびページ入力欄（高さ26px）を連動拡大しました。
また、初期実装後のフィードバックに基づき、透け感の最適化（#E6FFFFFF → #99FFFFFF、約60%不透明度）と、単一ページ表示（FitToWindow）時にスクロールバーが誤出現していた問題（パディング不整合）を解消しました。

---

## 修正・改善内容

### 1. 半透明度の調整（透け感の最適化）
- 当初設定した `#E6FFFFFF`（約90%不透明度）では白が濃すぎて背後の透過が肉眼で視認しづらかったため、`#99FFFFFF`（約60%不透明度）に変更しました。
- 上部境界線を `#B0CBD5E1` に設定し、背後のPDFドキュメントやサムネイルカードがステータスバー越しに上品かつ明確に透けて見えるように調整しました。

### 2. 単一ページ表示時のスクロールバー誤出現解消
- `DetailEditorView.xaml` の `DetailScrollViewer` に設定していた下部余白パディング（`Padding="5,5,5,48"`）により、ViewModelのフィット計算（`TotalVerticalMargin = 50px`）との不整合が生じ、`FitToWindow` 時に垂直スクロールバーが常時出現していた問題を解消しました。
- `DetailScrollViewer` のパディングを元の `Padding="5"` に戻し、`GridView.xaml` のパディングも `Padding="20"` に戻すことで、計算値と実レイアウトを完全一致させ、単一ページ表示時に余計なスクロールバーが一切出現しないよう修正しました。
- これにより、グリッドおよびエディタのコンテンツがステータスバーの下に自然に潜り込み、スクロール時に半透明効果を存分に体感できるようになりました。

---

## 変更ファイル一覧
1. **[MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)**:
   - `Grid.RowDefinitions` の最下行（Row 3: `Height="Auto"`）を廃止し、コンテンツ領域（Row 2: `Height="*"`）をウィンドウ下端まで拡張。
   - ステータスバー `Border` を `Grid.Row="2"` に配置し、`VerticalAlignment="Bottom"` および `Panel.ZIndex="10"` を指定してオーバーレイ化。
   - 背景色を `Background="#99FFFFFF"`（約60%不透明の半透明白）、境界線を `BorderBrush="#B0CBD5E1"` に変更。
   - `MinHeight="42"`、`Padding="12,5"` に拡大。
   - ページ移動ボタン（前へ・次へ）およびズームボタン（縮小・拡大）のサイズを `28x28px` に拡大、アイコンフォントサイズを `12px` / `13px` に調整。
   - ページ番号入力欄 `TextBox` の高さを `26px`、幅を `40px` に拡大。
   - ズームリセットボタンの高さを `28px`、最小幅を `50px` に拡大。
   - 未保存変更確認ダイアログの `Grid.RowSpan` を `3` に調整。
2. **[GridView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml)**:
   - パディング整合性を保ち、カードがステータスバー背後に潜り込んで半透明に透けるよう `Padding="20"` を維持。
3. **[DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
   - `DetailScrollViewer` のパディングを `Padding="5"` に保ち、`FitToWindow` 時のスクロールバー誤出現を防止。
4. **[basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)** & **[PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)**:
   - ステータスバーの半透明オーバーレイ仕様（#99FFFFFF）およびサイズ変更、スクロールバー整合性をドキュメントに同期反映。

---

## 検証結果

### 1. ビルド検証
```pwsh
dotnet build
```
- **結果**: 警告 0 件、エラー 0 件でビルド成功。

### 2. 自動テスト検証
```pwsh
dotnet test
```
- **結果**: 全 210 件合格（パス: 210、失敗: 0、スキップ: 0）。既存の表示モード・スクロールバー判定テストを含め全テストが PASS。
