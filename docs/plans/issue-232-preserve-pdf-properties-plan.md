# Implementation Plan - Issue #232: PDF保存時におけるレイヤー設定(/OCProperties)・文書情報・しおり等の属性保持

本ドキュメントは、PDF保存処理（`PdfService.BuildAndSavePdf`）において、元PDFのカタログ（`/Catalog`）および文書情報（`/Info`）に定義されている各種属性（レイヤー設定、しおり、表示設定、メタデータ等）が消失する問題を解決し、単一ファイル編集時・複数ファイル結合時の双方で適切な保持・マージを行うための実装計画です。

---

## 1. 概要・改修目的

PDF Binder では、バインダーとしての複数ファイル結合やページ並び替え・削除を実現するため、空の `new PdfDocument()` を作成して `outputDoc.AddPage` でページを再構築して保存しています。
しかし、この処理ではドキュメント全体（カタログ・Info）の属性がコピーされないため、以下の深刻な問題が発生しています。

1. **非表示レイヤーの可視化によるレイアウト崩れ（最重要課題）**:
   - カタログの `/OCProperties`（特に `/OFF` リスト）が欠落することで、PDF標準仕様（ISO 32000-1）に基づき非表示レイヤーがデフォルト表示（ON）となり、隠されていた下書きや旧版テキストが前面に重複描画される。
2. **閲覧・ナビゲーション機能の喪失**:
   - 目次・しおり（`/Outlines`）の完全消滅。
   - 右開き・見開き設定（`/ViewerPreferences`）や初期表示モード（`/PageLayout`, `/PageMode`）のリセット。
3. **文書メタデータの消失**:
   - タイトル・作成者・作成日時などの文書情報（`/Info`）や言語指定（`/Lang`）の上書き・消滅。

本改修では、これらの情報を「ページ単位・依存情報」と「文書全体情報」に分類し、単一ファイル保存時および複数ファイル結合時の要件に従って安全に引き継ぎ・マージする仕組みを実装します。

---

## 2. 保持ルールと設計方針

### 2.1 保持ルールの整理

| 属性・機能 | 単一ファイル保存時<br>（1ファイルのみ開いている場合） | 複数ファイル結合保存時<br>（別ファイル追加・差し込み） | ページ分割・個別抽出 |
| :--- | :---: | :---: | :---: |
| **レイヤー設定 (`/OCProperties`)** | 元ドキュメントの設定を完全保持 | 含まれる全ドキュメントの OCG と `/OFF` リストをマージ | 抽出ページに関係する OCG 設定を保持 |
| **しおり・目次 (`/Outlines`)** | ページの並び替え・削除に追従させて再構築 | 各ドキュメントの目次を統合し、新ページ番号へ追従 | 抽出対象ページに該当するしおりのみ保持 |
| **初期表示 (`/ViewerPreferences`)** | 完全保持（右開き・見開き等を維持） | 先頭（第1）ファイルを引き継ぐ（閲覧性の維持） | 元ファイルを引き継ぐ |
| **表示形式 (`/PageLayout`, `/PageMode`)**| 完全保持 | 先頭ファイルを引き継ぐ | 元ファイルを引き継ぐ |
| **文書情報 (`/Info`)** | 完全保持（更新日時のみ更新） | 破棄（競合・矛盾回避） | 元ファイルを引き継ぐ |
| **言語指定 (`/Lang`)** | 完全保持 | 先頭ファイルを引き継ぐ | 元ファイルを引き継ぐ |
| **フォーム・構造 (`/AcroForm`, `/StructTreeRoot`)** | 保持 | 破棄（ID競合・ツリー破損回避） | 破棄 |

---

## 3. 実装アプローチ

