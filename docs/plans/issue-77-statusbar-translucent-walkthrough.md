# Issue #77 ステータスバーの半透明化およびサイズ拡大 検証報告（Walkthrough）

## 変更概要
Issue #77 の要件に基づき、下部ステータスバーを半透明オーバーレイ配置（下端ドック型、半透明白 `#E6FFFFFF`）とし、高さを約5px拡大（MinHeight 42px、Padding 12,5）するとともに、内部のボタン（28x28px）およびページ入力欄（高さ26px）を連動拡大しました。また、背後のコンテンツ（グリッドビューおよび手書き詳細エディタ）が最下部スクロール時にステータスバーに隠れないよう、各ビューのスクロール領域下部に適切な余白パディングを確保しました。

---

## 変更ファイル一覧
1. **[MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)**:
   - `Grid.RowDefinitions` の最下行（Row 3: `Height="Auto"`）を廃止し、コンテンツ領域（Row 2: `Height="*"`）をウィンドウ下端まで拡張。
   - ステータスバー `Border` を `Grid.Row="2"` に配置し、`VerticalAlignment="Bottom"` および `Panel.ZIndex="10"` を指定してオーバーレイ化。
   - 背景色を `Background="#E6FFFFFF"`（約90%不透明の半透明白）に変更。
   - `MinHeight="42"`、`Padding="12,5"` に拡大。
   - ページ移動ボタン（前へ・次へ）およびズームボタン（縮小・拡大）のサイズを `28x28px` に拡大、アイコンフォントサイズを `12px` / `13px` に調整。
   - ページ番号入力欄 `TextBox` の高さを `26px`、幅を `40px` に拡大。
   - ズームリセットボタンの高さを `28px`、最小幅を `50px` に拡大。
   - 未保存変更確認ダイアログの `Grid.RowSpan` を `3` に調整（画面全体を覆うZIndex 1000のまま維持）。
2. **[GridView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml)**:
   - `ScrollViewer` の `Padding` を `20,20,20,56` に更新し、最下部スクロール時に最終行のサムネイルカードおよびアクションボタンがステータスバーに隠れない余白を確保。
3. **[DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
   - `DetailScrollViewer` の `Padding` を `5,5,5,48` に更新し、縦スクロール時にページ下端がステータスバーに隠れず完全に閲覧・編集できるように対応。
4. **[basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)** & **[PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)**:
   - ステータスバーの半透明オーバーレイ仕様およびサイズ変更をドキュメントに同期反映。

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
- **結果**: 全 210 件合格（パス: 210、失敗: 0、スキップ: 0）。既存ロジックおよびテストに影響がないことを確認。
