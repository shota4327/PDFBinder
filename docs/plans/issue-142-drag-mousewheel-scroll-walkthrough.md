# Issue #142: グリッド表示でドラッグ中のマウスホイールスクロールおよびオートスクロール対応 検証報告

## 概要
PDFページのグリッド俯瞰ビューにおいて、ページドラッグ中（並び替え・外部PDF挿入）および余白ドラッグによるラバーバンド矩形選択中に、マウスホイールによる縦スクロールおよび画面上下端への接近による自動スクロール（オートスクロール）を可能にする機能を実装しました。

---

## 実施した変更内容

### 1. `AutoScroller` クラスの実装
- **ファイル**: [`src/PDFBinder.App/Controls/AutoScroller.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/AutoScroller.cs)
- **概要**:
  - `GridScrollViewer` の上端・下端 40px の領域にカーソルが進入した際、端に近づくほど高速になる可変速度（1フレームあたり最大24px）でスムーズに自動スクロールするコントローラー。
  - スクロール更新時に `Scrolled` イベントを発火し、ドロップ先インジケーター（青い縦線）やラバーバンド選択矩形をリアルタイムに再計算・追従。
  - 純粋な計算メソッド `CalculateScrollSpeed` を提供し、UI非依存で高精度な単体テストを実現。

### 2. `DragMouseWheelHook` クラスの実装
- **ファイル**: [`src/PDFBinder.App/Controls/DragMouseWheelHook.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/DragMouseWheelHook.cs)
- **概要**:
  - WPF の OLE ドラッグループ（`DoDragDrop`）中であっても、スレッド固有マウスフック（Win32 `WH_MOUSE`）を用いて `WM_MOUSEWHEEL` メッセージを確実にインターセプト。
  - 外部ファイルドラッグ中も `ComponentDispatcher.ThreadFilterMessage` を併用して安全かつ確実にホイール回転（delta）を検知。
  - `IDisposable` を実装し、ドラッグ終了時およびアンロード時にフックを完全解放。

### 3. `GridView.xaml.cs` の連携・リファクタリング
- **ファイル**: [`src/PDFBinder.App/Views/GridView.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml.cs)
- **概要**:
  - `StartDrag` / `EndDrag`、`OnGridDragOver`、`OnGridDragLeave`、`OnGridDrop` にフックとオートスクロールの開始・停止および更新処理を統合。
  - ラバーバンド矩形選択処理から `UpdateRubberBandSelection` メソッドを抽出し、マウス移動時だけでなくスクロール発生時にも選択状態を滑らかに追従更新（メソッドの30〜50行制限を遵守）。
  - ドラッグ操作中（ページドラッグ、外部ドラッグ、ラバーバンド選択中）は誤操作防止のため Ctrl+ホイールによるサムネイルズームを抑止し、通常の縦スクロールを実行。

### 4. ドキュメントの同期更新
- [`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md): 6.5節「グリッド俯瞰ビュー」にドラッグ中スクロール＆オートスクロールの仕様を追加。
- [`README.md`](file:///c:/Git/PDFBinder/README.md): 2節「俯瞰グリッド」の機能一覧にドラッグ中スクロール＆オートスクロールを追記。
- [`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md): 機能インベントリに `F61` を追加しステータスを「完了」に更新。

---

## 検証結果

### 1. 自動テスト
- **単体テストクラス**:
  - [`tests/PDFBinder.Tests/AutoScrollerTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/AutoScrollerTests.cs): 上端ゾーン、下端ゾーン、不感帯、境界値、異常値などの速度計算テスト（9件）
  - [`tests/PDFBinder.Tests/DragMouseWheelHookTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/DragMouseWheelHookTests.cs): ライフサイクル管理、二重Dispose安全性テスト（3件）
- **実行コマンド**: `dotnet test`
- **実行結果**:
  ```text
  成功!   -失敗:     0、合格:   406、スキップ:     0、合計:   406、期間: 3 s - PDFBinder.Tests.dll (net10.0)
  ```
  既存のテストを含め、全406件のテストが100%合格することを確認。

### 2. ビルド確認
- **実行コマンド**: `dotnet build`
- **実行結果**:
  ```text
  ビルドに成功しました。
      0 個の警告
      0 エラー
  ```
