# Issue #140 横スクロールバーの浮遊配置およびステータスバー重複解消 検証報告（Walkthrough）

## 変更概要
Issue #140 の要件に基づき、下部ステータスバー（半透明オーバーレイ配置・高さ42px）に横スクロールバーが隠れて見えなくなっていた問題を解消しました。
また、ヒアリング（Grill-me）での合意事項に基づき、縦スクロールバーの下端をステータスバー直上で停止させてズームボタンとの重なりを回避し、グリッドビューへの一貫適用、ダークテーマに調和するモダンスクロールバーデザインの導入、および `Shift + マウスホイール` による横スクロール操作のサポートを実施しました。

---

## 修正・改善内容

### 1. 浮遊オーバーレイスクロールビューアースタイルの新設 (`App.xaml`)
- `OverlayScrollViewerStyle`:
  - 2層構造の Grid テンプレートを採用：
    - **レイヤー0（最背面・全画面）**: `ScrollContentPresenter` を配置し、用紙やカードがステータスバーの背後（画面最下部）まで自然にスクロール通過できる仕様を完全に維持。
    - **レイヤー1（前面・スクロールバー層）**: 下部に42pxのステータスバースペーサー（Row 1）を設け、スクロールバーがステータスバー領域に侵入しない構造を構築。
    - **横スクロールバー (`PART_HorizontalScrollBar`)**: ステータスバー直上（下端から42pxの位置）に浮遊配置。ステータスバーに隠れず完全に視認・ドラッグ可能。
    - **縦スクロールバー (`PART_VerticalScrollBar`)**: 下端がステータスバー直上（42px上）で停止するため、右下のズームボタンとの重なりを回避。
    - 縦横両方のスクロールバーが表示された際も、Gridの列定義（Column 0: `*`, Column 1: `Auto`）により横スクロールバーの右端が縦スクロールバーの手前で自動収縮し、自然に直交。
- `ModernScrollBarStyle` / `ScrollBarThumbStyle`:
  - 不要な矢印ボタンを排除したフラット・スリム（幅・高さ12px）デザイン。
  - チャコールダークテーマ（`#424242`, `#4F4F4F`, `#686868`）に調和する角丸サムネイル（Thumb）を定義。

### 2. 詳細エディタおよびグリッドビューへのスタイル適用
- `DetailEditorView.xaml`: `DetailScrollViewer` に `OverlayScrollViewerStyle` を適用。
- `GridView.xaml`: `GridScrollViewer` に `OverlayScrollViewerStyle` を適用し、アプリ全体で統一された外観と操作性を実現。

### 3. Shift + マウスホイールによる水平スクロール機能 (`DetailEditorView.xaml.cs`)
- `OnScrollViewerPreviewMouseWheel` を責務ごとにプライベートメソッド（`HandleZoomWheel`, `HandleHorizontalScrollWheel`, `HandleSinglePageWheelTurn`）へ適切に分割（メソッド行数制限30〜50行を厳守）。
- `Shift` キー押下中にマウスホイールを回転させた際、1ノッチ（Delta 120）あたり48pxの水平スクロールを実行し、直感的な左右移動を実現。

### 4. 単体テストの追加 (`OverlayScrollViewerStyleTests.cs`)
- `OverlayScrollViewerStyle` が適用された ScrollViewer のテンプレート生成、`PART_HorizontalScrollBar` / `PART_VerticalScrollBar` の存在、方向（Orientation）、および42pxスペーサーの配置を自動検証するテストを作成。

### 5. バージョン更新とドキュメント同期
- `Directory.Build.props`: 不具合修正および改善に伴い `0.4.0` → `0.4.1` へインクリメント。
- `CHANGELOG.md`: エンドユーザー向けの `0.4.1` リリースノートを記述し、空の `[Unreleased]` を配置。
- `docs/basic_design.md` & `docs/PROJECT.md`: 浮遊オーバーレイスクロールバー、Shift+ホイール横スクロール機能（F63）を反映。

---

## 変更ファイル一覧
1. **[App.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/App.xaml)**:
   - `ScrollBarTrackRepeatButtonStyle`, `ScrollBarThumbStyle`, `ModernScrollBarStyle`, `OverlayScrollViewerStyle` を定義
2. **[DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)**:
   - `DetailScrollViewer` に `Style="{StaticResource OverlayScrollViewerStyle}"` を適用
3. **[GridView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/GridView.xaml)**:
   - `GridScrollViewer` に `Style="{StaticResource OverlayScrollViewerStyle}"` を適用
4. **[DetailEditorView.xaml.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml.cs)**:
   - Shift + ホイール横スクロール処理を追加し、ホイール処理を分割リファクタリング
5. **[OverlayScrollViewerStyleTests.cs](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/OverlayScrollViewerStyleTests.cs)**:
   - 浮遊スクロールバーのテンプレート構造および配置の単体テストを新規作成
6. **[Directory.Build.props](file:///c:/Git/PDFBinder/Directory.Build.props)**:
   - バージョンを `0.4.1` にインクリメント
7. **[CHANGELOG.md](file:///c:/Git/PDFBinder/CHANGELOG.md)**:
   - `0.4.1` のリリースノートを追記
8. **[basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md)** & **[PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)**:
   - 基本設計書と機能インベントリを同期更新

---

## 検証結果

### 1. ビルド検証
```pwsh
dotnet build
```
- **結果**: 警告 0 件、エラー 0 件でビルド成功。

### 2. 単体テスト検証
```pwsh
dotnet test
```
- **結果**: 全 426 件合格（成功: 426、失敗: 0、スキップ: 0）。新規追加テストを含め全テストが 100% PASS。
