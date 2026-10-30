# 検証報告: 詳細エディタの余白エリアにおけるタッチスクロール・ピンチズーム対応

- **Issue**: [#197 手によるスクロールを周りの余白エリアでもできるようにする](https://github.com/shota4327/PDFBinder/issues/197)
- **検証日**: 2026-10-30
- **ブランチ**: `issue-197-touch-scroll-margin`

---

## 1. 実装内容の概要
詳細手書きエディタ（`DetailEditorView`）において、PDFページ上だけでなく、ページの周囲にある余白エリアやページ間の余白を指でタッチ・操作しても、滑らかなスクロール（1本指パン）および拡大・縮小（2本指ピンチズーム）が行えるように機能を拡張しました。

### 主な改修点
1. **`DetailTouchTracker` の新設 (`src/PDFBinder.App/Helpers/DetailTouchTracker.cs`)**:
   - タッチデバイスごとの座標追跡、タッチスロップ判定（閾値8pxによる微小なブレ・揺れ防止）、1本指パン変位計算、2本指ピンチズーム（`PinchZoomHelper.Calculate` 連携）、およびスタイラスペン検知時のパームリジェクション判定を一元管理するコントローラークラスを新設。
2. **`DetailEditorView` へのタッチ・スタイラスイベント統合 (`src/PDFBinder.App/Views/DetailEditorView.xaml.cs`)**:
   - ページ全体および周囲余白を含む親コンテナ `DetailScrollViewer` レベルで `PreviewTouchDown` / `PreviewTouchMove` / `PreviewTouchUp` を購読・捕捉。
   - 余白エリアのみならず、片方の指がページ内・もう片方が余白にある場合でも、両指の距離変化からシームレスにピンチズームが追従。
   - スタイラスペンでの筆記時（`PreviewStylusDown`、近接ホバー中、離脱後150ms以内）は、手のひらが余白エリアに接地してもタッチ操作を一括抑止（パームリジェクション）。
3. **ドキュメント・バージョンの同期**:
   - `Directory.Build.props`: 新機能追加に伴い `0.15.0` から `0.16.0` へマイナーインクリメント。
   - `CHANGELOG.md`: エンドユーザー向けリリースノートの確定・追記。
   - `docs/basic_design.md` / `README.md` / `docs/PROJECT.md`: 余白エリア対応の記述を同期。

---

## 2. 検証結果

### 2.1 単体テスト実行結果 (`dotnet test`)
- コマンド: `dotnet test`
- 結果: **全 694 件 PASS（成功: 694、失敗: 0、スキップ: 0）**
- 新規追加テスト (`tests/PDFBinder.Tests/DetailTouchTrackerTests.cs`):
  - `OneFingerPan_SuppressesMicroMovement_UntilTouchSlopExceeded`: スロップ閾値（8px）未満の微小移動抑止および閾値超過後の正確なパン変位計算を検証 -> **PASS**
  - `TwoFingerPinchZoom_CalculatesNewZoomAndOffset`: 2本指によるピンチ操作で新しい拡大率および幾何学的中心追従スクロール位置が算出されることを検証 -> **PASS**
  - `CrossBoundaryPinch_OperatesSeamlesslyAcrossPageAndMargin`: 片指が余白・片指がページ内にある境界跨ぎピンチズームの正常動作を検証 -> **PASS**
  - `TouchUp_ClearsActiveStateAndResetsPinch`: タッチ離脱時のアクティブ状態解除およびピンチ状態リセットを検証 -> **PASS**
  - `StylusSuppression_RejectsTouchesDuringStylusActivity`: スタイラスペン接地中・ホバー中のタッチ拒否（パームリジェクション）を検証 -> **PASS**
  - `StylusActivity_PurgesExistingActiveTouches`: スタイラス検知時の既存タッチ即時パージを検証 -> **PASS**
  - `StylusSuppression_CooldownPeriodWorks`: スタイラス離脱後150ms以内の抑止継続とクールダウン経過後の受付再開を検証 -> **PASS**
  - `DetailEditorView_InitializesTouchTracker`: `DetailEditorView` におけるトラッカー初期化のSTA検証 -> **PASS**

### 2.2 ビルド検証結果 (`dotnet build`)
- コマンド: `dotnet build`
- 結果: **警告 0、エラー 0 でビルド成功**
