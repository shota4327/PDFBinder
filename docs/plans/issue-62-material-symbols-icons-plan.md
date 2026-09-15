# Issue #62: 使用アイコンをGoogle Fonts (Material Symbols Rounded) に変更する 実装計画

## 1. 概要・目的
本計画は、Windows環境に依存していた `Segoe Fluent Icons` / `Segoe MDL2 Assets` および絵文字・Unicode記号を、Google Fontsのオープンソースフォント **Material Symbols Rounded** に完全刷新・統一することを目的とします。

完全オフライン動作（外部ネットワーク通信ゼロ）を担保するため、フォントファイル（`.ttf`）をアプリケーションリソースとして埋め込み、商用利用・再配布が自由な Apache License 2.0 に準拠した安全な構成を確立します。

---

## 2. 採用フォント・ライセンス仕様
- **フォントファミリー名**: Material Symbols Rounded
- **フォント形式**: TrueType Font (`.ttf`)
- **ライセンス**: Apache License 2.0 (Google Fonts)
- **配置先**: `src/PDFBinder.App/Assets/Fonts/MaterialSymbolsRounded.ttf`
- **XAML参照構文**:
  ```xaml
  <FontFamily x:Key="IconFontFamily">pack://application:,,,/PDFBinder;component/Assets/Fonts/#Material Symbols Rounded</FontFamily>
  ```
- **ビルドアクション**: `<Resource Include="Assets\Fonts\*.ttf" />`

---

## 3. アイコンマッピング仕様（確定版）

### 3.1 ウィンドウキャプション（最上部右側）
| 機能 | 変更前 | 反映アイコン | コード | グリフ説明 |
| :--- | :--- | :--- | :--- | :--- |
| **最小化** | `\uE921` | `remove` | `\uE15B` | 水平バー（丸角） |
| **最大化** | `\uE922` | `crop_square` | `\uE3C6` | 正方形アウトライン枠 |
| **復元（元に戻す）** | `\uE923` | `filter_none` | `\uE3E0` | 重なった二重四角枠 |
| **閉じる** | `\uE8BB` | `close` | `\uE5CD` | 丸みのあるクロス（×） |

### 3.2 PDF編集タブ（リボンツールバー）
| 機能 | 変更前 | 反映アイコン | コード | グリフ説明・採用理由 |
| :--- | :--- | :--- | :--- | :--- |
| **開く** | `\uE8E5` | `file_open` | `\uEAF3` | ファイルを開く（ドキュメント指向） |
| **追加** | `\uE710` | `library_add` | `\uE03C` | 複数シート・ドキュメント追加（白紙追加と差別化） |
| **保存** | `\uE74E` | `save` | `\uE161` | フロッピーディスク |
| **別名保存** | `\uE792` | `save_as` | `\uEB60` | ペン付きフロッピーディスク |
| **白紙追加** | `\uE7C3` | `note_add` | `\uE89C` | 用紙にプラス記号（新規白紙追加） |
| **選択抽出** | `\uEDE1` | `file_export` | `\uF3B2` | ファイル書き出し（file_open とペア統一） |
| **全分割** | `\uE8C8` | `insert_page_break` | `\uEACA` | 用紙裁断・分割記号（ページ分割を直感表現） |
| **左回転** | `\uE7AD` (X反転) | `rotate_left` | `\uE419` | 左回り（反時計回り）専用矢印 |
| **右回転** | `\uE7AD` | `rotate_right` | `\uE41A` | 右回り（時計回り）矢印 |
| **削除** | `\uE74D` | `delete` | `\uE92E` | 角丸ゴミ箱 |
| **元に戻す** | `\uE7A7` | `undo` | `\uE166` | アンドゥ矢印 |
| **やり直す** | `\uE7A6` | `redo` | `\uE15A` | リドゥ矢印 |

### 3.3 手書きタブ（リボンツールバー）
| 機能 | 変更前 | 反映アイコン | コード | グリフ説明・採用理由 |
| :--- | :--- | :--- | :--- | :--- |
| **選択** | `\uEF20` | `lasso_select` | `\uEB03` | 投げ縄・ストローク範囲選択 |
| **ペン** | `\uED63` | `stylus_pen` | `\uF363` | スタイラスデジタルペン |
| **蛍光ペン** | `\uE7E6` | `stylus_highlighter` | `\uF364` | スタイラス蛍光ペン |
| **全体消し** | `\uE75C` | `ink_eraser` | `\uE6D0` | 消しゴムアイコン |
| **部分消し** | `\uED61` | `stylus_laser_pointer` | `\uF747` | ピンポイント指定・レーザースタイラス |
| **移動** | `\uECE9` | `pan_tool` | `\uE925` | 開いた手（パン操作） |
| **直線** | `\uED5E` | `straighten` | `\uE41C` | 定規（ルーラー）アイコン |

