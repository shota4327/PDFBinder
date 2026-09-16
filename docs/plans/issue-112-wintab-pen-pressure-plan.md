# 実装計画: WinTab API連携による外付けペンタブレットの筆圧感知対応 (Issue #112)

## 概要
LenovoノートPCなどの内蔵ペン（Windows Inkネイティブ対応）では筆圧が機能する一方、デスクトップPC環境等でWacomペンタブレット等の外付けタブレットを使用し、ドライバ側で「Windows Ink を使う」が無効化されている（WinTab専用モード）環境において、WPFのInkCanvasで筆圧が失われてマウスとして扱われる課題を解決します。
自前P/Invoke（`wintab32.dll`）による軽量・安全なWinTab連携を実装し、Windows Inkが無効でも高精度な筆圧感知手書き描画を実現します。

---

## ユーザー合意事項（設計決定）
1. **実装基盤**: 自前P/Invokeラッパー方式（外部NuGetパッケージ不使用、.NET 10 64bit完全対応、単一EXEポータブル原則およびMITライセンスを厳守）。
2. **入力共存アーキテクチャ**: WinTab動的抑制＆インク注入方式（WinTabペン操作中はマウスの自動ストローク生成を抑制し、WinTabの高精度座標・筆圧でストロークを構築。通常マウスや内蔵ペンとも完全共存）。
3. **座標変換**: WinTabスクリーンマッピング ＋ WPF `PointFromScreen` 連携（マルチモニタ・Per-Monitor DPI・ズーム・スクロールに自動追従）。
4. **筆圧カーブ**: リニア正規化 ＋ 最小筆圧クランプ（`0.05`〜`1.0`）（Wacomドライバ設定を尊重し、内蔵ペンと同等の描き味を維持）。
5. **ライフサイクルと通知**: 動的プローブによる安全フォールバック（WinTab未導入PCでもクラッシュゼロ）＋ 筆圧ボタンのツールチップおよびステータスバーへの接続状態通知。

---

## 提案される変更内容

### 1. WinTab P/Invoke ネイティブ相互運用層
- [NEW] [`WinTabNativeMethods.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Interop/WinTab/WinTabNativeMethods.cs)
  - `wintab32.dll` の最小限の C言語 API（`WTInfoW`, `WTOpenW`, `WTPacket`, `WTClose`, `WTOverlap`）を定義。
  - 必要な構造体（`LOGCONTEXTW`, `PACKET`, `AXIS` 等）を 64bit/32bit 両対応で型安全に実装。
- [NEW] [`WinTabService.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Interop/WinTab/WinTabService.cs) / [`IWinTabService.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Interop/WinTab/IWinTabService.cs)
  - `wintab32.dll` の存在チェックと安全な動的プローブ。
  - ウィンドウハンドル（`HwndSource`）に対する WinTab コンテキストの生成・解放。
  - タブレットのデバイス名、最大筆圧レベル（$P_{\max}$）の取得。
  - `WT_PACKET` ウィンドウメッセージをフックしてパケットを受信し、スクリーン座標および正規化筆圧（`PressureFactor`）をイベントとして発行。

### 2. 筆圧計算ヘルパー
- [NEW] [`WinTabPressureHelper.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.Core/Helpers/WinTabPressureHelper.cs)
  - 生の筆圧値（$0 \sim P_{\max}$）を WPF の `0.05 \sim 1.0` の `float` 値にリニア正規化・クランプする純粋関数ロジック（単体テスト容易）。

### 3. EditorInkCanvas への WinTab インク注入パイプライン
- [MODIFY] [`EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
  - ウィンドウロード時に `WinTabService` を初期化・購読。
  - WinTabペン接地時（`PenDown`）:
    - 通常マウスによるインク生成を抑制。
    - 新規ストローク（`Stroke`）を開始し、`DefaultDrawingAttributes` と初期 `StylusPoint`（筆圧付き）を設定。
  - WinTabペン移動時（`PenMove`）:
    - スクリーン座標を `PointFromScreen` でキャンバス内座標に変換し、リアルタイムにストロークへ `StylusPoint` を追加・再描画。
  - WinTabペン離脱時（`PenUp`）:
    - ストロークを確定し、キャンバスの `Strokes` に登録して `InkUndoRedoService` に履歴追加。
  - WinTab操作がない時は、通常のマウス描画および既存のスタイラス（Windows Ink）処理がそのまま動作。

### 4. UIステータス通知・ツールチップ連携
- [MODIFY] [`DetailEditorViewModel.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs)
  - `PenTabletStatusText` プロパティを追加（例: `筆圧感知: 有効 (WinTabデバイス検出: Wacom Intuos)`）。
  - WinTabデバイス検出時にステータスバー通知を発行。
- [MODIFY] [`MainWindow.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/MainWindow.xaml)
  - 筆圧感知ボタンの `ToolTip` に接続状態テキストをバインド。

### 5. 単体テストの追加
- [NEW] [`WinTabPressureHelperTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/Helpers/WinTabPressureHelperTests.cs)
  - 筆圧の最小・最大境界値、ゼロ筆圧、最大筆圧超えのクランプ動作を網羅テスト。
- [NEW] [`WinTabServiceTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/Interop/WinTabServiceTests.cs)
  - WinTab非存在環境での安全な初期化失敗（例外を出さないサイレントフォールバック）を検証。

---

## 検証計画

### 自動テスト
- `dotnet test`: 既存の全テストスイートおよび新規追加テストが 100% パスすることを確認。
- `dotnet build`: 警告・エラーなく正常にコンパイルが完了することを確認。

### 手動検証
- Wacomペンタブレット接続環境で、Wacom設定の「Windows Ink を使う」がOFFの状態でも筆圧に応じて線の太さが滑らかに変化することを確認。
- 通常のマウス操作で従来通り手書き描画ができることを確認。
- 筆圧ボタンのツールチップにタブレット接続状態が表示されることを確認。
- ペンタブレットが接続されていない環境でアプリが正常起動し、何のエラーも発生しないことを確認。
