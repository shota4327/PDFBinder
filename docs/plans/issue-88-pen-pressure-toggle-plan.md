# Issue #88 実装計画: 筆圧のON/OFF機能の追加

## 1. 概要
手書き詳細エディタにおいて、現在ペン描画時の筆圧反映（StylusPointの筆圧による線の強弱）はWPFの標準挙動（デフォルト有効）となっていますが、Issue #88 の要件に基づき、**直線ボタンの右隣に「筆圧」トグルボタン（デフォルトOFF）を新設**し、OFF時は線の太さが均一に保たれ、ON時のみ筆圧に応じた強弱・太さ変化が反映されるようにします。

---

## 2. ヒアリング（/grill-me）で合意した設計方針

1. **対象ツール範囲**:
   - 筆圧トグルボタンの有効化（操作可能）対象は**「ペン」ツール選択時のみ**とする。
   - 蛍光ペンはマーカー引きとしての均一性を維持するため、筆圧は適用しない（常に均一な太さ）。
2. **ツールの切り替えと状態保持**:
   - 消しゴムや移動ツールなどの他ツールに切り替えてから再度「ペン」に戻った際も、**筆圧のON/OFF状態を保持**する（直線ツールのように都度リセットせず、ペンの設定として維持）。
   - 別PDFファイルを開いた際も、アプリ起動中はユーザーの入力デバイス設定として筆圧のON/OFF状態を引き継ぐ。
3. **直線モードとの連動**:
   - 「直線」トグルがONになっている間は、直線描画に筆圧が影響しないことをUI上でも明確にするため、**筆圧ボタンを無効化（IsEnabled=falseでグレーアウト）**する。
4. **リボンUIデザイン・アイコン**:
   - 配置: 「直線」ボタンのすぐ右横（グループ3「描画オプション」内、太さプリセットの手前）。
   - アイコン: Material Symbols Outlined の線の太さ変化アイコン（`line_weight`: `&#xE91A;`）。
   - テキストラベル: `筆圧`。
   - ツールチップ: `筆圧感知（ペン使用時のみ）`。
   - トグルボタン形式（`RibbonToolToggleStyle`、ON時にハイライト表示）。
5. **描画ロジック**:
   - WPF `DrawingAttributes.IgnorePressure` を制御:
     - 筆圧OFF時（デフォルト）: `IgnorePressure = true`（均一な太さ）
     - 筆圧ON時: `IgnorePressure = false`（筆圧による太さ変化が有効）
   - `EditorInkCanvas` の `ApplyDrawingAttributes()` において、`IsPenPressureActive`（`IsPenPressureEnabled && ToolMode == EditorToolMode.Pen && !IsStraightLine`）に基づき `attr.IgnorePressure = !isPressureActive` を設定。

---

## 3. 変更対象ファイル

### 3.1 ViewModel / Model
#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- `_isPenPressureEnabled`（`bool`、初期値: `false`）フィールドおよびプロパティを追加（`[ObservableProperty]`）。
- `CanTogglePenPressure => SelectedTool == EditorToolMode.Pen && !IsStraightLine;` プロパティを追加。
- `OnSelectedToolChanged` 内で `OnPropertyChanged(nameof(CanTogglePenPressure));` を通知。
- `_isStraightLine` 変更時にも `OnPropertyChanged(nameof(CanTogglePenPressure));` が発行されるよう属性付与または通知コードを追加。

### 3.2 View / Control
#### [MODIFY] [EditorInkCanvas.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- `IsPenPressureEnabled` 依存関係プロパティ（`DependencyProperty`、初期値: `false`）を追加。
- プロパティ変更ハンドラー `OnIsPenPressureEnabledChanged` を追加し、変更時に `ApplyDrawingAttributes()` を呼び出し。
- `ApplyDrawingAttributes()` 内で `DrawingAttributes.IgnorePressure = !(IsPenPressureEnabled && ToolMode == EditorToolMode.Pen && !IsStraightLine)` を設定。

#### [MODIFY] [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- `EditorInkCanvas` の `IsPenPressureEnabled` プロパティに、`DataContext.IsPenPressureEnabled` をバインド。

#### [MODIFY] [MainWindow.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
- 手書きリボンタブの「描画オプション」グループ内、直線ボタンの右横に「筆圧」`ToggleButton` を追加。
  - `IsChecked="{Binding DetailEditor.IsPenPressureEnabled, Mode=TwoWay}"`
  - `IsEnabled="{Binding DetailEditor.CanTogglePenPressure}"`
  - `Style="{StaticResource RibbonToolToggleStyle}"`
  - アイコン: `&#xE91A;`（`line_weight`）
  - ラベル: `筆圧`
  - ツールチップ: `筆圧感知（ペン使用時のみ）`

### 3.3 単体テスト
#### [NEW] [DetailEditorPenPressureTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorPenPressureTests.cs)
- `IsPenPressureEnabled` の初期値が `false`（OFF）であることの検証。
- ツール切り替え時の `CanTogglePenPressure` 状態の検証（Pen時のみ `true`、Highlighter・Select・Eraser時は `false`）。
- 直線トグルON時の `CanTogglePenPressure` の無効化（`false`）の検証。
- ツール切り替え（Pen → Eraser → Pen）後も `IsPenPressureEnabled` の状態が保持されることの検証。
- `EditorInkCanvas` の `DefaultDrawingAttributes.IgnorePressure` が `IsPenPressureEnabled` と連動して切り替わることの検証（STAスレッドテスト）。

### 3.4 ドキュメント
#### [MODIFY] [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)
- 手書き詳細エディタのツールバー仕様・描画オプションに「筆圧トグル」の仕様（デフォルトOFF、IgnorePressure制御）を追記。
#### [MODIFY] [docs/PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
- 機能インベントリに筆圧機能（Issue #88）を反映。

---

## 4. 検証計画

### 4.1 自動テスト（単体テスト）
- コマンド: `dotnet test`
- `DetailEditorPenPressureTests` を含む全テストが 100% PASS することを確認。

### 4.2 ビルド検証
- コマンド: `dotnet build`
- 警告・エラーなく正常にビルドが成功することを確認。

### 4.3 手動確認観点
- 詳細エディタを開いた際、直線ボタンの右横に「筆圧」ボタンが表示され、初期状態でOFFとなっていること。
- ペン選択時に「筆圧」ボタンが有効（クリック可能）であること。
- 「直線」をONにすると「筆圧」ボタンがグレーアウト（無効化）され、直線をOFFにすると再度有効になること。
- 蛍光ペンや消しゴムに切り替えた際、「筆圧」ボタンがグレーアウト（無効化）されること。
- 筆圧をONにした状態で消しゴム等へ切り替え、再度ペンに戻した際も「筆圧」がONのまま維持されていること。
