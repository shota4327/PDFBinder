# [実装計画] Issue #91: チャコールグレー＆VS Codeブルーへのカラーパレット調整 (連番-2)

## 概要
ダークテーマへの移行後、ユーザーフィードバック「背景の青みが強いため、もっと灰色よりの彩度の低い色（チャコール・エディタ風）にしたい」「アクセントにはVS Code風の落ち着いたブルーを採用したい」に基づき、カラーパレット全体を再調整します。
PDF用紙（紙面）の白地保持はそのままに、最背面・パネル・ツールバー・カード外枠の青みを完全に排除し、無彩色のチャコールグレー（`#1E1E1E` / `#252526`）と上品なエディタ風ブルー（`#007ACC` / `#094771`）へ移行します。

---

## 決定事項（設計合意内容）
1. **ベーストーン**: チャコール・エディタ風グレー（VS Code / Fluent系：`#1E1E1E` / `#252526`）。青みを完全に排した低彩度無彩色ダーク。
2. **アクセントブルー**: VS Code風の落ち着いたブルー（`#007ACC`、選択背景 `#094771`）。チャコール背景に自然に馴染む上品なコントラスト。
3. **用紙の扱い**: PDF用紙（紙面）は白地のまま維持（変更なし）。

---

## 新旧カラーパレット対照表

| セマンティック名 | 変更前 (Slate Dark) | 変更後 (Charcoal Dark) | 用途・説明 |
|---|---|---|---|
| `AppBackgroundBrush` | `#0F172A` (暗藍灰色) | **`#1E1E1E`** (完全無彩色チャコール) | ウィンドウ、ワークスペース最背面 |
| `HeaderBackgroundBrush` | `#1E293B` (青みダーク) | **`#252526`** (チャコールサーフェス) | 統合タイトルバー／タブバー背景 |
| `RibbonBackgroundBrush` | `#1E293B` (青みダーク) | **`#252526`** (チャコールサーフェス) | リボンツールバー背景 |
| `SurfaceBackgroundBrush` | `#1E293B` (青みダーク) | **`#252526`** (チャコールサーフェス) | カード、フローティングバー、ダイアログ等 |
| `SurfaceSecondaryBrush` | `#334155` (青みグレー) | **`#2D2D30`** (中間ダークグレー) | コントロール背景、入力欄背景 |
| `SurfaceBorderBrush` | `#334155` (青みグレー) | **`#3E3E42`** (ニュートラルグレー) | 境界線、区切り線 |
| `SurfaceBorderLightBrush`| `#475569` (青みグレー) | **`#505054`** (強調グレー) | コントロール枠線 |
| `TextPrimaryBrush` | `#F8FAFC` | **`#F1F1F1`** | メインテキスト、アクティブアイコン |
| `TextSecondaryBrush` | `#94A3B8` | **`#CCCCCC`** | サブテキスト、アイコン |
| `TextMutedBrush` | `#64748B` | **`#858585`** | 無効状態テキスト、キャプション |
| `AccentBrush` | `#3B82F6` (鮮やかな青) | **`#007ACC`** (VS Codeブルー) | プライマリボタン、選択枠ハイライト |
| `AccentHoverBrush` | `#2563EB` | **`#1F8AD2`** | アクセントホバー |
| `AccentPressedBrush` | `#1D4ED8` | **`#094771`** | アクセント押下 |
| `SelectedItemBrush` | `#1E3A8A` (深い青) | **`#094771`** (エディタ選択背景青) | 選択中アイテム・トグル背景 |
| `ControlHoverBrush` | `#334155` | **`#3E3E40`** | ボタン・タブホバー背景 |
| `ControlPressedBrush` | `#475569` | **`#4E4E50`** | ボタン・タブ押下背景 |
| `ScrollBarThumbBrush` | `#475569` | **`#424242`** | スクロールバーThumb |
| `ScrollBarThumbHoverBrush`| `#64748B` | **`#4F4F4F`** | スクロールバーThumbホバー |
| `ScrollBarThumbPressedBrush`| `#94A3B8` | **`#686868`** | スクロールバーThumb押下 |

---

## 変更予定コンポーネント

### 1. `src/PDFBinder.App/App.xaml`
- 上記カラーパレット定義の Hex 値を更新。
- 各種ボタンスタイル（`PrimaryButtonStyle`, `SecondaryButtonStyle`, `ToolbarButtonStyle`, `RibbonToolbarButtonStyle`, `RibbonTabRadioButtonStyle`, `RibbonToolRadioStyle`, `RibbonToolToggleStyle`, `ThicknessButtonStyle` 等）の個別ハードコード色（もしあれば）をチャコールリソースに同期。
- アクティブタブのアクセント色を `#3794FF`（VS Code風の明るいブルー）に調整。

### 2. `src/PDFBinder.App/MainWindow.xaml`
- 半透明ステータスバー背景を `#E6252526` に更新。
- 外部PDFドロップ案内オーバーレイ背景を `#E61E1E1E` に更新。

### 3. `src/PDFBinder.App/Views/GridView.xaml`
- サムネイルカード選択時のシャドウ色（`DropShadowEffect Color`）を `#007ACC` に調整。

### 4. ドキュメント
- `docs/basic_design.md`: カラーパレット仕様をチャコール系に更新。
- `README.md`: 「モダンなスレートダークテーマ」の表記を「モダンなチャコールダークテーマ」に更新。

---

## 検証手順
- `dotnet build`: ビルドが警告・エラーなく成功することを確認。
- `dotnet test`: 210件すべての単体テストが 100% PASS することを確認。
- アプリを起動し、青みが排除され完全な無彩色のチャコールグレーと落ち着いたアクセントブルーで統一されていることを視認確認。
