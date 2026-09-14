# [Issue #43] 手書き文字増加時の描画遅延改善とビットマップキャッシュ導入 検証報告

## 変更概要
1ページ内に手書き文字やストロークが大量に蓄積された際に発生していたペンの描画追従遅延（ペン先から線が遅れてついてくる現象）を解消するため、描画パイプラインの最適化とストロークの透過ビットマップキャッシュ機構を導入しました。

1. **描画パイプラインの最適化**:
   - `DetailEditorView.xaml` において、`DropShadowEffect`（ピクセルシェーダー）を手書きキャンバス・背景画像を含む親 `Border` から分離。影専用の背景ダミー要素に移動し、`EditorInkCanvas` 描画中の毎フレーム全画面再ラスタライズ処理を根絶。
   - `PenOnlyDynamicRenderer` でのペンスレッド処理において、入力元デバイス判定（`TabletDeviceType.Touch`）の結果を `ConcurrentDictionary` にキャッシュ化し、数百Hzのパケット処理負荷を削減。
2. **ストローク・ビットマップキャッシュ層（分離レイヤー）導入**:
   - `IStrokeCacheService` / `StrokeCacheService`: `StrokeCollection` を指定された解像度の透過 `BitmapSource` に高速レンダリングする専用サービスを新設。
   - `DetailPageItemViewModel`: 確定済みストロークを保持・表示する `StrokeCache`（透過画像）プロパティを追加。
   - **ペン・蛍光ペン描画時**: `EditorInkCanvas` 内部は常に身軽な状態（現在描画中の1ストロークのみ）を保ち、確定済みストロークは背面キャッシュ画像側でレンダリング表示。これにより既存ストロークが1,000本あっても、`EditorInkCanvas` 内のベクター要素数は常に0または1本となり、描画遅延・負荷を根本的にゼロ近傍へ抑え込みました。
   - **消しゴム・選択ツール時**: `Page.InkStrokes` の全ベクターデータを `EditorInkCanvas` に展開して直接編集を行い、編集完了後またはペンモード復帰時にキャッシュ画像を自動再生成。
   - **ズーム連動**: ズーム倍率変更時のデバウンス動的レンダリングと同期して、最新ズーム解像度に応じた高品質キャッシュを動的追従生成。

---

## テスト結果

### 自動テスト（Automated Tests）
- コマンド: `dotnet test`
- 結果: **全 131 テストが 100% PASS**（失敗 0、スキップ 0）
  - `StrokeCacheServiceTests`: 透過ビットマップの生成、寸法計算、蛍光ペン透過レンダリング、境界値検証（PASS）
  - `EditorInkCanvasCacheTests`: ペンモード時の身軽化、消しゴムモード時の全ストローク展開とキャッシュ非表示、消去時のマスター同期、直線ツールコミット連携の検証（PASS）
  - `DetailEditorViewModelTests`: ズーム連動キャッシュ更新、ストローク有無に応じたキャッシュ生成の検証（PASS）
  - 既存の全単体テスト（手書き直線、パームリジェクション、PDF操作、Undo/Redoなど）もすべて継続してPASS。

### ビルド検証（Build Validation）
- コマンド: `dotnet build`
- 結果: **0 警告、0 エラーでビルド成功**

---

## 変更ファイル一覧
- `src/PDFBinder.Core/Services/IStrokeCacheService.cs` [NEW]
- `src/PDFBinder.Core/Services/StrokeCacheService.cs` [NEW]
- `src/PDFBinder.App/Controls/PenOnlyDynamicRenderer.cs` [MODIFY]
- `src/PDFBinder.App/Controls/EditorInkCanvas.cs` [MODIFY]
- `src/PDFBinder.App/ViewModels/DetailPageItemViewModel.cs` [MODIFY]
- `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs` [MODIFY]
- `src/PDFBinder.App/Views/DetailEditorView.xaml` [MODIFY]
- `tests/PDFBinder.Tests/StrokeCacheServiceTests.cs` [NEW]
- `tests/PDFBinder.Tests/EditorInkCanvasCacheTests.cs` [NEW]
- `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs` [MODIFY]
- `docs/basic_design.md` [MODIFY]
- `docs/PROJECT.md` [MODIFY]
