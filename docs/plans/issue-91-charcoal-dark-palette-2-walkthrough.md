# Walkthrough - チャコールグレー＆VS Codeブルーへのカラーパレット調整 (Issue #91 追加改修-2)

本ドキュメントは、Issue #91 の追加改修として実施した「チャコールダークテーマ（無彩色グレー＆VS Codeブルーアクセント）」へのカラーパレット調整の実装内容および検証結果をまとめたものです。

---

## 1. 改修の背景と目的
- **課題**: 初回ダークテーマ化において青みが強いスレート／ネイビー系（Tailwind Slate: `#0F172A`, `#1E293B` 等）を採用していましたが、ユーザーフィードバック「青をアクセント色として使うのは良いが、背景の青みが強いため、もっと灰色よりの彩度の低い色（チャコール・エディタ風）にしてほしい」に対応。
- **目標**: 
  1. 背景・パネル・カード・境界線を無彩色の自然なチャコールグレー（VS Code / Fluentエディタ調）へ刷新。
  2. アクセントカラーを落ち着いたVS Codeブルー（`#007ACC`）へ統一。
  3. 各ボタンスタイルやオーバーレイに散在していたハードコード色の完全排除とセマンティックリソース化。
  4. PDF原本用紙（白地）と手書きインクのハイコントラスト・明瞭性を維持。

---

## 2. 実施した主な変更内容

### 2.1 チャコールダーク カラーパレットへの刷新 (`src/PDFBinder.App/App.xaml`)
以下の通り、完全無彩色のチャコール系グラデーションおよびVS Codeブルーのセマンティックブラシを再定義しました。

| リソースキー | 設定値 (Hex) | 役割・用途 |
|---|---|---|
| `AppBackgroundBrush` | `#1E1E1E` | ウィンドウ最背面、グリッド・エディタのワークスペース背景 |
| `HeaderBackgroundBrush` | `#252526` | 統合タイトルバー／タブバー背景 |
| `RibbonBackgroundBrush` | `#252526` | リボンツールバー背景 |
| `SurfaceBackgroundBrush` | `#252526` | カード、ダイアログ、フローティングバーの基本背景 |
| `SurfaceSecondaryBrush` | `#2D2D30` | コントロール背景、入力欄、サブパネル |
| `SurfaceBorderBrush` | `#3E3E42` | 通常の境界線、区切り線 |
| `SurfaceBorderLightBrush` | `#505054` | コントロール枠、強調境界線 |
| `TextPrimaryBrush` | `#F1F1F1` | メインテキスト、アクティブアイコン |
| `TextSecondaryBrush` | `#CCCCCC` | ラベル、補助テキスト、非アクティブアイコン |
| `TextMutedBrush` | `#858585` | プレースホルダー、無効状態テキスト |
| `AccentBrush` | `#007ACC` | プライマリボタン、選択カード枠ハイライト |
| `AccentHoverBrush` | `#1F8AD2` | アクティブタブ、アクセントホバー |
| `AccentPressedBrush` | `#094771` | アクセント押下状態 |
| `DangerBrush` | `#F14C4C` | 削除などの危険アクション |
| `DangerHoverBrush` | `#E51400` | 危険アクションホバー状態 |
| `DangerBackgroundHoverBrush` | `#4E1A1A` | 危険ボタンホバー背景 |
| `ControlHoverBrush` | `#3E3E40` | ボタン・タブホバー背景 |
| `ControlPressedBrush` | `#4E4E50` | ボタン・タブ押下背景 |
| `SelectedItemBrush` | `#094771` | 選択中アイテム・トグル背景 |
| `ScrollBarThumbBrush` | `#424242` | スクロールバー通常つまみ |
| `ScrollBarThumbHoverBrush` | `#4F4F4F` | スクロールバーホバーつまみ |
| `ScrollBarThumbPressedBrush` | `#686868` | スクロールバードラッグつまみ |

### 2.2 ボタンスタイルのセマンティックリソース参照化 (`src/PDFBinder.App/App.xaml`)
従来スタイル内に埋め込まれていた旧スレート系の固定カラーコード（`#334155`, `#475569`, `#1E3A8A`, `#94A3B8`, `#F8FAFC` 等）をすべて廃止し、セマンティックブラシ参照へと完全に統一しました。
- `PrimaryButtonStyle`, `SecondaryButtonStyle`, `MiniIconButtonStyle`
- `ToolbarButtonStyle`, `RibbonToolbarButtonStyle`, `DangerRibbonToolbarButtonStyle`
- `IconToolbarButtonStyle`, `DangerIconToolbarButtonStyle`
- `ToolRadioStyle`, `RibbonTabRadioButtonStyle`, `CaptionButtonStyle`
- `RibbonToolRadioStyle`, `RibbonToolToggleStyle`, `ThicknessButtonStyle`

### 2.3 オーバーレイ・ビューの調整
- **`MainWindow.xaml`**:
  - ドロップ案内オーバーレイ: `Background="#E61E1E1E"`（不透明度90%チャコール最背面）
  - 半透明ステータスバー: `Background="#E6252526"`（不透明度90%チャコールパネル面、上部境界線 `SurfaceBorderBrush` `#3E3E42`）
- **`GridView.xaml`**:
  - サムネイルカード選択時のグロー効果: `DropShadowEffect Color="#007ACC"`

### 2.4 ドキュメントの同期更新
- **`docs/basic_design.md`**:
  - 6.2節のダークテーマ仕様を「チャコール系ダークテーマ」に改定し、新カラーパレット一覧表およびスクロールバー色を最新化。
  - 6.4節のステータスバー背景・境界線色定義を更新。
- **`README.md`**:
  - 「5. モダンなチャコールダークテーマ」として特徴・配色方針を最新化。

---

## 3. 検証結果

### 3.1 ビルド検証 (`dotnet build`)
- **結果**: 成功 (0 警告, 0 エラー)
- XAMLリソース参照の解決漏れや文法エラーが一切ないことを確認。

### 3.2 単体テスト検証 (`dotnet test`)
- **結果**: 成功
- **合格数**: 210 件 / 210 件 (100% PASS, 失敗: 0, スキップ: 0)
- PDF操作ロジック、レンダリング、コマンド動作にリグレッションがないことを確認。

---

## 4. 完了状態
- チャコールダークテーマへのカラーパレット調整、ボタンスタイルの整理、ドキュメントの同期、ビルド・テスト検証がすべて完了しました。
