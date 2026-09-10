# 手書きエディタのパームリジェクションとタッチ操作（1本指パン・2本指ピンチズーム）実装計画

詳細手書きエディタ（`EditorInkCanvas`）において、スタイラスペン（デジタイザー）と手指によるタッチ入力を分離し、パームリジェクション（誤検知防止）および直感的なタッチジェスチャー（1本指パン・2本指ピンチズーム）を実装します。

## 決定事項（`/grill-me` にて合意済み）
1. **入力分離**:
   - スタイラスペン（`TabletDeviceType.Stylus`）: 描画・消去など選択中ツールの動作を常時実行。
   - 手指（`TabletDeviceType.Touch`）:
     - 1本指タッチ: 画面のパン（スクロール移動）。
     - 2本指タッチ: 指の中心位置を起点とする滑らかなピンチズーム（拡大・縮小）＆パン移動。
2. **パームリジェクション**:
   - ペン先が画面に接地中、またはペンが画面に近接（ホバー）している間は、手指タッチによる移動・拡大操作を一時抑止し、筆記時の手首接触による画面揺れを防止。
   - ペンを画面から離すと即座に手指によるパン＆ピンチズームを受け付ける。
3. **既存操作の維持**:
   - ツールバーの「✋ 手のひら」ツールは維持し、マウス操作時やペン先ドラッグによる移動も引き続きサポート。

---

## 提案される変更

### コア・ヘルパー層 (`PDFBinder.App.Helpers`)

#### [NEW] `PinchZoomHelper.cs`
- ピンチズーム時の新しいズーム倍率および中心点追従スクロールオフセットを計算する純粋関数ロジック。
- 拡大縮小の中心となる指の中間座標を保持したまま、ScrollViewerの `HorizontalOffset` / `VerticalOffset` の目標値を算出。
- ズーム範囲（0.5倍〜3.0倍）の境界値クランプ処理。
- 単体テスト可能（WPF UIスレッド非依存）。

---

### コントロール・View層 (`PDFBinder.App.Controls` / `PDFBinder.App.Views`)

#### [MODIFY] `EditorInkCanvas.cs`
- `StylusDevice.TabletDevice.Type` による入力デバイス種別の判別:
  - `TabletDeviceType.Touch` による `PreviewStylusDown` / `Move` / `Up` をブロックし、指タッチによるインク描画を抑止。
- ペン近接・筆記状態トラッキング:
  - `StylusDown`, `StylusUp`, `StylusInAirMove`, `StylusInRange`, `StylusOutOfRange` によるペンの接地および近接（ホバー）検知。
  - `IsStylusSuppressed()` によるパームリジェクション判定。
- マルチタッチイベント処理:
  - `PreviewTouchDown`, `PreviewTouchMove`, `PreviewTouchUp`:
    - 1本指タッチ時: 画面パン（ScrollViewerスクロール）。
    - 2本指タッチ時: `PinchZoomHelper` を利用した中心点追従ピンチズーム＆スクロール連動。
- 既存のマウス操作（直線ツール、手のひらツール）はそのまま保持。

#### [MODIFY] `DetailEditorViewModel.cs`
- ズーム倍率の直接設定用メソッド（またはプロパティ）の提供（ピンチズーム時の連続的な倍率変更に対応）。
- ズーム最小値（`0.5`）・最大値（`3.0`）の定数化。

---

### テスト層 (`tests/PDFBinder.Tests`)

#### [NEW] `PinchZoomHelperTests.cs`
- `PinchZoomHelper` の計算精度・境界値・中心点追従ロジックの単体テスト。
  - 拡大時のオフセット計算の正確性（指の中心位置がズレないこと）
  - 縮小時のオフセット計算の正確性
  - ズーム上下限値（0.5〜3.0）でのクランプ動作
  - ゼロ除算・微小距離での安全性テスト

---

## 検証計画

### 1. 自動テスト (Automated Tests)
- `dotnet test`: 新規追加の `PinchZoomHelperTests` を含む全テストスイートの 100% PASS を検証。

### 2. ビルド検証
- `dotnet build`: 警告・エラーなしで正常終了することを確認。

### 3. レビューおよびユーザー確認
- 実装コード、テスト結果、Walkthrough（検証報告書）を作成し、ユーザーへ報告。
