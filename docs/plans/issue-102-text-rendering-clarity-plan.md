# ウェルカム表示およびUIテキストの描画ぼやけ・滲み解消 実装計画 (Issue #102)

## 概要
起動直後のウェルカム表示（「PDFファイルを開くか、ここにドラッグ＆ドロップしてください」）や中央配置の要素において、WPF のデフォルト描画設定（小数座標によるサブピクセル配置、`TextFormattingMode="Ideal"`、フォントフォールバック時のアンチエイリアス滲み等）に起因して文字や枠線がぼやけて見える現象を解消し、アプリ全体でくっきりと鮮明なレンダリングを実現します。

## 問題の根本原因
1. **サブピクセル配置（半ピクセルずれ）**: ウェルカムカードが `HorizontalAlignment="Center" VerticalAlignment="Center"` で配置されているが、`UseLayoutRounding` が未設定のため、計算結果が `0.5px` 等の端数になりピクセル境界を跨いで描画され滲む。
2. **WPF のテキスト整形モード**: デフォルトの `TextOptions.TextFormattingMode="Ideal"` が使用されており、画面の物理ピクセルグリッドへのフィッティング（ヒンティング）が無効化されているため、文字輪郭がソフト化・ぼやける。
3. **フォント未指定とフォールバック**: `TextBlock` に共通フォントが設定されておらず、`Segoe UI`（欧文）から日本語フォントへのフォールバック時に `FontWeight="SemiBold"` が合わさり、文字の線が潰れて滲みやすい。

## 変更方針

### 1. アプリ全体でのフォント・レンダリング基盤の整備 (`App.xaml`)
- アプリ共通のフォントファミリリソース `AppFontFamily`（`Segoe UI Variable, Segoe UI, Meiryo, sans-serif`）を定義。
- `BaseFont` スタイルでもこのリソースを参照するように統一。

### 2. メインウィンドウでのピクセル丸めと画面向けテキスト描画の有効化 (`MainWindow.xaml`)
- `<Window>` のルート属性に以下を追加:
  - `UseLayoutRounding="True"`（レイアウト計算結果を物理ピクセル境界に丸める）
  - `TextOptions.TextFormattingMode="Display"`（文字のグリフ輪郭をピクセルグリッドにスナップさせる）
  - `TextOptions.TextRenderingMode="ClearType"`（サブピクセルRGB描画を明示）
  - `FontFamily="{StaticResource AppFontFamily}"`（ウィンドウ配下の全コントロール・TextBlockへ日本語対応フォントを自動継承）

### 3. 各ビューのウェルカムカードの枠線スナップ (`GridView.xaml`, `DetailEditorView.xaml`)
- ウェルカム表示の `Border` に `SnapsToDevicePixels="True"` を付与し、1pxの枠線が滲まず鮮明に描画されるようにする。

### 4. ドキュメントの同期更新 (`docs/basic_design.md`)
- 基本設計書のUI設計・レンダリング指針に、上記ピクセル丸めおよび文字レンダリング最適化方針を追記・同期。

## 影響範囲とファイル
- `src/PDFBinder.App/App.xaml`: `AppFontFamily` リソース追加
- `src/PDFBinder.App/MainWindow.xaml`: `UseLayoutRounding`, `TextOptions`, `FontFamily` 追加
- `src/PDFBinder.App/Views/GridView.xaml`: `SnapsToDevicePixels` 追加
- `src/PDFBinder.App/Views/DetailEditorView.xaml`: `SnapsToDevicePixels` 追加
- `docs/basic_design.md`: レンダリング方針のドキュメント追記

## 検証手順
1. `dotnet build` によるビルドエラー・警告の検証
2. `dotnet test` による既存全単体テストの実行・パス確認
3. アプリケーション起動時のレイアウト確認および表示検証