### 3.4 表示タブ（リボンツールバー）
| 機能 | 変更前 | 反映アイコン | コード | グリフ説明・採用理由 |
| :--- | :--- | :--- | :--- | :--- |
| **詳細ビュー** | `\uE8A5` | `description` | `\uE873` | ドキュメント用紙詳細 |
| **グリッドビュー** | `\uE80A` | `grid_view` | `\uE9B0` | タイル一覧表示 |
| **100% (等倍)** | *(テキスト「1:1」)* | `view_real_size` | `\uF4C2` | 等倍・実寸表示アイコン化 |
| **ウィンドウ幅** | `\uE73F` | `fit_page` | `\uF77A` | ページ全体フィット（fit_page_width と完全ペア） |
| **幅に合わせる** | `\uE765` | `fit_page_width` | `\uF396` | ページ横幅フィット |
| **単一ページ** | `\uE743` | `view_day` | `\uE8ED` | 単一カード表示（view_stream と同一体系） |
| **連続スクロール** | `\uEA55` | `view_stream` | `\uE8F2` | 縦連続カードストリーム表示 |
| **縮小** | `\uE71F` | `zoom_out` | `\uE900` | 虫眼鏡マイナス |
| **拡大** | `\uE8A3` | `zoom_in` | `\uE8FF` | 虫眼鏡プラス |

### 3.5 その他・ステータスバー・カード内ボタン
| 機能 | 変更前 | 反映アイコン | コード | グリフ説明 |
| :--- | :--- | :--- | :--- | :--- |
| **ドロップ案内** | `\uE8F1` | `upload_file` | `\uE9FC` | ファイルドロップ・インポート |
| **前のページ** | `\uE76B` | `chevron_left` | `\uE5CB` | 左シェブロン |
| **次のページ** | `\uE76C` | `chevron_right` | `\uE5CC` | 右シェブロン |
| **ステータス縮小** | `\uE738` | `remove` | `\uE15B` | マイナス記号 |
| **ステータス拡大** | `\uE710` | `add` | `\uE145` | プラス記号 |
| **警告ダイアログ** | `\uE7BA` | `warning` | `\uF083` | 三角警告マーク |
| **グリッド左回転** | `⟲` | `rotate_left` | `\uE419` | 左回転アイコン（ボタン統一） |
| **グリッド右回転** | `⟳` | `rotate_right` | `\uE41A` | 右回転アイコン（ボタン統一） |
| **グリッド削除** | `🗑` | `delete` | `\uE92E` | ゴミ箱アイコン（ボタン統一） |
| **ウェルカム表示** | `📄` | `picture_as_pdf` | `\uE415` | PDFドキュメント |

---

## 4. 変更対象ファイルと改修内容

### 4.1 新規アセット・フォント追加
- **`src/PDFBinder.App/Assets/Fonts/MaterialSymbolsRounded.ttf`**
  - 公式の Material Symbols Rounded フォントファイルを配置。
- **`src/PDFBinder.App/PDFBinder.App.csproj`**
  - `<Resource Include="Assets\Fonts\*.ttf" />` を追加。

### 4.2 リソース定義の更新
- **`src/PDFBinder.App/App.xaml`**
  - `IconFontFamily` の定義を埋め込みフォントリソース参照に変更。
  - `MiniIconButtonStyle` に `FontFamily="{StaticResource IconFontFamily}"` を追加（GridViewのカードボタンでアイコンフォントを適用するため）。

### 4.3 View / XAML のグリフ更新
- **`src/PDFBinder.App/MainWindow.xaml`**
  - ウィンドウキャプションボタン（最小化・最大化・閉じる）
  - PDF編集タブのリボンボタン（開く、追加、保存、別名保存、白紙追加、選択抽出、全分割、左回転、右回転、削除、元に戻す、やり直す）
  - ※左回転の `ScaleTransform ScaleX="-1"` を解除（`rotate_left` が専用グリフのため）
  - 手書きタブのリボンボタン（選択、ペン、蛍光ペン、全体消し、部分消し、移動、直線）
  - 表示タブのリボンボタン（詳細、グリッド、等倍、ウィンドウ、幅、単一、連続、縮小、拡大）
  - ※「100%（等倍）」ボタンを `view_real_size` アイコン＋下段テキストに変更
  - ドラッグドロップ案内オーバーレイ（`upload_file`）
  - 下部ステータスバー（ページ送り、ズームボタン）
  - 未保存変更確認ダイアログ（`warning`）
- **`src/PDFBinder.App/MainWindow.xaml.cs`**
  - `OnWindowStateChanged` 内の最大化/復元グリフ切り替えコードを `\uE3E0`（復元: `filter_none`）と `\uE3C6`（最大化: `crop_square`）に更新。
- **`src/PDFBinder.App/Views/GridView.xaml`**
  - カード下部のクイックアクションボタン（`⟲` -> `rotate_left`, `⟳` -> `rotate_right`, `🗑` -> `delete`）
  - ウェルカム表示（`📄` -> `picture_as_pdf`）
- **`src/PDFBinder.App/Views/DetailEditorView.xaml`**
  - ウェルカム表示（`📄` -> `picture_as_pdf`）

### 4.4 ドキュメント更新
- **`docs/basic_design.md`**: UI設計セクションにおける使用フォント・アイコン定義を Material Symbols Rounded へ更新。
- **`README.md`**: 特徴・動作要件の説明を更新。

---

## 5. 検証計画
1. **ビルド検証**:
   - `dotnet build` を実行し、リソースの埋め込みおよびコンパイルが正常に通ることを確認。
2. **単体テスト検証**:
   - `dotnet test` を実行し、既存の全単体テストが 100% PASS することを確認。
3. **外観・動作目視検証**:
   - アプリを起動し、リボンツールバー各タブ、ウィンドウキャプションボタン、ステータスバー、グリッドカードボタン、ウェルカム表示、未保存ダイアログの全アイコンが意図した通り Material Symbols Rounded で美しく描画されることを確認。
