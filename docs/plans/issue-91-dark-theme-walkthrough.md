# [検証報告] Issue #91: ダークモードデザインへの完全移行

## 概要
Issue #91 に基づき、アプリ全体を明るいライト配色からスレート／ネイビー系ダークテーマ（`#0F172A` / `#1E293B`）へ完全移行しました。
PDF原本用紙の忠実な白地表示を維持しつつ、統合タイトルバー、リボンツールバー、グリッド俯瞰ビュー、詳細手書きエディタ、ステータスバー、各種ダイアログ、およびカスタムスクロールバーの配色をダークカラーシステムへ刷新しました。

---

## 変更内容のまとめ

### 1. セマンティックカラーパレット & スクロールバースタイルの新設 (`App.xaml`)
- **ダークテーマカラーパレット定義**:
  - `AppBackgroundBrush` (`#0F172A`), `HeaderBackgroundBrush` (`#1E293B`), `RibbonBackgroundBrush` (`#1E293B`), `SurfaceBackgroundBrush` (`#1E293B`), `SurfaceSecondaryBrush` (`#334155`), `SurfaceBorderBrush` (`#334155`), `SurfaceBorderLightBrush` (`#475569`), `TextPrimaryBrush` (`#F8FAFC`), `TextSecondaryBrush` (`#94A3B8`), `TextMutedBrush` (`#64748B`), `AccentBrush` (`#3B82F6`), `DangerBrush` (`#EF4444`) 等をセマンティックリソースとして集約定義。
- **既存コントロールスタイルのダーク化**:
  - `PrimaryButtonStyle`, `SecondaryButtonStyle`, `MiniIconButtonStyle`, `ToolbarButtonStyle`, `RibbonToolbarButtonStyle`, `DangerRibbonToolbarButtonStyle`, `IconToolbarButtonStyle`, `DangerIconToolbarButtonStyle`, `ToolRadioStyle`, `RibbonTabRadioButtonStyle`, `CaptionButtonStyle`, `CloseCaptionButtonStyle`, `RibbonToolRadioStyle`, `RibbonToolToggleStyle`, `ThicknessButtonStyle` の配色・ホバー・押下・無効状態をダークリソースに最適化。
- **カスタムスクロールバー (`ScrollBar`)**:
  - Windows標準の白系スクロールバーを廃止し、スレートダーク背景に馴染むスリムで控えめなカスタムスクロールバー（幅10px、Thumb角丸4px、ホバー・ドラッグ時ハイライト）を全体適用。
- **セパレーター (`Separator`)**:
  - リボン内の縦区切り線をダーク境界色（`#334155`）に統一。

### 2. メインウィンドウの刷新 (`MainWindow.xaml`)
- **統合タイトルバー**: 暗藍灰色（`#1E293B`）背景、アクティブタブ（`#60A5FA`）、非アクティブタブ（`#94A3B8`）、白系ウィンドウキャプションボタン。
- **リボンツールバー**: 背景（`#1E293B`）、太さドット視認性確保（`#F8FAFC`）、カラーパレット選択枠のハイライト改善。
- **外部PDFドロップ案内オーバーレイ**: 半透明ダークレイヤー（`#E60F172A`）とカードデザインの調和。
- **半透明ステータスバー**: 半透明ダーク背景（`#E61E293B`）、ページ入力欄（`#334155` / `#F8FAFC`）、高コントラストな文字・アイコン表示。
- **未保存確認ダイアログ**: 半透明暗転スクリム（`#80000000`）とダークサーフェスカード（`#1E293B`）。

### 3. グリッド俯瞰ビュー (`GridView.xaml`)
- **背景**: 最背面（`#0F172A`）。
- **空白時ウェルカムカード**: ダークサーフェス（`#1E293B`）、境界線（`#334155`）、青アクセントアイコン。
- **サムネイルカード**:
  - カード枠背景（`#1E293B`）、境界線（`#334155`）、選択時ハイライト（`#3B82F6`）。
  - サムネイル外枠余白を `#0F172A` とし、白いPDF用紙が美しく浮かび上がるコントラストを実現。

### 4. 詳細手書きエディタビュー (`DetailEditorView.xaml`)
- **背景**: 最背面（`#0F172A`）。
- **PDF用紙（紙面）の維持**:
  - 原本用紙は白地（`Background="White"`）のまま保持し、手書きインクや原本のカラーを劣化・反転なく忠実に再現。
  - 背面のドロップシャドウをダーク背景に合わせて最適化（`BlurRadius=20, Opacity=0.5, Color=#000000`）。

### 5. ドキュメント更新
- `docs/basic_design.md`: 6.2節にUIデザインシステム＆ダークテーマ仕様（カラーパレット表）を追記し、ステータスバー等の記述・番号を更新。
- `README.md`: 主な機能に「5. モダンなスレートダークテーマ」を追記。
- `docs/PROJECT.md`: 機能インベントリに「F53: スレート／ネイビー系ダークテーマへの完全移行」を追加し、ステータスを完了に更新。

---

## 検証結果

### 1. ビルド検証
- `dotnet build`: 警告 0、エラー 0 でビルド成功。

### 2. 自動テスト検証
- `dotnet test`: **210 件中 210 件すべて合格（100% PASS）**。
  - テストプロジェクト: `PDFBinder.Tests.dll`
  - 失敗: 0、合格: 210、スキップ: 0
