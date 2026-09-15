# [実装計画] Issue #91: ダークモードデザインへの完全移行

## 概要
現在アプリ全体が明るいライトテーマ配色（Tailwind Slate系ライト）になっていますが、目の疲労を軽減しモダンで洗練された外観を実現するため、スレート／ネイビー系ダーク（`#0F172A` / `#1E293B`）を基調としたダークテーマに完全移行します。
ユーザーインタビュー（`/grill-me`）で確定した要件・設計方針に基づき、PDF用紙（紙面）の忠実な白地表示を保持しつつ、アプリフレーム、ツールバー、各種パネル、フローティングコントロール、スクロールバーを統一的なダークデザインへ刷新します。

---

## 決定事項（設計合意内容）
1. **動作モード**: ダークテーマ専用（常時ダーク）としてアプリ全体のデザインを完全移行（動的切り替え機能は不要）。
2. **カラーパレット**: スレート／ネイビー系ダーク（現在の配色と親和性の高い暗藍灰色：`#0F172A` / `#1E293B` ベース）。
3. **PDF用紙の扱い**: PDF用紙（紙面）自体は白地（原本色）のまま維持し、周囲のワークスペース・外枠・ツールバー・カード余白等をダーク化。
4. **スクロールバー**: ダークテーマに調和するスリムなカスタムスクロールバースタイルを定義・適用。
5. **リソース管理**: `App.xaml` にセマンティックな Brush リソースを集約定義し、各 View のハードコード色を置き換え。

---

## カラーパレット定義（Slate Dark）

| セマンティック名 | カラーコード (Hex) | 用途・説明 |
|---|---|---|
| `AppBackgroundBrush` | `#0F172A` (Slate-900) | アプリ全体の最背面（ウィンドウ、グリッド・エディタのワークスペース） |
| `HeaderBackgroundBrush` | `#1E293B` (Slate-800) | 統合タイトルバー／タブバー背景 |
| `RibbonBackgroundBrush` | `#1E293B` (Slate-800) | リボンツールバー背景 |
| `SurfaceBackgroundBrush` | `#1E293B` (Slate-800) | カード、フローティングバー、ダイアログ等の基本面 |
| `SurfaceSecondaryBrush` | `#334155` (Slate-700) | サブ領域、ホバー背景、バッジ背景 |
| `SurfaceBorderBrush` | `#334155` (Slate-700) | 通常の境界線、区切り線 |
| `SurfaceBorderLightBrush` | `#475569` (Slate-600) | 強調境界線、コントロール外枠 |
| `TextPrimaryBrush` | `#F8FAFC` (Slate-50) | 最前面メインテキスト、アクティブアイコン |
| `TextSecondaryBrush` | `#94A3B8` (Slate-400) | ラベル、補助テキスト、非アクティブアイコン |
| `TextMutedBrush` | `#64748B` (Slate-500) | 無効状態テキスト、控えめなキャプション |
| `AccentBrush` | `#3B82F6` (Blue-500) | プライマリボタン、選択ハイライト |
| `AccentHoverBrush` | `#2563EB` (Blue-600) | アクセントホバー状態 |
| `AccentPressedBrush` | `#1D4ED8` (Blue-700) | アクセント押下状態 |
| `DangerBrush` | `#EF4444` (Red-500) | 削除などの危険アクション |
| `DangerHoverBrush` | `#DC2626` (Red-600) | 危険アクションホバー状態 |
| `DangerBackgroundHoverBrush` | `#451A1A` (Red半透明暗色) | 危険ボタンホバー背景 |
| `ControlHoverBrush` | `#334155` (Slate-700) | ボタン・タブホバー背景 |
| `ControlPressedBrush` | `#475569` (Slate-600) | ボタン・タブ押下背景 |
| `SelectedItemBrush` | `#1E3A8A` (Blue-900) | 選択中アイテム・トグル背景 |

---

## 変更予定コンポーネント

### 1. `PDFBinder.App`

