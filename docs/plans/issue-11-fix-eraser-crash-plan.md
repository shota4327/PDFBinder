# 手書きエディタで全消しゴム選択時にクラッシュする不具合の修正計画 (Issue #11)

手書きエディタ（`DetailEditorView`）において、ペンで線を描画した後に「全消しゴム」等の描画ツールを選択すると、アプリがフリーズした後にクラッシュする不具合を修正します。

## 決定事項（`/grill-me` にて合意済み）
1. **`ToolToBooleanConverter.ConvertBack` の正式実装**:
   - `RadioButton.IsChecked` は既定で TwoWay バインディングとなっており、クリック時に `ConvertBack` が呼び出される。
   - `value` が `true` の場合、パラメータ文字列（`ConverterParameter`）を `Enum.TryParse<EditorToolMode>` でパースして該当 Enum 値を返却する。
   - `value` が `false`（ラジオボタンのチェック解除側）や未指定時は `Binding.DoNothing` を返却し、ViewModel 側の値を維持する。
2. **`DetailEditorViewModel.OnSelectedToolChanged` へのロジック集約**:
   - `SelectedTool` のプロパティ変更通知部分メソッド `OnSelectedToolChanged(EditorToolMode value)` を実装。
   - 蛍光ペン（黄色・太さ12px）や通常ペン（黒色・太さ2px）への初期値連動ロジックをここに集約し、TwoWay バインディング経由の変更およびコマンド経由の変更の双方で確実に連動させる。
   - `SelectTool` コマンド内は `SelectedTool = tool;` の設定にシンプル化する。
3. **ラジオボタングループの明示**:
   - `DetailEditorView.xaml` の各ラジオボタンに `GroupName="EditorToolGroup"` を付与し、グループ動作を明示化する。

---

## 提案される変更

### コンバーター層 (`PDFBinder.App.Converters`)

#### [MODIFY] [`CommonConverters.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Converters/CommonConverters.cs)
- `ToolToBooleanConverter.ConvertBack` の実装:
  - `throw new NotSupportedException();` を撤廃。
  - `value is true` かつ `parameter != null` の場合、`EditorToolMode` へのパースを実施して返却。
  - それ以外（`value is false` 等）は `Binding.DoNothing` を返却。

---

### ViewModel層 (`PDFBinder.App.ViewModels`)

#### [MODIFY] [`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- `OnSelectedToolChanged(EditorToolMode value)` 部分メソッドの実装:
  - `value == EditorToolMode.Highlighter` の場合、色（黒なら黄色へ）と太さ（12px）を自動調整。
  - `value == EditorToolMode.Pen` の場合、色（黄色なら黒へ）と太さ（2px）を自動調整。
- `SelectTool(EditorToolMode tool)` コマンドの実装をシンプル化。

---

### View層 (`PDFBinder.App.Views`)

#### [MODIFY] [`DetailEditorView.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- ツール切り替え用の各 `RadioButton` に `GroupName="EditorToolGroup"` を設定。

---

### テスト層 (`tests/PDFBinder.Tests`)

#### [NEW] [`ToolToBooleanConverterTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ToolToBooleanConverterTests.cs)
- `ToolToBooleanConverter` の単体テスト:
  - `Convert`: 各 `EditorToolMode` 値とパラメータの一致・不一致・大文字小文字無視の判定。
  - `ConvertBack`:
    - `true` 時に正しい `EditorToolMode` が返却されること。
    - `false` 時に `Binding.DoNothing` が返却されること。
    - パラメータが不正な文字列や null の場合に `Binding.DoNothing` が返却されること。

#### [MODIFY] [`ViewModelsTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/ViewModelsTests.cs)
- `DetailEditorViewModel` のツール変更連動テスト:
  - `SelectedTool` を `Highlighter` に変更した際、`SelectedColor` が黄色、`StrokeThickness` が 12 になること。
  - `SelectedTool` を `Pen` に変更した際、`SelectedColor` が黒色、`StrokeThickness` が 2 になること。

---

## 検証計画

### 自動テスト
- `dotnet test`: すべての既存テストおよび新規テスト（`ToolToBooleanConverterTests`, `ViewModelsTests`）が 100% PASS することを確認。
- `dotnet build`: 警告・エラーなくビルドが成功することを確認。

### 手動検証確認項目
- アプリを起動し、ページ詳細手書きエディタを開く。
- ペンでストロークを描画後、「全消しゴム」ラジオボタンをクリックして、フリーズやクラッシュが発生せずツールが正常に切り替わることを確認。
- ストロークを消去できることを確認。
- 「部分消し」「蛍光ペン」「直線」「手のひら」など他のツールへの切り替えもスムーズに行えることを確認。
