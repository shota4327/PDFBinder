# Issue #52 追加改修-2 実装計画: 表示オプション切り替え時の即時反映

## 概要
表示オプションボタン（100%、ウィンドウに合わせる、幅に合わせる）をクリックして切り替えた瞬間、およびPDFファイル初回オープン時にも即座に拡大率が計算・反映されるように改善します。
※アイコンについては、別途Google Fontsへの移行作業にて対応するため、本計画からは除外します。

---

## 決定事項
1. **ボタン切り替え時の即時反映**:
   - `DetailEditorViewModel` の `FitMode` プロパティ変更時（`OnFitModeChanged`）に即座に `ApplyFitMode()` を実行し、ラジオボタンをクリックした瞬間に画面の拡大率が更新されるようにする。
2. **初回読み込み時の自動適用**:
   - ドキュメントを開いた初回表示時および `DetailEditorView` のロード時にも、デフォルトの「ウィンドウに合わせる」が即座に適用されるようにする。
3. **アイコン変更の除外**:
   - アイコンの変更は別途Google Fontsへの移行対応で行うため、今回は変更を行わない。

---

## 実装計画

### 1. ViewModel / View の改修
- **`DetailEditorViewModel.cs`**:
  - `partial void OnFitModeChanged(DetailViewFitMode value)` を追加し、モード変更時に `ApplyFitMode()` を即座に実行。
  - `InitializeDocument` 時に `ApplyFitMode()` を呼び出し、ファイルオープン直後にも即時反映。
- **`DetailEditorView.xaml.cs`**:
  - `OnLoaded` 時にビューポートサイズを伝達後、直ちに `ApplyFitMode()` が実行されるように連携を確実化。

### 2. 検証計画
- **単体テスト**:
  - `FitMode` プロパティを直接変更した際（ボタンのTwoWayバインディング相当）に即座に `Zoom` が再計算されることのテストを追加。
  - `InitializeDocument` 実行後にフィット計算が適用されることのテストを追加。
  - 全単体テスト（`dotnet test`）がPASSすることを確認。
- **ビルド検証**:
  - `dotnet build` が警告0、エラー0で成功することを確認。
- **起動確認**:
  - アプリケーションを起動し、表示オプションボタンをクリックした瞬間に拡大率が切り替わることを確認。
