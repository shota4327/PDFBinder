# 起動・表示パフォーマンス改善および不具合修正 検証報告書 (Walkthrough)

- **対象Issue**: #204
- **作業ブランチ**: `issue-204-startup-and-rendering-performance-fixes`
- **作成日**: 2026-10-06
- **ステータス**: 検証完了 (Verified)

---

## 1. 概要

Issue #204 において、PDF Binder のアプリケーション起動時間、PDFファイル読み込み直後の1ページ目表示速度、サムネイル生成速度のボトルネック解消、およびレンダリングやプロセス管理における不具合（B1, B2, B3）の修正を実施しました。

事前の詳細調査およびユーザーとのレビューに基づき、表示品質（文字や図面の美しさ）を一切損なうことなく、冗長なシリアライズ処理（`doc.Save`）の完全撤廃や、高DPIディスプレイ環境（4K等）での鮮明な描画を担保する設計で改修を行いました。

---

## 2. 実施した変更内容

### 2.1 パフォーマンス改善

1. **レンダラーでの白矩形挿入・再シリアライズの完全撤廃（P1, P2）**
   - **背景**: 従前は透過背景PDFへの白背景保証のために、レンダリング直前に全ページへ白矩形を挿入して `doc.Save` を実行（`SanitizeForRendering`）していました。これにより自前注釈のない通常の外部PDFでも1ページ目表示およびサムネイル生成のたびに巨大なシリアライズ処理が走り、深刻なラグが発生していました。
   - **対策**: UI側（`GridView.xaml` および `DetailEditorView.xaml`）の用紙枠（`PageBorder`）に白背景が既に設定されており、Docnet (PDFium) が透過出力したビットマップを WPF の DirectX レンダラーが美しくハードウェア合成するため、レンダラー内部での白矩形挿入は完全に不要でした。
   - **実装**:
     - `SanitizeForRendering` を撤廃し、通常の外部PDFはディスク上の生バイト列から直接 PDFium へ渡してレンダリングするよう改修。
     - 自前手書き注釈（`PdfBinderInkAnnotation`）が含まれるPDFのみ、初回レンダリング時に注釈を除去したバイト列を生成してメモリキャッシュ（`_fileBytesCache`、ファイル更新日時検証付き）に保持し、詳細画面での手書き線の多重焼き込みを防止。
     - `PdfDocumentModel.HasBinderInkAnnotations` および `PdfPageModel.HasSourceBinderInkAnnotation` フラグを導入し、読み込みループ内で O(1) 検知。

2. **重複ドキュメント初期化の削除（P5）**
   - `MainViewModel.cs` の `OnActiveSessionChanged` および `OnDocumentChanged` で実行されていた `InitializeDocument` の多重呼び出しを解消し、アクティブドキュメント切り替え時のサムネイル再走査・再生成の無駄な重複を防止。

3. **PDF読み込みループのバックグラウンドタスク委譲（P6）**
   - `PdfService.cs` の `LoadDocumentAsync` において、PDFSharp ドキュメント構造解析および手書き注釈復元ループを `Task.Run` に委譲し、数百ページ規模のファイルでもUIスレッドのプチフリーズ（ヒッチ）を完全に解消。

4. **単一EXE発行時の展開圧縮無効化（P7）**
   - `build.ps1` において、自己完結版単一EXEの発行オプションに `-p:EnableCompressionInSingleFile=false` を指定。初回起動時の解凍オーバーヘッドを削減し、ディスクからの即時実行を実現。

### 2.2 不具合修正

1. **高DPI環境でのレンダリング解像度計算の修正（B1）**
   - **問題**: 4Kディスプレイや拡大表示環境（DPIスケール 125%、150%等）において、詳細エディタで等倍・拡大表示した際に物理ピクセル解像度が不足し、文字や図面がぼやける不具合。
   - **修正**:
     - `DetailEditorViewModel` に `DpiScale` プロパティを追加し、`CalculateRenderDimensions` 内で `zoom * DpiScale` を有効スケールとして乗算するよう改修。
     - `DetailEditorView.xaml.cs` の `Loaded` イベントおよび `OnDpiChanged` ハンドラーで ViewModel の `DpiScale` を動的更新。
     - 最大レンダリング寸法（`MaxRenderDimension = 8192`）を維持し、高倍率ズーム時でも十分な解像度を確保。

2. **サムネイル生成失敗時のダーティフラグ誤リセット防止（B2）**
   - **問題**: `UpdatePageThumbnailAsync` の描画処理がキャンセルやエラーで中断した場合でも、呼び出し側で無条件に `page.IsThumbnailDirty = false` が設定され、サムネイルが未生成のまま更新対象から外れてしまう不具合。
   - **修正**: `UpdatePageThumbnailAsync` の戻り値を `Task<bool>` に変更し、レンダリングが正常完了した場合のみ `IsThumbnailDirty = false` を設定するよう呼び出し側4箇所を是正。

