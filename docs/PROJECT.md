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
| **F04** | プロジェクト基盤 | 単一EXE発行環境（Self-Contained / `build.ps1`） | **完了** | `dist/PDFBinder.exe` (約66MB) |
| **F10** | PDF操作コア | 非破壊ドキュメント読み込み（メモリ展開/ファイルロック回避） | **完了** | PdfService (PdfSharp) |
| **F11** | PDF操作コア | ページ回転（時計回り90°、反時計回り90°、180°） | **完了** | PdfPageModel / PageRotation |
| **F12** | PDF操作コア | ページ削除 | **完了** | PdfDocumentModel.RemovePage |
| **F13** | PDF操作コア | ページ順序入れ替え（Reordering） | **完了** | MovePageCommand / PdfDocumentModel |
| **F14** | PDF操作コア | 空白ページの追加（隣接サイズ継承 / A4標準） | **完了** | PdfService.CreateBlankPage |
| **F15** | PDF操作コア | 外部PDFの結合（Append / 任意位置挿入） | **完了** | PdfService.AppendDocumentAsync |
| **F16** | PDF操作コア | PDF分割（選択ページ抽出 / 個別1ページ分割） | **完了** | PdfService.ExportPagesAsync / SplitAll |
| **F17** | PDF操作コア | ドキュメント保存（上書き保存 / 別名で保存） | **完了** | SafeReplaceFile アトミック保存 |
| **F20** | レンダリング | PDFiumによる高精細サムネイル生成（最大360px・高DPI対応） | **完了** | PdfiumRenderer (Docnet.Core) |
| **F21** | レンダリング | 手書きストローク（InkStrokes）のサムネイル縮小合成反映 | **完了** | Issue #21 / CompositeStrokes |
| **F30** | UI・グリッド俯瞰 | ページタイル状グリッド一覧表示（仮想化・カードUI） | **完了** | GridView.xaml |
| **F31** | UI・グリッド俯瞰 | ドラッグ＆ドロップによるページ並び替え | **完了** | DragDrop.DoDragDrop / MovePage |
| **F32** | UI・グリッド俯瞰 | 外部PDFのグリッドへのD&D差し込み | **完了** | OnControlDrop / FileDrop |
| **F33** | UI・グリッド俯瞰 | 複数選択・一括操作（回転・削除・分割） | **完了** | CheckBox / Batch commands |
| **F34** | UI・グリッド俯瞰 | Undo / Redo（元に戻す・やり直す） | **完了** | UndoRedoService / Ctrl+Z, Ctrl+Y |
| **F35** | UI・グリッド俯瞰 | メインヘッダーツールバー刷新（リボンタブ化・手書きツール統合・太さ刷新） | **完了** | Issue #3, #7, #15, #17 |
| **F40** | 手書き詳細 | ページダブルクリックでの詳細エディタ切り替え（216 DPI高解像度化） | **完了** | DetailEditorView.xaml / Issue #21 |
| **F41** | 手書き詳細 | ペンツール（色・太さ変更） | **完了** | EditorInkCanvas / ToolMode |
| **F42** | 手書き詳細 | 蛍光ペンツール（半透明描画・色変更） | **完了** | IsHighlighter = true |
| **F43** | 手書き詳細 | 消しゴムツール（ストローク消し / 部分消し切替） | **完了** | EraseByStroke / EraseByPoint |
| **F44** | 手書き詳細 | 直線描画ツール（プレビュー付き） | **完了** | CommitStraightLine |
| **F36** | UI・タイトルバー | タイトルバー廃止とタブバー統合（Chrome/Edgeスタイル・WindowChrome） | **完了** | Issue #28 / WindowChrome |
| **F45** | 手書き詳細 | ズーム（拡大縮小）＆パン（手のひらツール） | **完了** | ScaleTransform & Hand tool |
| **F46** | 手書き詳細 | 手書きストロークのベクター/透過PNGハイブリッド保存 | **完了** | DrawInkStrokesOnPage (PdfSharp) |
| **F47** | 手書き詳細 | パームリジェクション & タッチ操作（1本指パン・2本指ピンチズーム） | **完了** | Issue #9 / EditorInkCanvas & PinchZoomHelper |
| **F22** | レンダリング | スキャンPDF対応（差分回転レンダリング・Docnet正規化・手書き座標逆変換） | **完了** | Issue #34 |
| **F23** | レンダリング | 詳細ビューのズーム連動動的レンダリング（デバウンス・高品質補間・縮小細線保護） | **完了** | Issue #24 |
| **F50** | テスト・品質 | コアロジックの単体テスト自動化（xUnit 71件全PASS） | **完了** | PDFBinder.Tests |

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
