# Issue #200 実装計画: ファイル切り替え・新規オープン時の拡大率およびウィンドウサイズの保持

## 1. 概要と目的
本改修では、以下の2つの課題を解決します。
1. **ウィンドウサイズ・最大表示の維持**:
   - ファイルを新しく開いた時（特にエクスプローラーからのダブルクリック等による既存インスタンスへのファイルオープン時）、既存のウィンドウが最大表示（Maximized）になっていると、通常ウィンドウ表示に変更されてしまう問題を解消し、元のウィンドウサイズおよび最大化状態を完全に保持する。
2. **ファイル切り替え時の表示方法に応じた拡大率制御**:
   - ファイル切り替え前後でウィンドウサイズが変更されている場合、各ドキュメントの表示設定（FitMode）に基づいて拡大率を適切に制御する。
     - **ウィンドウに合わせる（FitToWindow）**: 変更後のウィンドウサイズに合わせて拡大率を再計算
     - **幅に合わせる（FitToWidth）**: 変更後のウィンドウサイズに合わせて拡大率を再計算
     - **100%表示（ActualSize）**: 100%（1.0）のまま元の拡大率を維持
     - **手動ズーム（None）**: 手動設定された元の拡大率をそのまま維持

---

## 2. 現状の課題と原因分析

### 2.1 ダブルクリック起動時の最大化解除（ウィンドウ表示に戻る）
- **原因箇所**: `WindowActivationHelper.BringToForeground`（[WindowActivationHelper.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Helpers/WindowActivationHelper.cs#L102-L123)）
  ```csharp
  public static void BringToForeground(Window window)
  {
      if (window.WindowState == WindowState.Minimized)
      {
          window.WindowState = WindowState.Normal;
      }
      ...
      ShowWindow(hwnd, SwRestore); // SwRestore (9) は最大化ウィンドウを通常ウィンドウに縮小復元してしまう
      ...
  }
  ```
- Win32 API の `ShowWindow(hwnd, SW_RESTORE)` は、ウィンドウが最大化（Maximized）されている場合、通常サイズ（Normal）に復元する仕様となっています。
- また、最小化（Minimized）されている場合も無条件に `WindowState = WindowState.Normal` に戻しているため、最小化前に最大化されていた情報が失われて通常表示になってしまいます。

### 2.2 ファイル（タブ）切り替え時のプロパティ同期順序と拡大率再計算
- **原因箇所**: `MainViewModel.OnActiveSessionChanged`（[MainViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/MainViewModel.cs#L624-L667)）
  - `Document = newValue.Document;` 代入時に `OnDocumentChanged` が発火し、その中で `DetailEditor.InitializeDocument` -> `ApplyFitMode()` が実行されます。
  - しかし、この時点ではまだ `DetailEditor.FitMode` が切り替え先の `newValue.FitMode` に同期されていないため、直前のドキュメントの FitMode のまま拡大率が計算されてしまう不整合が生じます。
  - 切り替え先の `FitMode` に基づく拡大率の再計算および復元処理が整流化されていません。

---

## 3. 実装方針

### 3.1 ウィンドウサイズ・最大化状態の保持 (`WindowActivationHelper.cs`, `MainWindow.xaml.cs`)
1. **最小化前の状態追跡**:
   - `MainWindow` に `PreviousWindowState`（または `NonMinimizedWindowState`）プロパティを設け、`StateChanged` イベント発生時に `WindowState` が `Minimized` 以外（`Normal` または `Maximized`）である場合に記録・更新します。
2. **`BringToForeground` の修正**:
   - ウィンドウが最小化されている場合（`WindowState == WindowState.Minimized`）:
     - 最小化前の状態（`PreviousWindowState`）が `Maximized` であれば `WindowState = WindowState.Maximized`、それ以外は `WindowState = WindowState.Normal` に復元します。
   - ウィンドウが最小化されていない場合（通常表示中または最大化表示中）:
     - `WindowState` やウィンドウサイズは一切変更しません。
   - Win32 API 呼び出し:
     - 最大化状態のウィンドウに対して `SW_RESTORE` を呼び出すのを防ぎ、`SetForegroundWindow` のみ、または適切な表示コマンド（`SW_SHOW`）を使用します。

### 3.2 ファイル切り替え時の拡大率制御 (`MainViewModel.cs`)
1. **プロパティ同期の整流化**:
   - `OnActiveSessionChanged` において、`Document = newValue.Document` を設定する前に、あらかじめ `DetailEditor.FitMode` を `newValue.FitMode` に設定します。
2. **表示モードに応じた拡大率の適用**:
   - `FitToWindow` / `FitToWidth`:
     - 現在のウィンドウサイズ（`ViewportWidth`, `ViewportHeight`）に基づいて `DetailEditor.ApplyFitMode()` を実行し、変更後のウィンドウサイズに最適な拡大率へ再計算します。
   - `ActualSize`:
     - `DetailEditor.ApplyFitMode()`（または `SetZoom(1.0)`）により、100%表示を維持します。
   - `None`（手動ズーム中）:
     - `DetailEditor.Zoom = newValue.ZoomFactor;` により、元の拡大率を維持します。
3. **新規ファイル追加時**:
   - 常にデフォルト設定（`FitToWindow`）として生成され、現在のウィンドウサイズに合わせて自動調整されます。

---

## 4. 影響範囲とリスク管理
- **影響範囲**:
  - `src/PDFBinder.App/Helpers/WindowActivationHelper.cs`
  - `src/PDFBinder.App/MainWindow.xaml.cs`
  - `src/PDFBinder.App/ViewModels/MainViewModel.cs`
  - `tests/PDFBinder.Tests/` 配下の関連テスト
- **リスクと対策**:
  - 他プロセスからのフォアグラウンド移行時におけるOS制限（フォーカス奪取制限）を阻害しないよう、既存の `AllowSetForegroundWindow` や Topmost 一時トグル処理は安全に維持します。
  - テスト環境（ヘッドレス / 非UIスレッド）でのテスト容易性を確保するため、ヘルパーメソッドやロジックを静的/分離メソッドとして設計します。

---

## 5. テスト計画
1. **単体テストの追加**:
   - `WindowActivationHelperTests`:
     - 最大化状態のウィンドウを前面化しても `WindowState.Maximized` が維持されること。
     - 最小化前の状態（Maximized / Normal）が正しく復元されること。
   - `MainViewModelMultiFileTests`:
     - ファイルA（FitToWindow）とファイルB（FitToWidth）を切り替えた際、ウィンドウサイズが変化しても各々の FitMode に基づいて再計算されること。
     - ファイルC（ActualSize / 100%）または手動ズーム（None）のセッションにおいて、ウィンドウサイズ変更後に切り替えても元の倍率がそのまま維持されること。
2. **リグレッション検証**:
   - `dotnet test`: 既存テストを含め全テストが 100% PASS すること。
   - `dotnet build`: ビルドが警告・エラーなく成功すること。
