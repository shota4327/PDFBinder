# Issue #154 実装計画: ステータスバー右下の拡大率表示クリック時動作変更

## 1. 概要
ステータスバー右下に配置されている拡大率表示ボタン（`CurrentZoomText`）をクリックした際、詳細ビュー表示中であれば「100%表示」「ウィンドウに合わせる」「幅に合わせる」の各表示フィットモードを順繰りに切り替える（サイクルする）ように変更する。

## 2. 仕様・決定事項

### 2.1 モード切り替えサイクル（詳細ビュー時）
ツールバー「表示」タブの表示オプション（100%、ウィンドウ、幅）の並び順に従って、クリックするたびに右隣のモードへ切り替え、幅の次は100%へ戻る。
- **ActualSize (100%)** → **FitToWindow (ウィンドウに合わせる)**
- **FitToWindow (ウィンドウに合わせる)** → **FitToWidth (幅に合わせる)**
- **FitToWidth (幅に合わせる)** → **ActualSize (100%)**
- **None (手動ズーム中)** → **FitToWindow (ウィンドウに合わせる)**

### 2.2 グリッドビュー時の動作
- 従来どおりサムネイル倍率を 100%（`DefaultThumbnailSize` = 220）にリセットする。

### 2.3 既存コマンドとの分離
- リボンツールバー「表示」タブにある「100%」ボタンおよびショートカットキー「Ctrl+0」は、従来の `ZoomResetCommand`（100%等倍リセット）を維持する。
- ステータスバー専用のコマンド `CycleZoomModeCommand`（`MainViewModel`）および `CycleFitModeCommand`（`DetailEditorViewModel`）を新設する。

### 2.4 ツールチップ
- ステータスバーの拡大率ボタンの ToolTip を「表示モード切り替え (100% → ウィンドウ → 幅)」に変更する。

---

## 3. 実装詳細

### 3.1 `DetailEditorViewModel.cs`
- `CycleFitMode()` メソッドおよび `[RelayCommand]` を追加。
  - 現在の `FitMode` に応じて次のモードを決定し、`SetFitMode(nextMode)` を呼び出す。
  - 各メソッドは30〜50行ルールおよび日本語コメント規約を遵守。

### 3.2 `MainViewModel.cs`
- `CycleZoomMode()` メソッドおよび `[RelayCommand]` を追加。
  - `IsDetailViewActive` が true の場合: `DetailEditor?.CycleFitMode()` を呼び出し。
  - `IsDetailViewActive` が false の場合: `ThumbnailSize = DefaultThumbnailSize`。
  - `CurrentZoomText`, `CanZoomIn`, `CanZoomOut` の通知を更新。

### 3.3 `MainWindow.xaml`
- ステータスバー右側のズームボタンの `Command` を `{Binding CycleZoomModeCommand}` に変更。
- ToolTip を `"表示モード切り替え (100% → ウィンドウ → 幅)"` に更新。

### 3.4 単体テスト
- `DetailEditorViewModelTests` / `ViewModelsTests`:
  - `CycleFitMode` の各遷移（ActualSize → FitToWindow → FitToWidth → ActualSize、None → FitToWindow）を検証するテストを追加。
- `MainViewModelTests`:
  - グリッドビュー時、詳細ビュー時それぞれの `CycleZoomModeCommand` 実行結果を検証するテストを追加。

### 3.5 ドキュメント更新
- `docs/basic_design.md` にステータスバー拡大率ボタンのクリック仕様を反映。
- `docs/PROJECT.md` の機能インベントリを更新。
- 作業ブランチ内で `Directory.Build.props` をバージョンインクリメント（パッチまたはマイナー）し、`CHANGELOG.md` を更新。

---

## 4. 検証計画
- `dotnet test`: すべての既存テストおよび新規追加テストが PASS すること。
- `dotnet build`: 警告・エラーなく正常にビルドできること。