#### [MODIFY] [App.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/App.xaml)
- 上記カラーパレットリソース（`SolidColorBrush`）を追加。
- 既存ボタンスタイル（`PrimaryButtonStyle`, `SecondaryButtonStyle`, `ToolbarButtonStyle`, `RibbonToolbarButtonStyle`, `DangerRibbonToolbarButtonStyle`, `IconToolbarButtonStyle`, `DangerIconToolbarButtonStyle`, `ToolRadioStyle`, `RibbonTabRadioButtonStyle`, `CaptionButtonStyle`, `CloseCaptionButtonStyle`, `RibbonToolRadioStyle`, `RibbonToolToggleStyle`, `ThicknessButtonStyle`, `MiniIconButtonStyle`）の背景・文字色・枠線・ホバー・押下状態をダークリソースに更新。
- スリムなダークスクロールバースタイル（`CustomScrollBarStyle`）を新規追加し、アプリケーション全体のデフォルト ScrollBar スタイルとして適用。

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- ウィンドウおよびメイン Grid 背景を `{StaticResource AppBackgroundBrush}` に変更。
- 統合タイトルバー／タブバーの背景を `{StaticResource HeaderBackgroundBrush}`、下部境界線を `{StaticResource SurfaceBorderBrush}` に変更。
- リボンツールバーの背景、区切り線（Border）、グループタイトル（TextBlock）の配色をダークリソースへ更新。
- フローティング操作バー（ページナビゲーション、ズームバー、クイックアクションバー）の背景を `{StaticResource SurfaceBackgroundBrush}`、境界線を `{StaticResource SurfaceBorderBrush}` に更新し、影のドロップシャドウを調整。
- 最下部ステータスバーの配色をダークリソースへ更新。
- ショートカット一覧モーダルの背景・枠線・テキスト色をダークリソースへ更新。

#### [MODIFY] [GridView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml)
- ルート Grid 背景を `{StaticResource AppBackgroundBrush}` に変更。
- 空白時ウェルカムパネルの背景を `{StaticResource SurfaceBackgroundBrush}`、枠線およびテキストをダークリソースへ更新。
- サムネイルカード Border:
  - カード枠背景を `{StaticResource SurfaceBackgroundBrush}`、枠線を `{StaticResource SurfaceBorderBrush}` に変更。
  - 選択時の枠線色を `{StaticResource AccentBrush}` に変更。
  - サムネイル画像（PDFページ）自体は白背景（`Background="White"`）を保持。
  - ページ番号バッジ・カード内ミニボタン（回転・削除等）の配色をダーク化。

#### [MODIFY] [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- ルート Grid 背景を `{StaticResource AppBackgroundBrush}` に変更。
- 空白時ウェルカムパネルをダークリソースへ更新。
- PDFページ描画領域:
  - ページ用紙自体は白地（`Background="White"`）を維持し、インク色および原本色をそのまま再現。
  - ドロップシャドウの不透明度・色調をダーク背景に合わせて最適化。

---

### 2. ドキュメント

#### [MODIFY] [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
- UIデザイン原則およびカラーパレット仕様に、スレート／ネイビー系ダークテーマの定義を追記・更新。

#### [MODIFY] [README.md](file:///c:/Git/PDFBinder/README.md)
- アプリの主な機能・UIの特徴に「モダンなスレートダークテーマ」を追記。

---

## 検証手順

### 自動テスト
- `dotnet test`: 既存の単体テストがすべて 100% PASS することを確認。
- `dotnet build`: ビルドが警告およびエラーなく成功することを確認。

### 手動・視認検証（UI確認）
- アプリを起動し、以下の各ビューの表示状態を目視確認:
  1. **統合タイトルバー＆リボン**: 暗藍灰色の背景に白文字・アイコンがコントラスト高く表示され、タブ切替やホバーが滑らかに反応するか。
  2. **グリッド俯瞰ビュー**: 濃紺ダーク背景（`#0F172A`）の上に白いPDF用紙サムネイルが浮かび上がり、カード枠・選択枠・ページ番号が明瞭に見えるか。
  3. **詳細手書きエディタ**: ダーク背景の中で用紙（白地）が明瞭に認識でき、ペン・蛍光ペン・消しゴム・直線などの手書きが通常通り忠実に描画されるか。
  4. **フローティングバー＆スクロールバー**: ズームバーやナビゲーションバー、スクロールバーがダーク背景に溶け込みつつ直感的に操作できるか。
