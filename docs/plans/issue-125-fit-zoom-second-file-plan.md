# [実装計画] Issue #125: 2つ目以降のPDF追加時に「ウィンドウに合わせる」が適用されず100%表示になる不具合を修正

## 概要
PDFファイルを関連付けダブルクリック等で開く際、2つ目以降のドキュメントが追加されたときに「ウィンドウに合わせる (FitToWindow)」の自動計算が正しく適用されず、常に100%（等倍）で表示されてしまう問題を修正します。

## 原因分析
1. `DocumentSession` の初期化時に `ZoomFactor` のデフォルト値が `1.0` に固定されており、`FitMode` がセッション状態として保持されていない。
2. `MainViewModel.OnActiveSessionChanged` において、`DetailEditor.InitializeDocument` によるフィット拡大率の計算直後に、無条件で `DetailEditor.Zoom = newValue.ZoomFactor;`（= 1.0）が代入され、計算されたフィット倍率が 100% で上書きされていた。
3. `DetailEditor.FitMode` 自体は `FitToWindow` のまま維持されるため、表示倍率（100%）とフィットモード（FitToWindow）の不整合が発生していた。

## 変更対象ファイルと設計詳細

### 1. `DocumentSession.cs` [MODIFY]
- `FitMode` プロパティ（デフォルト: `DetailViewFitMode.FitToWindow`）を追加。

### 2. `MainViewModel.cs` [MODIFY]
- `OnActiveSessionChanged`:
  - 旧セッション退避時: `oldValue.FitMode = DetailEditor?.FitMode ?? DetailViewFitMode.FitToWindow;`
  - 新セッション適用時:
    - `DetailEditor.FitMode = newValue.FitMode;`
    - `newValue.FitMode != DetailViewFitMode.None` の場合は `DetailEditor.ApplyFitMode();` を実行。
    - 手動ズーム時（`newValue.FitMode == DetailViewFitMode.None`）のみ `DetailEditor.Zoom = newValue.ZoomFactor;` を適用。
- `OnDetailEditorPropertyChanged`:
  - `FitMode` および `Zoom` の変更を `ActiveSession` にリアルタイム反映。
- `OnIsDetailViewActiveChanged`:
  - 詳細ビューへ切り替えた際にも `FitMode != None` であれば `ApplyFitMode()` を実行。

### 3. 単体テストの追加 [MODIFY]
- `tests/PDFBinder.Tests/DocumentSessionTests.cs` または `MainViewModelTests.cs` にて、セッション切り替え時および新規ドキュメント追加時の `FitMode` / `Zoom` 保持・適用を検証。

---

## 検証計画 (Verification Plan)
- `dotnet test` で全テストが 100% PASS することを確認。
- `dotnet build` で 0 警告 / 0 エラーでビルド成功することを確認。
