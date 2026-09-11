# PDF Binder 基本設計書 (Basic Design)

本書は、Windowsデスクトップ向けPDF編集・バインダー管理・手書きアノテーションアプリケーション「PDF Binder」のシステム構成、アーキテクチャ、機能仕様、画面設計、およびデータ構造を定義する正本ドキュメントです。

---

## 1. システム概要と基本方針

### 1.1 アプリケーションの目的
「PDF Binder」は、複数のPDFファイルの結合・分割・ページの並び替え・回転・白紙追加といった「バインダーとしての文書整理」と、タッチペンやマウスによる「直感的な手書きアノテーション（ペン・蛍光ペン・直線・消しゴム）」を高次元で統合したWindows向けデスクトップアプリケーションです。

### 1.2 コア設計原則
1. **ポータブル＆完全オフライン**: 外部ネットワーク接続を一切必要とせず、ランタイム同梱の自己完結版（Self-Contained / 約66MB）およびOSの.NET 10を活用した軽量・高速起動版（Framework-Dependent / 約8.4MB）の単一exe発行に対応すること。
2. **非破壊編集＆安全なファイル管理**: 読み込み元PDFを排他ロックせず、メモリ/一時ストレージを活用して安全な「上書き保存」「別名で保存」を実現すること。
3. **オープンソース（OSS）ライセンス適合**: 商用利用制限や強力なコピーレフト（AGPL等）のない寛容型ライセンス（MIT / Apache-2.0 / BSD）のコンポーネントのみで構成すること。
4. **高品位アノテーション**: 手書きストロークはベクター情報および高解像度透過レンダリングのハイブリッドにより、印刷・拡大時にもボケや劣化のないPDF出力を行うこと。

---

## 2. 技術スタック・ライブラリ選定

| カテゴリ | 採用技術 / ライブラリ | ライセンス | 用途・選定理由 |
| :--- | :--- | :--- | :--- |
| **プラットフォーム** | .NET 10.0 (`net10.0-windows`) | MIT | 最新の高DPI対応、高速なJIT/GC、WPFの性能改善を活用 |
| **UIフレームワーク** | WPF (Windows Presentation Foundation) | MIT | 成熟した `InkCanvas` による手書き・筆圧・消しゴム機能、強力なドラッグ＆ドロップ |
| **デザイン/スタイル** | WPF-UI または モダンFluentスタイル | MIT | Windows 11準拠の洗練された角丸・アクリル外観の実現 |
| **MVVM基盤** | CommunityToolkit.Mvvm | MIT | 軽量・高パフォーマンスな `ObservableObject`, `RelayCommand` |
| **PDF構造操作** | PdfSharp (v6.x+) | MIT | ページの回転、並び替え、削除、結合、分割、空白ページ追加、PDF保存 |
| **PDFレンダリング** | PDFium / PDFiumSharp | Apache-2.0 / BSD | Chromiumで実証済みの高速・高精細なサムネイルおよび画面描画 |
| **単体テスト** | xUnit, FluentAssertions | Apache-2.0 / MIT | ロジックの網羅的自動テスト |

---

## 3. ソリューションおよびプロジェクト構成

```text
PDFBinder/
├── .gitignore
├── GEMINI.md                     # プロジェクト開発規約・運用制約（厳守事項）
├── icon.png                      # 正本アプリアイコン
├── README.md                     # プロジェクト概要・ビルド手順
├── PDFBinder.sln                 # ソリューション定義
├── docs/
│   ├── basic_design.md           # 本書（基本設計書）
│   ├── PROJECT.md                # 機能インベントリ・進捗管理
│   └── plans/                    # 実装計画およびWalkthroughの永続アーカイブ
├── src/
│   ├── PDFBinder.Core/           # UI非依存のドメイン・PDF操作・レンダリング層
│   │   ├── Models/               # PDFドキュメント、ページ、インクストロークモデル
│   │   ├── Services/             # PdfService, RenderingService, UndoService
│   │   └── Common/               # ユーティリティ、例外定義
│   └── PDFBinder.App/            # WPF UI層（Views, ViewModels, Behaviors, Styles）
│       ├── Views/                # MainWindow, GridView, DetailEditorView
│       ├── ViewModels/           # MainViewModel, GridViewModel, DetailEditorViewModel
│       ├── Controls/             # CustomInkCanvas, ThumbnailCard
│       ├── Converters/           # 各種ValueConverter
│       └── Assets/               # アイコンリソース
└── tests/
    └── PDFBinder.Tests/          # 単体テストプロジェクト（xUnit）
```

