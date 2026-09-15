# Issue #62: 使用アイコンをGoogle Fonts (Material Symbols Outlined) に変更する 検証報告 (Walkthrough)

## 1. 実施概要
- **対象Issue**: #62
- **ブランチ**: `issue-62-material-symbols-icons`
- **目的**: Windows標準フォント（`Segoe Fluent Icons` / `Segoe MDL2 Assets`）およびUnicode絵文字・記号に依存していた全アイコンを、Google Fontsのオープンソースフォント **Material Symbols Outlined** に完全移行・統一。
- **オフライン動作保証**: フォントファイル（`MaterialSymbolsOutlined.ttf`）を `PDFBinder.App/Assets/Fonts/` にリソースとして内包し、外部通信ゼロの完全オフライン動作を実現。

---

## 2. 変更内容一覧

### 2.1 フォントファイルおよびリソース設定
- `src/PDFBinder.App/Assets/Fonts/MaterialSymbolsOutlined.ttf`: Google Fonts 公式リポジトリより入手した Outlined TTF フォントファイルを配置。
- `src/PDFBinder.App/PDFBinder.App.csproj`: `<Resource Include="Assets\Fonts\*.ttf" />` を定義。
- `src/PDFBinder.App/App.xaml`:
  - `IconFontFamily` を `pack://application:,,,/PDFBinder;component/Assets/Fonts/#Material Symbols Outlined` に設定。

### 2.2 UI グリフの刷新

#### ① ウィンドウキャプション（最上部右側）
- 最小化: `\uE15B` (`remove`)
- 最大化: `\uE3C6` (`crop_square`)
- 復元（最大化解除）: `\uE3E0` (`filter_none`)
- 閉じる: `\uE5CD` (`close`)
- `MainWindow.xaml.cs`: `OnWindowStateChanged` 内で最大化時は `\uE3E0`（復元）、通常時は `\uE3C6`（最大化）を動的設定。

#### ② PDF編集タブ（リボンツールバー）
- 開く: `\uEAF3` (`file_open`)
- 追加: `\uE03C` (`library_add`)
- 保存: `\uE161` (`save`)
- 別名保存: `\uEB60` (`save_as`)
- 白紙追加: `\uE89C` (`note_add`)
- 選択抽出: `\uF3B2` (`file_export`)
- 全分割: `\uEACA` (`insert_page_break`)
- 左回転: `\uE419` (`rotate_left`) ※旧反転描画ハックを撤廃
- 右回転: `\uE41A` (`rotate_right`)
- 削除: `\uE92E` (`delete`)
- 元に戻す: `\uE166` (`undo`)
- やり直す: `\uE15A` (`redo`)

#### ③ 手書きタブ（リボンツールバー）
- 選択: `\uEB03` (`lasso_select`)
- ペン: `\uF363` (`stylus_pen`)
- 蛍光ペン: `\uF364` (`stylus_highlighter`)
- 全体消し: `\uE6D0` (`ink_eraser`)
- 部分消し: `\uF747` (`stylus_laser_pointer`)
- 移動: `\uE925` (`pan_tool`)
- 直線: `\uE41C` (`straighten`)

#### ④ 表示タブ（リボンツールバー）
- 詳細: `\uE873` (`description`)
- グリッド: `\uE9B0` (`grid_view`)
- 100%（等倍）: テキスト「1:1」から専用アイコン `\uF4C2` (`view_real_size`) に刷新
- ウィンドウ: `\uF77A` (`fit_page`)
- 幅: `\uF396` (`fit_page_width`)
- 単一: `\uE8ED` (`view_day`)
- 連続: `\uE8F2` (`view_stream`)
- 縮小: `\uE900` (`zoom_out`)
- 拡大: `\uE8FF` (`zoom_in`)

#### ⑤ ドロップ案内・ステータスバー・ダイアログ・カードボタン
- ドロップ案内オーバーレイ: `\uE9FC` (`upload_file`)
- 前のページ: `\uE5CB` (`chevron_left`)
- 次のページ: `\uE5CC` (`chevron_right`)
- ステータスバー縮小: `\uE15B` (`remove`)
- ステータスバー拡大: `\uE145` (`add`)
- 未保存確認ダイアログ: `\uF083` (`warning`)
- グリッドビューカード下部ボタン:
  - 左回転: `\uE419` (`rotate_left`)
  - 右回転: `\uE41A` (`rotate_right`)
  - 削除: `\uE92E` (`delete`)
- ウェルカム画面（GridView / DetailEditorView）:
  - `\uE415` (`picture_as_pdf`)

### 2.3 ドキュメント同期
- `docs/basic_design.md`: 技術スタックおよびプロジェクトツリーに Material Symbols Rounded を追記。
- `README.md`: 主要ライブラリに Material Symbols Rounded（Apache-2.0）を追記。
- `docs/PROJECT.md`: 機能インベントリに F52（アイコン刷新）を完了として追記。

---

## 3. 検証結果

### 3.1 ビルド検証
- コマンド: `dotnet build`
- 結果: **成功 (警告 0, エラー 0)**

### 3.2 単体テスト検証
- コマンド: `dotnet test`
- 結果: **全 210 件 PASS (失敗 0, スキップ 0)**
