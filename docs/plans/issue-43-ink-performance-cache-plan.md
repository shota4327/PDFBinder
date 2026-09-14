# [Issue #43] 手書き文字増加時の描画遅延改善とビットマップキャッシュ導入

## 概要
手書き文字・ストローク数が増加した際に発生するペンの追従遅延（ペン先から線が遅れてついてくる現象）を解消するため、以下の2段階のアプローチを組み合わせた高パフォーマンス描画アーキテクチャを実装します。

1. **描画パイプラインの最適化**:
   - ページコンテナに直接適用されていた `DropShadowEffect`（ピクセルシェーダー）を手書きキャンバスから完全分離し、描画中の毎フレーム全画面再ラスタライズ処理を根絶します。
   - ペンスレッド（`PenOnlyDynamicRenderer`）でのデバイス判定処理をキャッシュ化・軽量化します。
2. **ストロークのビットマップキャッシュ層（分離レイヤー）導入**:
   - 各ページの手書きストローク（`Page.InkStrokes`）を透明な高解像度ビットマップ画像（`StrokeCache`）としてキャッシュ化し、PDF背景画像と `InkCanvas` の間に配置します。
   - **ペン・蛍光ペン描画時**: `InkCanvas` 自身は「現在描画中の1ストロークのみ」を保持・描画するため、既存ストロークが1,000本あっても `InkCanvas` のベクター描画負荷は常にゼロ本と同等になり、極めて高速なペン追従性を維持します。確定時にキャッシュ画像へ合成されます。
   - **消しゴム・選択ツール時**: `Page.InkStrokes` の全ベクターデータを `InkCanvas` に展開して通常の消去・選択判定を行えるようにし、編集完了時またはペンツール復帰時にキャッシュ画像を再生成します。
   - **ズーム追従**: ズーム倍率変更時のデバウンス動的レンダリングと連携し、ズームに応じた高精細解像度でキャッシュを追従生成します。
   - **先行生成**: 既存ストロークを持つページは、詳細ビュー表示時にバックグラウンドで非同期先行生成を行い、スクロール時も滑らかに表示します。

---

## ユーザー確認事項（User Review Required）
> [!NOTE]
> ペン描画中は確定済みストロークが背面キャッシュ画像（高精細ビットマップ）として表示され、消しゴム・選択ツール選択時のみ一時的にベクターが InkCanvas 上に直接展開されます。見た目やUndo/Redo・消しゴム等の操作感はこれまでと同一のまま、描画レスポンスのみが劇的に向上します。

---

## 変更計画

### 1. 描画パイプラインの分離・軽量化 (WPF XAML / UI)

#### [MODIFY] [DetailEditorView.xaml](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- `DropShadowEffect` を含む最外枠 `Border` を影専用の背景要素（子要素なし）として分離。
- ページコンテンツ（背景画像、ストロークキャッシュ画像、`EditorInkCanvas`）を Effect のかかっていない独立した `Grid` 内に配置し、InkCanvas 描画時の毎フレーム GPU/CPU 再ラスタライズを防止。
- 背景PDF画像と `EditorInkCanvas` の間に `<Image Source="{Binding StrokeCache}" Stretch="Fill" />` を追加。

#### [MODIFY] [PenOnlyDynamicRenderer.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/PenOnlyDynamicRenderer.cs)
- `Tablet.TabletDevices` の判定結果をデバイスIDごとにキャッシュし、毎パケット（数百Hz）での無駄なコレクション列挙・COM呼び出しを削減。

---

### 2. ストロークキャッシュ管理サービス・ViewModel層

#### [NEW] [IStrokeCacheService.cs / StrokeCacheService.cs](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Services/StrokeCacheService.cs)
- `StrokeCollection` を指定された解像度（DIP寸法 × レンダリングスケール）の透過 `BitmapSource` に高速レンダリングする専用サービスを新設。
- 高解像度レンダリング（WPF `RenderTargetBitmap` / `DrawingVisual`）を活用し、蛍光ペンの半透明ブレンドや線の太さ、アンチエイリアスを忠実に再現。

#### [MODIFY] [DetailPageItemViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailPageItemViewModel.cs)
- `StrokeCache`（`BitmapSource?`）プロパティを追加。
- キャッシュの更新・破棄（Dispose）メソッドを追加。

#### [MODIFY] [DetailEditorViewModel.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
- ズーム変更時の `PerformDynamicRenderAsync` 内で、背景画像と同期して各ページの `StrokeCache` を最新ズームスケールで再生成。
- ツール切り替え（ペン ⇔ 消しゴム／選択ツール）に応じたキャッシュ・ベクター同期制御の追加。
- 初期表示時における既存ストローク保有ページの非同期先行キャッシュ生成処理を追加。

---

### 3. EditorInkCanvas のキャッシュ連携・最適化

#### [MODIFY] [EditorInkCanvas.cs](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- ペン描画完了時（`StrokeCollected`）に、確定ストロークをマスターコレクション（`Page.InkStrokes`）に追記しつつ、リアルタイムにキャッシュ画像を更新して InkCanvas を軽量状態にリセットするイベント／コールバックを実装。
- 直線ツール確定時のコミット処理も同様にキャッシュ層へ統合。

---

## 検証計画

### 自動テスト（Automated Tests）
1. `StrokeCacheServiceTests`:
   - 空のストロークコレクション、通常ペンストローク、蛍光ペンストロークの透過ビットマップレンダリングの正常性検証。
   - スケール（ズーム倍率）に応じたピクセル寸法の正確性検証。
2. `DetailEditorViewModelTests` / `EditorInkCanvasTests`:
   - ペン描画時のストロークコミットとキャッシュ更新トリガーの検証。
   - ツール切り替え時のストローク展開・同期動作の検証。
   - ズーム変更時のキャッシュ再生成トリガーの検証。
3. 全体テストの実行:
   - `dotnet test` が 100% PASS することを確認。
   - `dotnet build` がエラー・警告なく成功することを確認。

### 手動検証（Manual Verification）
- アプリを起動し、多数の手書き文字（数百ストローク以上）を書き込んだ状態でペン先の追従性および描画ラグがないことを確認。
- ズーム拡大・縮小時の文字の鮮明さと描画追従性を確認。
- 消しゴム（ストローク・部分）、直線ツール、Undo/Redoが正常に機能することを確認。
