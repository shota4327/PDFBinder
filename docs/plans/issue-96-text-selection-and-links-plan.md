# Issue #96: テキスト選択・コピーおよびリンク（URL/ページジャンプ）機能 実装計画

## 1. 概要
本機能は、PDF内の文字をマウスドラッグで選択・コピー（`Ctrl+C` および右クリック「コピー」）可能にし、さらにPDF内のURLリンク（ブラウザ起動）およびページ内ジャンプリンク（該当ページスクロール）を可能にするインタラクティブ・オーバーレイ機能の実装計画です。

詳細エディタ（`DetailEditorView`）で既に稼働している高解像度動的レンダリング（PDFium）の前面に、文字選択・リンク処理を担うインタラクティブ層を新設します。

---

## 2. 確定したアーキテクチャおよび仕様
- **レンダリング＆オーバーレイ構造**:
  - `PageBackground`（動的高精細ビットマップ画像）
  - `InteractiveOverlayCanvas`（文字ハイライト描画、URL・ページジャンプリンクのクリック領域）
  - `StrokeCache`（確定済み手書きインク画像）
  - `EditorInkCanvas`（手書き入力中の一画 / 選択枠）
- **文字選択・コピー機能**:
  - ツールバーに「テキスト選択ツール（`EditorToolMode.TextSelect`）」を新設。
  - マウスドラッグによる文字の範囲選択および半透明ブルー（`#4D0078D7`）ハイライト描画。
  - `Ctrl+C` キーボードショートカットおよび右クリックコンテキストメニュー「コピー」によるクリップボード格納。
  - ※操作スコープはページ単位（Ctrl+A全選択は含めない）。
- **リンククリック機能**:
  - 「テキスト選択ツール」または「手のひらツール（Hand）」選択時に通常クリックでリンクを開く。
  - ペン・消しゴム等の手書きモード時は手書き入力を最優先し、リンクは無反応とする（誤タッチ防止）。
  - URLリンククリック時は確認ダイアログなしで直接既定のブラウザで起動。
  - ページ内リンククリック時は該当ページへスクロール・ジャンプ。
  - リンク領域にマウスホバー時は指カーソル（`Cursors.Hand`）およびツールチップ（URLやジャンプ先）を表示。
- **抽出エンジン**:
  - テキスト・文字座標抽出: `Docnet.Core` の `IPageReader.GetCharacters()` を活用。
  - リンク注釈（URI / GoTo）抽出: `PdfSharp` の `PdfPage.Annotations` を活用。

---

## 3. 変更対象コンポーネントおよびファイル構成

### [NEW] コントロールおよびモデル
1. `src/PDFBinder.Core/Models/PdfTextCharacter.cs`
   - 抽出文字（`char`）、バウンディングボックス（`Rect`）、インデックス等を保持するデータモデル。
2. `src/PDFBinder.Core/Models/PdfLinkAnnotation.cs`
   - リンク矩形領域（`Rect`）、リンク種別（URL / ページジャンプ）、宛先（URL文字列 / ページ番号）を保持するデータモデル。
3. `src/PDFBinder.Core/Models/PageInteractiveData.cs`
   - 1ページ分のテキストデータ（文字リスト・行リスト）およびリンク注釈リストをまとめたコンテナ。
4. `src/PDFBinder.App/Controls/InteractiveOverlayCanvas.cs`
   - ページの文字選択、ハイライト描画、リンククリック領域の表示とイベント処理を行うカスタムWPFコントロール。

### [MODIFY] サービス層
1. `src/PDFBinder.Core/Services/IPdfRenderer.cs`
   - ページのインタラクティブ情報（テキストおよびリンク注釈）を非同期取得する `ExtractInteractiveDataAsync` メソッドを追加。
2. `src/PDFBinder.Core/Services/PdfiumRenderer.cs`
   - `ExtractInteractiveDataAsync` の実装（`Docnet.Core` による文字抽出、`PdfSharp` によるリンク注釈抽出、WPF座標・回転角変換）。

### [MODIFY] ViewModel および View 層
1. `src/PDFBinder.App/Controls/EditorInkCanvas.cs`
   - `EditorToolMode.TextSelect` の追加。
   - `TextSelect` モード時の `IsHitTestVisible` 制御。
2. `src/PDFBinder.App/ViewModels/DetailPageItemViewModel.cs`
   - `PageInteractiveData` の保持および遅延ロード。
   - 選択中テキスト状態（`SelectedText`, `HasSelectedText`）の保持。
   - リンククリック時のイベント/コマンドディスパッチ。
3. `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`
   - `SelectedTool` に `TextSelect` を追加。
   - ページジャンプリンク実行メソッド（`ScrollToPage`）の提供。
4. `src/PDFBinder.App/Views/DetailEditorView.xaml`
   - `DetailPageItemTemplate` 内に `InteractiveOverlayCanvas` を配置。
   - ツールモード連動によるヒットテスト制御。
5. `src/PDFBinder.App/MainWindow.xaml`
   - ツールバーに「テキスト選択（I-Beamアイコン）」ボタンを追加。

### [NEW] 単体テスト
1. `tests/PDFBinder.Tests/InteractiveDataExtractionTests.cs`
   - テキスト抽出およびリンク注釈抽出の正常系・回転系・境界値テスト。
2. `tests/PDFBinder.Tests/TextSelectionLogicTests.cs`
   - 座標選択範囲判定、複数行選択、文字列結合ロジックのテスト。

---

## 4. 検証計画
- `dotnet test` により全単体テストがパスすること。
- `dotnet build` によりエラーおよび警告がないこと。
- 実機手動テスト（PDFを開き、テキスト選択ツールで文字をドラッグ選択してCtrl+Cで貼り付け確認、URLリンククリックでブラウザ起動確認、ページジャンプリンクで該当ページ移動確認）。