---

## 4. データモデル設計

### 4.1 `PdfPageModel`
バインダー内の個別ページを表現するモデル。
- `PageId`: `Guid` (一意なページID)
- `SourceFilePath`: `string?` (元ファイルパス、空白ページの場合はnull)
- `OriginalPageIndex`: `int` (元ファイル内でのインデックス)
- `OriginalRotation`: `PageRotation` (元PDFファイル読み込み時の初期回転角度)
- `Rotation`: `PageRotation` (現在の回転角度: 0°, 90°, 180°, 270°)
- `RenderRotation`: `PageRotation` (PDFiumレンダリング時に追加適用する差分回転角度)
- `Width`: `double` (ポイント単位)
- `Height`: `double` (ポイント単位)
- `DisplayWidth`: `double` (回転考慮後の表示上の幅)
- `DisplayHeight`: `double` (回転考慮後の表示上の高さ)
- `Thumbnail`: `BitmapSource?` (画面プレビュー用キャッシュビットマップ)
- `InkStrokes`: `StrokeCollection` (WPFインクストロークコレクション)
- `IsModified`: `bool` (編集フラグ)

### 4.2 `PdfDocumentModel`
現在作業中のバインダー全体を表現するモデル。
- `FilePath`: `string?` (現在開いているファイルのパス)
- `Pages`: `ObservableCollection<PdfPageModel>` (ページ順序リスト)
- `IsDirty`: `bool` (未保存変更の有無)
- `CanUndo` / `CanRedo`: `bool` (アンドゥ・リドゥ可能状態)

---

## 5. 主要サービスインターフェース設計

### 5.1 `IPdfService`
PDFファイルの入出力、構造操作を担当。
- `Task<PdfDocumentModel> LoadDocumentAsync(string filePath)`: ファイルロックを行わずメモリ読み込み
- `Task SaveDocumentAsync(PdfDocumentModel doc, string outputPath)`: ページ配置・回転・インク合成を行い保存
- `PdfPageModel CreateBlankPage(double width, double height)`: 指定サイズの白紙ページ生成
- `Task AppendPdfAsync(PdfDocumentModel doc, string filePath, int insertIndex)`: 別PDFのページ差し込み結合
- `Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputPath)`: 選択ページの分割抽出

### 5.2 `IPdfRenderer`
PDFページの画面表示用ビットマップ生成およびストローク合成を担当。
- `Task<BitmapSource?> RenderPageAsync(string? filePath, int pageIndex, int targetWidth, int targetHeight, PageRotation rotation)`: サムネイル/詳細画面用レンダリング（サムネイル: 360x504px基準固定生成、詳細画面: 216 DPI相当 / 3.0倍スケール）
- `BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)`: 白紙レンダリング
- `BitmapSource CompositeStrokes(BitmapSource baseImage, StrokeCollection strokes, double originalPageWidth, double originalPageHeight)`: 手書きストローク（InkStrokes）の縮小合成描画（グリッド一覧反映用）

### 5.3 `IUndoRedoService`
ページ操作およびインク操作の履歴管理。
- `Execute(IUndoableCommand command)`
- `Undo()`, `Redo()`
- `Clear()`

---

## 6. 画面設計とUI/UXフロー

### 6.1 画面モード構成
アプリは **「詳細ビュー（縦連続表示）」** を基本（デフォルト）画面とし、ページ一覧の確認や自由な並び替えを行う時のみ **「グリッド俯瞰ビュー（Binder Overview）」** へ切り替えて使用します。

```mermaid
stateDiagram-v2
    [*] --> 詳細ビュー: 起動 / PDF読み込み（縦連続表示）
    詳細ビュー --> グリッド俯瞰ビュー: 「表示」タブの「グリッド」選択（オンデマンドでサムネイル生成）
    グリッド俯瞰ビュー --> 詳細ビュー: 「表示」タブの「詳細」選択 / ページカードをダブルクリック
    詳細ビュー --> 外部保存: 保存 / 分割エクスポート
```

