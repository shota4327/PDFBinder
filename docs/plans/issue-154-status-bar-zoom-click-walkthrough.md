# Issue #154 検証報告: ステータスバー右下の拡大率表示クリック時動作変更

## 1. 実施概要
Issue [#154](https://github.com/shota4327/PDFBinder/issues/154) に基づき、ステータスバー右下の拡大率表示ボタン（`CurrentZoomText`）をクリックした際、詳細ビュー表示中であればツールバーの表示オプション順（「100%表示」→「ウィンドウに合わせる」→「幅に合わせる」）にサイクル切り替えを行う機能を実装しました。また、手動ズーム等によりフィットモードが未選択（None）の場合は「ウィンドウに合わせる」に切り替わり、グリッドビュー表示中は従来どおりサムネイル倍率を100%（標準サイズ）にリセットします。

## 2. 変更内容

### 2.1 ViewModel 実装
- **`DetailEditorViewModel.cs`**:
  - `CycleFitMode()` メソッドおよび `[RelayCommand]` を追加。
  - 現在の `FitMode` に基づき、`ActualSize` → `FitToWindow` → `FitToWidth` → `ActualSize` の順に遷移。`None` 等の未選択状態からは `FitToWindow` へ遷移。
- **`MainViewModel.cs`**:
  - `CycleZoomMode()` メソッドおよび `[RelayCommand]` を追加。
  - `IsDetailViewActive` が true の場合は `DetailEditor.CycleFitModeCommand` を実行。
  - `IsDetailViewActive` が false の場合は `ThumbnailSize = DefaultThumbnailSize` で 100% リセット。
  - 既存のリボンツールバー「100%」ボタンおよびショートカットキー「Ctrl+0」用の `ZoomResetCommand` は分離して維持。

### 2.2 XAML / View 実装
- **`MainWindow.xaml`**:
  - ステータスバー右側のズームボタンのバインドコマンドを `CycleZoomModeCommand` に変更。
  - ToolTip を `"表示モード切り替え (100% → ウィンドウ → 幅)"` に更新。

### 2.3 ドキュメント・バージョン管理
- **`Directory.Build.props`**: バージョンを `0.4.5` → `0.4.6` にインクリメント。
- **`CHANGELOG.md`**: `[0.4.6] - 2026-09-24` セクションをエンドユーザー向けに記述。
- **`docs/basic_design.md`**: ステータスバー右端のズームコントロールのクリック仕様を更新。
- **`docs/PROJECT.md`**: 機能インベントリ F38 に Issue #154 を反映。
- **`README.md`**: クイックズームコントロールの説明を更新。

## 3. テスト・検証結果

### 3.1 単体テスト実行結果
- **`DetailEditorViewModelTests.cs`**:
  - `CycleFitMode_CyclesThroughModes_InCorrectOrder`: ActualSize → FitToWindow → FitToWidth → ActualSize の循環遷移を検証（PASS）。
  - `CycleFitMode_WhenModeIsNone_SwitchesToFitToWindow`: None 状態から FitToWindow への遷移を検証（PASS）。
- **`StatusBarAndErrorDialogTests.cs`**:
  - `CycleZoomModeCommand_WhenDetailViewActive_CyclesFitMode`: 詳細ビュー時のサイクル実行および None からの復帰を検証（PASS）。
  - `CycleZoomModeCommand_WhenGridViewActive_ResetsThumbnailSizeToDefault`: グリッドビュー時の 100% リセットを検証（PASS）。

```
成功!   -失敗:     0、合格:   439、スキップ:     0、合計:   439、期間: 4 s - PDFBinder.Tests.dll (net10.0)
```

### 3.2 ビルド結果
- `dotnet build`: 警告 0、エラー 0 でビルド成功。