3. **単一インスタンス管理におけるミューテックスハンドルリーク防止（B3）**
   - **問題**: `SingleInstanceManager.TryAcquireOwnership` の再試行時、所有権を取得できなかった既存の `_mutex` インスタンスが破棄されずに上書きされ、OSハンドルがリークする問題。
   - **修正**: 新規 `Mutex` 生成前に既存の `_mutex` を明示的に `Dispose()` / `null` 化する安全策を追加。

4. **ファイルクローズ時・保存時のレンダラーキャッシュ解放（B4 / メモリリーク防止）**
   - **問題**: `PdfiumRenderer` のメモリ内キャッシュ（`_fileBytesCache`）に保持されたファイルバイト列（`byte[]`）が、ファイル終了（`CloseDocumentAsync`）後も解放されずにプロセス終了まで残留し、複数ファイル開閉時にメモリリークとなる問題。
   - **修正**:
     - `IPdfRenderer` に `InvalidateCache(string? filePath = null)` を追加し、`PdfiumRenderer` で該当ファイルのキャッシュエントリー削除（`TryRemove`）および全クリア（`Clear`）を実装。
     - `MainViewModel.CloseDocumentAsync` において、対象ファイルが他のタブで開かれていない場合にキャッシュを明示的に即時解放。全ドキュメントが閉じられた場合は全キャッシュをクリア。
     - `ExecuteSaveForDocumentAsync` においても保存先パスのキャッシュを無効化。
     - `DocumentSession.Dispose` において、各ページの `InkStrokes` および `Document.Pages` をクリアして参照を確実に切断。

---

## 3. 変更ファイル一覧

| ファイルパス | 変更区分 | 内容 |
| :--- | :--- | :--- |
| `src/PDFBinder.Core/Models/PdfDocumentModel.cs` | 変更 | `HasBinderInkAnnotations` プロパティの追加 |
| `src/PDFBinder.Core/Models/PdfPageModel.cs` | 変更 | `HasSourceBinderInkAnnotation` プロパティの追加 |
| `src/PDFBinder.Core/Services/PdfService.cs` | 変更 | 読み込みループの `Task.Run` 委譲、注釈フラグ設定 |
| `src/PDFBinder.Core/Services/IPdfRenderer.cs` | 変更 | `InvalidateCache` メソッドの追加 |
| `src/PDFBinder.Core/Services/PdfiumRenderer.cs` | 変更 | 白矩形挿入撤廃、キャッシュ付き `GetRenderBytes`、`InvalidateCache` 導入 |
| `src/PDFBinder.Core/PDFBinder.Core.csproj` | 変更 | テスト向け `InternalsVisibleTo` の追加 |
| `src/PDFBinder.App/Models/DocumentSession.cs` | 変更 | `Dispose` でのストローク・ページコレクション明示クリア |
| `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs` | 変更 | `DpiScale` プロパティ追加と寸法計算への乗算 |
| `src/PDFBinder.App/Views/DetailEditorView.xaml.cs` | 変更 | `Loaded` および `OnDpiChanged` による `DpiScale` 更新 |
| `src/PDFBinder.App/ViewModels/MainViewModel.cs` | 変更 | 重複初期化削除、サムネイル更新成否ハンドリング、クローズ時キャッシュ解放 |
| `src/PDFBinder.App/Services/SingleInstanceManager.cs` | 変更 | ミューテックス再試行時の明示的破棄・リーク防止 |
| `build.ps1` | 変更 | `EnableCompressionInSingleFile=false` の指定 |
| `tests/PDFBinder.Tests/PdfiumRendererTests.cs` | 変更 | 白矩形撤廃テスト、キャッシュ動作・無効化検証テストの更新・追加 |
| `tests/PDFBinder.Tests/DetailEditorViewModelTests.cs` | 変更 | `DpiScale` 連動解像度計算の単体テスト追加 |
| `tests/PDFBinder.Tests/SingleInstanceManagerTests.cs` | 変更 | ミューテックス再取得・破棄の単体テスト追加 |
| `tests/PDFBinder.Tests/ReEditableInkAnnotationTests.cs` | 変更 | 白矩形撤廃に伴う背景透過期待値（0）への更新 |
| `tests/PDFBinder.Tests/DocumentResourceCleanupTests.cs` | 変更 | クローズ時キャッシュ無効化検証テストの追加 |
| `Directory.Build.props` | 変更 | バージョンを `0.11.2` にインクリメント |
| `CHANGELOG.md` | 変更 | エンドユーザー向けリリースノートの追記 |
| `docs/basic_design.md` | 変更 | 直描画化、キャッシュ仕様、DpiScale対応の仕様追記 |
| `docs/PROJECT.md` | 変更 | 機能インベントリ F75 の追加 |

---

## 4. 検証結果

### 4.1 単体テスト実行 (`dotnet test`)
- 全 600 件のテストが 100% 成功（PASS）。

```text
成功!   -失敗:     0、合格:   600、スキップ:     0、合計:   600、期間: 7 s - PDFBinder.Tests.dll (net10.0)
```

### 4.2 ビルド検証 (`dotnet build`)
- 警告・エラー 0 件で正常ビルド完了。

```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```