### 6.2 統合タイトルバー＆メインツールバー（モダンリボンスタイル）
- **統合タイトルバー兼タブバー（Chrome / Edge スタイル）**:
  - Windows標準のタイトルバーを廃止し、最上段（高さ38px）にアプリアイコン、タブバー、ファイル名表示、ウィンドウキャプションボタンを1段に統合。
  - **左端**: 正本アプリアイコン（18x18px、ドラッグ可能）。
  - **タブ配置**: 「PDF編集」「手書き」「表示」タブ。選択中のタブは下部の白ツールバー領域とシームレスに結合。
  - **中央部**: 現在開いているファイル名（未読み込み時は非表示）を薄いグレーで表示。この領域を含む余白全体がウィンドウドラッグ移動およびダブルクリック最大化/復元に対応。
  - **右端**: Windows標準スタイルのキャプションボタン（最小化、最大化/復元、閉じる[ホバーで赤ハイライト]）。
  - **ウィンドウ制御**: `WindowChrome` を使用し、Windows 11 Snap Layouts や境界リサイズ、最大化時のパディング自動補正に対応。
- **リボンタブ構成**:
  - **「PDF編集」タブ**: ファイル操作およびドキュメント構成の編集（常時利用可能）
    - ファイル操作: 開く (`Ctrl+O`), 追加, 保存 (`Ctrl+S`), 別名保存 (`Ctrl+Shift+S`)
    - ページ構成・抽出: 白紙追加 (`Ctrl+B`), 選択抽出, 全分割
    - ページ編集: 左回転 (`Ctrl+L`), 右回転 (`Ctrl+R`), 削除 (`Delete` / ホバー時赤ハイライト)
      - ※詳細ビュー表示時、特定ページが明示選択されていない場合は現在操作・表示中のカレントページを対象として回転・削除・白紙挿入が実行される。
    - 履歴操作: 元に戻す (`Ctrl+Z`), やり直す (`Ctrl+Y`)
  - **「手書き」タブ**: 詳細ビュー表示時のみ選択可能（グリッドビュー時は非活性化、グリッド切替時は他タブへ自動遷移）
    - ツール: 選択 (`Cursor`), ペン (`Pen`), 蛍光ペン (`Highlighter`), 全体消し (`EraserStroke`), 部分消し (`EraserPoint`), 移動 (`Pan`)
    - 描画オプション（直線・太さ・色）:
      - 直線トグル: ペンまたは蛍光ペン使用時のみ有効化可能なトグルボタン（ON時は十字カーソルで直線描画、ツール切り替え時に自動リセット）
      - 太さプリセット: 選択中ツールに応じた4種ドット（●）ボタン。選択中の太さは薄青背景（`#DBEAFE`）と青枠線（`#2563EB`）で強調表示。ペン時は `0.5px`, `1.0px`, `2.0px`, `4.0px`（初期値 `1.0px`）、蛍光ペンおよび部分消しゴム時は `8.0px`, `12.0px`, `16.0px`, `24.0px`（初期値 `12.0px`、部分消しゴムは円形 `EllipseStylusShape`）。ペン・蛍光ペン・部分消しゴム選択時のみ有効。
      - カラーパレット: 5色プリセット（黒 `#000000`、鮮やかな赤 `#EF4444`、鮮やかな青 `#2563EB`、鮮やかな緑 `#16A34A`、鮮やかな黄 `#EAB308`）。選択中の色にチェックマーク（✓）を表示。ペンおよび蛍光ペン選択時のみ有効。
      - ツール状態の独立保持: ペン（色・太さ）、蛍光ペン（色・太さ）、部分消しゴム（太さ）を切り替えても直前の各設定が独立して記憶・保持される。
  - **「表示」タブ（新設）**: ビュー切り替えおよびズーム操作の一本化
    - ビュー切り替え: 「詳細」ラジオボタン（縦連続詳細表示）、「グリッド」ラジオボタン（サムネイル一覧表示）
    - ズーム操作: 縮小 (`Ctrl+-`), 倍率表示/リセット (`Ctrl+0`), 拡大 (`Ctrl++`)
      - 詳細ビュー時: ページ表示倍率（50%〜300%、初期値100%）を操作、クリックで等倍（100%）にリセット
      - グリッドビュー時: サムネイルサイズ（140px〜360px、初期値220px）を操作、初期値220pxを100%とした比率表示およびリセット

