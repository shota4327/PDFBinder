# タイトルバー廃止とタブバー統合の実装計画 (Issue #28)

Windows標準のタイトルバーを廃止し、タブバーとウィンドウ制御ボタン（最小化・最大化・閉じる）を同一段に統合したモダンなタイトルバー兼タブバーを実装します。

## 概要と合意済み設計

事前のヒアリング（/grill-me）に基づき、以下の仕様で実装します：
1. **レイアウト形式**: **Chrome / Edge スタイル**
   - **左端**: アプリアイコン（18x18px、ドラッグ可能・非クリッカブル）
   - **その横**: 「PDF編集」「手書き」タブ
   - **中央部**: 薄いグレーのファイル名表示（`sample.pdf` 等、ドラッグ＆ダブルクリック最大化可能）
   - **右端**: Windows標準スタイルの最小化・最大化/復元・閉じるボタン（ホバーで赤色ハイライト）
2. **タブデザイン**: **下部ツールバー結合型**
   - アクティブタブ: 白背景（`#FFFFFF`）＋上部角丸（`6,6,0,0`）で下部ツールバー領域とシームレスに結合
   - 非アクティブタブ: 透明背景＋ホバー時ハイライト（`#E2E8F0`）
3. **サイズ感**: **標準モダン**
   - タイトルバー高さ: 38px
   - キャプションボタン幅: 46px, 高さ: 38px
4. **ウィンドウ制御**:
   - `WindowChrome` を使用し、バーの余白領域および中央テキスト上でウィンドウ移動ドラッグおよびダブルクリックによる最大化/元に戻す操作をネイティブにサポート
   - 最大化時のウィンドウ境界パディング補正を行い、画面端の不自然なはみ出しを防止
   - 最大化状態に応じて最大化ボタンのアイコン（`\uE922` ⇄ `\uE923`）およびツールチップ（「最大化」⇄「元に戻す」）が自動で切り替わる設計

---

## ユーザーレビューが必要な項目

> [!NOTE]
> - ヒアリングで決定した方針（左側アイコン・タブ、中央ファイル名ドラッグ領域、右側ボタン）に沿って実装します。
> - Windows標準の `Alt+Space` システムメニューやタスクバー上の表示、ウィンドウドラッグ移動等のネイティブ挙動はすべて維持されます。

---

## 変更内容

Group files by component:

### 1. ドメイン・ViewModel層 (`PDFBinder.App.ViewModels`)

#### [MODIFY] [MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs)
- タイトルバー中央に表示する現在開いているファイル名プロパティ `DisplayFileName` を追加。
- `Document` または `Document.FilePath` の変更を検知して `DisplayFileName` を自動更新（未読み込み時は空文字、読み込み時は `Path.GetFileName(FilePath)`）。
- プロパティの更新通知を確実に行うロジックを実装。

---

### 2. UI・スタイル・メインウィンドウ (`PDFBinder.App`)

#### [MODIFY] [App.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/App.xaml)
- タイトルバー統合用のキャプションボタンスタイル（最小化、最大化、閉じる）を追加。
  - 通常時: 背景透明、テキスト色 `#1E293B`
  - ホバー時: 最小化/最大化は `#E2E8F0`、閉じるボタンは `#E81123`（文字色: 白）
  - プレス時: 最小化/最大化は `#CBD5E1`、閉じるボタンは `#C42B1C`
- タブバーヘッダー用スタイル（`RibbonTabRadioButtonStyle`）の調整：
  - 選択タブの下部マージンを調整し、下部の白ツールバー境界とシームレスに結合。

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- `Window` 要素に `WindowChrome.WindowChrome` を設定：
  - `CaptionHeight="38"`
  - `ResizeBorderThickness="6"`
  - `CornerRadius="0"`
  - `GlassFrameThickness="0"`
  - `UseAeroCaptionButtons="False"`
- 最上段行に統合タイトルバーを新設：
  - 左側: アイコン（`Assets/icon.png`）
  - タブ領域: `WindowChrome.IsHitTestVisibleInChrome="True"` を付与した「PDF編集」「手書き」タブ
  - 中央領域: `DisplayFileName` を表示する `TextBlock`（`IsHitTestVisible="False"` でドラッグ可能）
  - 右側: `WindowChrome.IsHitTestVisibleInChrome="True"` を付与した最小化・最大化・閉じるボタン
- 最大化時のパディング補正（Style Triggers）を追加。

#### [MODIFY] [MainWindow.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml.cs)
- `SystemCommands` によるウィンドウ制御ハンドラー（最小化、最大化/復元、閉じる）の登録。
- `WindowState` 変更時の最大化/復元ボタンのアイコン・ツールチップ切り替え処理（またはバインディング）。

---

### 3. 単体テスト (`tests/PDFBinder.Tests`)

#### [NEW] [MainViewModelTitleTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/MainViewModelTitleTests.cs)
- ドキュメント未読み込み時の `DisplayFileName` が空文字であることを検証。
- ファイルパスが設定されたドキュメントを読み込んだ際に、正しくファイル名のみ（拡張子含む）が抽出・反映されることを検証。
- ファイルパスが変更された場合に更新通知が行われることを検証。

---

## 検証計画

### 自動テスト
- コマンド: `dotnet test`
- 既存の全54テストに加え、新規追加する `MainViewModelTitleTests` を含む全テストが 100% PASS することを確認。

### ビルド検証
- コマンド: `dotnet build`
- 警告やエラーなく正常にビルドが成功することを確認。

### 手動・動作確認
- ウィンドウ上部のドラッグによる移動ができるか
- バー中央の余白およびファイル名文字列のダブルクリックで最大化 / 元に戻すが機能するか
- 右上の最小化、最大化（元に戻す）、閉じるボタンが正常に動作し、ホバー時の視覚的フィードバック（閉じるボタンの赤ハイライト等）が適切か
- 「PDF編集」「手書き」タブの切り替えが正常に行え、アクティブタブが下部ツールバーと美しく結合しているか