### 3.1 クラス・モジュール構成
- **新規ヘルパークラス**: `PDFBinder.Core.Helpers.PdfMetadataHelper`
  - `TransferMetadata(PdfDocument outputDoc, IReadOnlyList<PdfPageModel> pages, Dictionary<string, PdfDocument> sourceCache)`:
    - 保存対象の全ページから、参照されている元ドキュメント（ソース）の一覧を特定。
    - 単一ソースか複数ソースかを判定。
    - 以下の個別移送メソッドを呼び出す。
  - `TransferDocumentInfo(...)`:
    - 単一ソース時: `outputDoc.Info` へ Title, Author, Subject, Keywords, Creator 等をコピーし、`CreationDate` を維持して `ModificationDate` を現在日時に更新。
  - `TransferLayerProperties(...)`:
    - 元ドキュメントの `/Catalog` から `/OCProperties` を抽出。
    - 単一ソース時: `/OCProperties` の辞書ツリーを `outputDoc.Internals.Catalog.Elements` へ安全にディープコピー。
    - 複数ソース時: 各ソースドキュメントの `/OCProperties` から `/OCGs`（レイヤー定義配列）および `/D` の `/OFF`（非表示レイヤー配列）、`/Order` をマージして新規 `/OCProperties` 辞書を構築。
  - `TransferViewerPreferences(...)`:
    - 単一ソース時および複数ソース時（先頭ドキュメント）の `/ViewerPreferences`, `/PageLayout`, `/PageMode`, `/Lang` をコピー。
  - `TransferOutlines(...)`:
    - 各元ドキュメントの `sourceDoc.Outlines` を走査。
    - しおりのジャンプ先（Destination）が指す元ページ番号を特定。
    - その元ページが `outputDoc` のどのページ位置（新ページインデックス）に配置されているかをマッピング。
    - 削除されたページを指すしおりは安全に除外、または親ノードを維持。
    - `outputDoc.Outlines` へ階層構造を保ったまま項目を再構築。

### 3.2 `PdfService.cs` の改修
- `BuildAndSavePdf` メソッド内において、全ページの `AppendPageToDocument` 完了後、`outputDoc.Save()` を呼び出す直前に `PdfMetadataHelper.TransferMetadata` を実行。
- 最小差分原則（Minimal Diff）に基づき、既存のページ追加ロジックやインク注釈描画には不要な変更を加えず、メタデータ適用処理のみを統合。

---

## 4. 検証・テスト計画

### 4.1 単体テスト（Unit Tests）の追加
`tests/PDFBinder.Tests/Services/PdfMetadataHelperTests.cs`（新規テストクラス）:
1. **レイヤー設定引き継ぎテスト（単一）**:
   - `/OCProperties`（`/OFF` に特定レイヤーを含む）PDFを読み込んで保存した際、出力PDFのカタログに `/OCProperties` が存在し、同一の `/OFF` が維持されていること。
2. **レイヤー設定マージテスト（複数結合）**:
   - ファイルA（レイヤー1がOFF）とファイルB（レイヤー2がOFF）を結合保存した際、出力PDFで両方のレイヤーがOFFとして登録されていること。
3. **文書情報（Info）引き継ぎテスト**:
   - 単一ファイル保存時: Author, Title 等が完全に引き継がれ、ModDateが更新されていること。
   - 複数ファイル結合時: Author, Title 等がクリアされ、不要な引き継ぎがないこと。
4. **表示設定（ViewerPreferences）引き継ぎテスト**:
   - `/Direction /R2L`（右開き設定）を持つPDFを保存した際、出力PDFでも `/Direction /R2L` が維持されていること。
5. **しおり（Outlines）追従・再構築テスト**:
   - 3ページのしおりを持つPDFで、ページ並び替え（1, 2, 3 → 3, 2, 1）やページ削除を行った際、しおりのジャンプ先が正しく追従・更新されていること。

### 4.2 実ファイル（`保存前.pdf`）での検証
- `保存前.pdf` を読み込み、未編集のまま保存した一時ファイルを出力。
- 生成されたPDFの1ページ目を PDFium でレンダリングし、`保存前.pdf` の1ページ目画像との差分（diff bbox）が完全にゼロになることを確認。