### 6.3 グリッド俯瞰ビュー
- **サムネイルグリッド**:
  - 表示タブで「グリッド」が選択された時に初めてオンデマンドで未生成サムネイルを非同期生成（読み込み時の高速化）
  - ドラッグ＆ドロップによる自由なページ順序入れ替え（ドロップインジケーター表示）
  - 複数選択（Shift+クリック、Ctrl+クリック）対応
  - 各カード上に「ページ番号」「回転ボタン」「削除ボタン」のクイックオーバーレイ
  - 外部PDFファイルのドラッグ＆ドロップによる直接差し込み結合
  - カードダブルクリックにより詳細ビューへ瞬時に切り替わり、該当ページへ自動スクロール

### 6.4 詳細手書きエディタビュー（縦連続スクロール表示）
- **画面構成**:
  - 全ページが縦一列に並ぶ連続スクロールリスト（`ScrollViewer` ＋ `ItemsControl`）
  - 各用紙カードはドロップシャドウ付きの用紙境界を持ち、用紙外の余白を保ったすっきりとしたドキュメントリーダー外観
  - ステータスバーに現在中央に表示・操作中のページ番号（例:「ページ 3 / 10」）を動的表示
- **キャンバスエリア**:
  - 各ページごとに背景PDFレンダリング画像と前面 `EditorInkCanvas` を配置
  - スケール連動（`LayoutTransform` / `ScaleTransform`）により、全ページ一括での滑らかな拡大・縮小
  - デバウンス制御（150ms）、ダブルバッファリング、世代番号管理により、ズーム時のチラつき（フリッカー）や非同期競合を完全に防止
  - **パームリジェクション & マルチタッチジェスチャー**:
    - スタイラスペン（デジタイザー）と手指タッチの完全分離
    - スタイラスペン使用時: ツールに応じた常時描画・消去
    - 手指1本タッチ: 画面のスムーズなパン（スクロール移動）
    - 手指2本タッチ: 指の中心座標を基準としたピンチズーム（拡大・縮小）＆スクロール連動
    - パームリジェクション: ペン接地中および近接（ホバー）中はタッチ操作を一時抑止し、筆記時の手首接触による画面揺れを防止
    - マウス操作: ツールバーの「移動」選択でドラッグスクロール、ペン選択で描画、ホイールでスクロールを維持

---

## 7. 手書きアノテーションのPDF合成・保存仕様

PDFへのインク反映は、画質とファイルサイズ、PDF標準互換性を両立するハイブリッド方式を採用します。
1. **ベクター出力（ペン・直線）**:
   - `PdfSharp` の `XGraphics.DrawLines` / `DrawPath` を用い、ストロークの座標点配列をベクターパスとして直接PDFのコンテンツストリームに書き込みます。
   - これにより、拡大・印刷時にも劣化せず、極小ファイルサイズを維持します。
2. **半透明ハイライト出力（蛍光ペン）**:
   - 蛍光ペンの透過ブレンド（Multiply / Alpha Transparency）はPDFリーダー依存の表示崩れを防ぐため、ハイライトストローク領域を高DPI透過PNGとしてレンダリングし、`XGraphics.DrawImage` でページの上に重ねて合成します。
3. **元ドキュメントの非破壊保持**:
   - 元のテキストやベクター図面は一切再エンコード・不可逆圧縮せず、新規コンテンツレイヤーとしてインクを上書き追記します。

---

## 8. 非機能要件・品質基準

1. **応答性**:
   - 100ページ以上のPDFでもUIフリーズなくスムーズにスクロールできる仮想化（`VirtualizingWrapPanel`）の採用。
   - サムネイル生成はバックグラウンドワーカースレッド（`Task.Run`）で非同期生成し、順次UIへ反映。
2. **堅牢性**:
   - 不正なPDF、破損ページ、パスワード保護PDFに対する適切なエラーハンドリングとダイアログ通知。
3. **テスト自動化**:
   - `PDFBinder.Core` 内のページ操作、マージ、スプリット、白紙挿入の単体テスト網羅率100%（全テストPASS必須）。
