# Issue #88 検証報告: 筆圧のON/OFF機能の追加

## 1. 概要
手書き詳細エディタにおいて、直線ボタンの右横に「筆圧」トグルボタン（デフォルトOFF）を新設し、OFF時は均一な太さの線を描画、ON時のみスタイラスペンの筆圧に応じた強弱・太さ変化を反映する機能を実装しました。

---

## 2. 実施した変更内容

### 2.1 ViewModel
- [`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs):
  - `_isPenPressureEnabled`（初期値 `false`）フィールドおよびプロパティを追加（`[ObservableProperty]`）。
  - `CanTogglePenPressure => SelectedTool == EditorToolMode.Pen && !IsStraightLine;` プロパティを追加。
  - `_isStraightLine` 変更時に `CanTogglePenPressure` の変更通知（`[NotifyPropertyChangedFor(nameof(CanTogglePenPressure))]`）を発行。
  - `OnSelectedToolChanged` 内で `OnPropertyChanged(nameof(CanTogglePenPressure));` を通知。
  - ツール切り替え時（ペン ⇔ 消しゴム等）や別PDF読み込み後も筆圧設定（`IsPenPressureEnabled`）は保持。

### 2.2 View / Control
- [`EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs):
  - `IsPenPressureEnabled` 依存関係プロパティ（初期値: `false`）を追加。
  - `IsPenPressureActive => IsPenPressureEnabled && ToolMode == EditorToolMode.Pen && !IsStraightLine;` プロパティを定義。
  - `ApplyDrawingAttributes()` 内で `DrawingAttributes.IgnorePressure = !IsPenPressureActive` を動的に設定。
  - プロパティ変更ハンドラー `OnIsPenPressureEnabledChanged` を追加し、切り替え時に `ApplyDrawingAttributes()` を即時反映。
- [`DetailEditorView.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml):
  - `EditorInkCanvas` に `IsPenPressureEnabled="{Binding DataContext.IsPenPressureEnabled, RelativeSource={RelativeSource AncestorType=UserControl}}"` をバインド。
- [`MainWindow.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml):
  - 手書きリボンタブ「描画オプション」グループ内、直線ボタンの右横に「筆圧」`ToggleButton` を追加。
  - アイコン: Material Symbols Outlined `line_weight`（`&#xE91A;`）、ラベル: `筆圧`、ツールチップ: `筆圧感知（ペン使用時のみ）`。

### 2.3 単体テスト
- [`DetailEditorPenPressureTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DetailEditorPenPressureTests.cs):
  - 初期値が `false`（OFF）であること。
  - 直線OFF時、ペン選択時のみ `CanTogglePenPressure` が `true` となること。
  - ペン選択時でも直線ONのときは `CanTogglePenPressure` が `false`（無効化）となること。
  - ツールを切り替えても筆圧ON/OFF状態が保持されること。
  - ツール切り替えおよび直線モード切り替え時に PropertyChanged が発行されること。
  - `EditorInkCanvas` において、筆圧OFF時は `IgnorePressure = true`、筆圧ON時は `IgnorePressure = false`、直線ON時や蛍光ペン時は強制的に `IgnorePressure = true` となること（STAスレッドテスト）。

### 2.4 ドキュメント同期
- [`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md): 描画オプショングループへの筆圧トグル仕様の追記。
- [`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md): 機能インベントリ（F49）の追加およびテスト件数（221件）の更新。
- [`README.md`](file:///c:/Git/PDFBinder/README.md): 主な機能・手書きアノテーション一覧への筆圧トグル説明の追記。

---

## 3. 検証結果

### 3.1 自動テスト
- コマンド: `dotnet test`
- 結果: **成功（合格: 221、失敗: 0、スキップ: 0、全PASS）**

### 3.2 ビルド検証
- コマンド: `dotnet build`
- 結果: **ビルド成功（0 警告、0 エラー）**
