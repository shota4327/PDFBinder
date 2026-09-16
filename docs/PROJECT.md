# PDF Binder プロジェクト進捗・機能インベントリ

本ドキュメントは、PDF Binder の機能開発状況、タスクステータス、およびリリース準備状況を追跡・管理するための台帳です。

---

## 1. プロジェクト基本情報

- **リポジトリ**: `https://github.com/shota4327/PDFBinder.git`
- **対象プラットフォーム**: Windows (x64)
- **ターゲットフレームワーク**: .NET 10.0 (`net10.0-windows`) / WPF
- **ライセンス**: MIT License (オープンソース・非商用)

---

## 2. 機能インベントリ (Feature Inventory)

| ID | 機能分類 | 機能名 | 状態 | 備考 / 担当 |
| :--- | :--- | :--- | :--- | :--- |
| **F01** | プロジェクト基盤 | ソリューション初期化 & プロジェクト分割 (.App, .Core, .Tests) | **完了** | .NET 10.0 / WPF / xUnit |
| **F02** | プロジェクト基盤 | アプリアイコン設定 (`icon.png` / `.ico`) | **完了** | Assets/icon.ico & icon.png |
| **F03** | プロジェクト基盤 | CI・コーディング規約 (`GEMINI.md`) 整備 | **完了** | 初期コミット済 |
| **F04** | プロジェクト基盤 | 単一EXE発行環境（Self-Contained & Framework-Dependent / `build.ps1`） | **完了** | `dist/PDFBinder.exe` (フレームワーク依存版: 約8.4MB) & `dist/self-contained/PDFBinder.exe` (自己完結版: 約66MB) |
| **F10** | PDF操作コア | 非破壊ドキュメント読み込み（メモリ展開/ファイルロック回避） | **完了** | PdfService (PdfSharp) |
| **F11** | PDF操作コア | ページ回転（時計回り90°、反時計回り90°、180°） | **完了** | PdfPageModel / PageRotation |
| **F12** | PDF操作コア | ページ削除 | **完了** | PdfDocumentModel.RemovePage |
| **F13** | PDF操作コア | ページ順序入れ替え（Reordering） | **完了** | MovePageCommand / PdfDocumentModel |
| **F14** | PDF操作コア | 空白ページの追加（隣接サイズ継承 / A4標準） | **完了** | PdfService.CreateBlankPage |
| **F15** | PDF操作コア | 外部PDFの結合（Append / 任意位置挿入） | **完了** | PdfService.AppendDocumentAsync |
| **F16** | PDF操作コア | PDF分割（選択ページ抽出 / 個別1ページ分割） | **完了** | PdfService.ExportPagesAsync / SplitAll |
| **F17** | PDF操作コア | ドキュメント保存（上書き保存 / 別名で保存） | **完了** | SafeReplaceFile アトミック保存 |
| **F18** | PDF操作コア / UI | 未保存変更確認ダイアログ（終了時・別ファイルオープン時の保存確認、モダンインアプリオーバーレイ） | **完了** | Issue #49, #54 / インアプリオーバーレイ & OnClosing |
| **F19** | PDF操作コア / 起動 | 起動時コマンドライン引数処理（「プログラムから開く」・関連付け起動・複数ファイル個別プロセス起動） | **完了** | Issue #83 / CommandLineArgsHelper & App.OnStartup |
| **F20** | レンダリング | PDFiumによる高精細サムネイル生成（最大360px・高DPI対応） | **完了** | PdfiumRenderer (Docnet.Core) |
| **F21** | レンダリング | 手書きストローク（InkStrokes）のサムネイル縮小合成反映 | **完了** | Issue #21 / CompositeStrokes |
| **F30** | UI・グリッド俯瞰 | ページタイル状グリッド一覧表示（仮想化・カードUI） | **完了** | GridView.xaml |
| **F31** | UI・グリッド俯瞰 | ドラッグ＆ドロップによるページ並び替え | **完了** | DragDrop.DoDragDrop / MovePage |
| **F32** | UI・ファイル操作 | 外部PDFのドラッグ＆ドロップ共通化（詳細・グリッド両ビュー対応、共通ドロップ案内オーバーレイ表示） | **完了** | Issue #23 追加改修-2 / MainWindow |
| **F33** | UI・グリッド俯瞰 | 複数選択・一括操作（回転・削除・分割） | **完了** | CheckBox / Batch commands |
| **F34** | UI・グリッド俯瞰 | Undo / Redo（元に戻す・やり直す） | **完了** | UndoRedoService / Ctrl+Z, Ctrl+Y |
| **F35** | UI・グリッド俯瞰 | メインヘッダーツールバー刷新（リボンタブ化・手書きツール統合・太さ刷新） | **完了** | Issue #3, #7, #15, #17 |
| **F40** | 手書き詳細 | ページダブルクリックでの詳細エディタ切り替え（216 DPI高解像度化） | **完了** | DetailEditorView.xaml / Issue #21 |
| **F41** | 手書き詳細 | ペンツール（色・太さ変更・現在値強調・独立状態保持） | **完了** | Issue #26 / DetailEditorViewModel |
| **F42** | 手書き詳細 | 蛍光ペンツール（半透明描画・専用太さプリセット・現在値強調・独立状態保持） | **完了** | Issue #26 / DetailEditorViewModel |
| **F43** | 手書き詳細 | 消しゴムツール（ストローク消し / 部分消し切替・部分消し太さ変更・独立状態保持） | **完了** | Issue #26 / DetailEditorViewModel & EraserShape |
| **F44** | 手書き詳細 | 直線トグル描画（ペン・蛍光ペン連動トグル化・十字カーソルプレビュー） | **完了** | Issue #25 / IsStraightLine & CommitStraightLine |
| **F36** | UI・タイトルバー | タイトルバー廃止とタブバー統合（Chrome/Edgeスタイル・WindowChrome） | **完了** | Issue #28 / WindowChrome |
| **F45** | 手書き詳細 | ズーム（拡大縮小）＆パン（手のひらツール、50%〜3200%適応型スナップズーム、最大レンダリング解像度8192px拡張） | **完了** | Issue #60 / ScaleTransform & ZoomSnapSteps |
| **F46** | 手書き詳細 | 手書きストロークのベクター/透過PNGハイブリッド保存 | **完了** | DrawInkStrokesOnPage (PdfSharp) |
| **F47** | 手書き詳細 | パームリジェクション & タッチ操作（1本指パン・2本指ピンチズーム・自動スクロール抑止・タッチスロップ・タッチ描画遮断） | **完了** | Issue #9, #13, #41, #46 / EditorInkCanvas |
| **F22** | レンダリング | スキャンPDF対応（差分回転レンダリング・Docnet正規化・手書き座標逆変換） | **完了** | Issue #34 |
| **F23** | レンダリング | 詳細ビューのズーム連動動的レンダリング（デバウンス・高品質補間・縮小細線保護） | **完了** | Issue #24 |
| **F37** | UI・表示設定 | 表示オプション（100%、ウィンドウにあわせる、幅にあわせる、動的リサイズ追従、スクロールバー幅および余白補正） | **完了** | Issue #52, #65 / DetailViewFitMode |
| **F38** | UI・ステータスバー | 高機能ステータスバー刷新・半透明オーバーレイ化（半透明ダーク `#E61E293B`（不透明度90%）、高さ約5px拡大 MinHeight 42px、ボタン・入力欄拡大、単一・連続表示時のステータスバー重複回避・スクロールバー誤出現防止、ページ移動 `< ページ 1 / 2 >`、ズームコントロール `[-] 100% [+]`） | **完了** | Issue #52, #77, #91 / MainWindow & DetailEditor |
| **F39** | UI・表示設定 | 連続表示・単ページ表示切り替え機能（初期値: 単一ページ、ホイールページめくり、拡大率固定制御、PageUp/PageDown、高速ホイール追従・Delta累積） | **完了** | Issue #61, #73 / DetailPageViewMode |
| **F48** | UI・ショートカット | フォーカス非依存のショートカット保証および矢印キー（↑↓←→）ページ送り対応 | **完了** | Issue #78 / OnPreviewKeyDown & NoAutoScrollScrollViewer |
| **F49** | 手書き詳細 | 筆圧ON/OFFトグル（ペンツール連動・デフォルトOFF均一線・直線連動無効化・状態保持） | **完了** | Issue #88 / IsPenPressureEnabled & IgnorePressure |
| **F51** | UI・ウィンドウ管理 | 前回終了時のウィンドウサイズ・最大化状態復元（`settings.json`、画面作業領域自動調整） | **完了** | Issue #53 / SettingsService & WindowBoundsHelper |
| **F50** | テスト・品質 | コアロジックの単体テスト自動化（xUnit 229件全PASS） | **完了** | PDFBinder.Tests |
| **F52** | UI・デザイン | アイコンを Google Fonts (Material Symbols Outlined) に完全刷新・内包化（オフライン動作・Apache-2.0） | **完了** | Issue #62 / MainWindow, GridView, DetailEditorView |
| **F54** | 手書き詳細 / インタラクティブ | テキスト選択・コピーおよびリンク機能（ドラッグ選択、Ctrl+C、右クリックコピー、URLブラウザ起動、ページジャンプ、インタラクティブオーバーレイ） | **完了** | Issue #96 / InteractiveOverlayCanvas & PdfiumRenderer |
| **F53** | UI・デザイン | スレート／ネイビー系ダークテーマへの完全移行（常時ダーク・PDF原本用紙白地保持・カスタムスリムスクロールバー） | **完了** | Issue #91 / App.xaml, MainWindow, GridView, DetailEditorView |

---

## 3. マイルストーン計画

- **M1: 初期基盤＆PDFコア操作確立 (Issue #1)**
  - ソリューション構成の作成、NuGetパッケージ設定
  - `PDFBinder.Core` 実装（回転、並び替え、削除、結合、分割、白紙追加、保存）
  - xUnit 単体テスト全件作成＆パス
- **M2: グリッド俯瞰ビューの実装**
  - サムネイル生成、グリッド一覧、ドラッグ＆ドロップ並び替え、ツールバー連携
- **M3: 詳細手書きエディタの実装**
  - InkCanvas統合、ペン・蛍光ペン・消しゴム・直線ツール、ズーム・パン、PDF合成保存
- **M4: ポリッシュ＆OSSリリース準備**
  - Windows 11 Fluentスタイリング、ポータブル単一exe出力確認、ドキュメント整備
