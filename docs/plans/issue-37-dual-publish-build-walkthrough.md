# Issue #37 自己完結版およびフレームワーク依存版のデュアル発行ビルドスクリプト改修検証報告（Walkthrough）

Issue #37 に基づき、`build.ps1` を改修し、完全ポータブルな「自己完結型（Self-Contained）」と、超軽量・高速起動な「フレームワーク依存型（Framework-Dependent）」の両方の単一実行可能ファイル（Single-File EXE）を発行できるように拡張しました。

---

## 1. 実施内容の概要

### 1.1 `build.ps1` の改修
- **`-Target` パラメータの追加**:
  - `all`（デフォルト）: 自己完結版およびフレームワーク依存版の両方を一括ビルド
  - `self-contained`: 自己完結版のみビルド
  - `framework-dependent`: フレームワーク依存版のみビルド
- **フレームワーク依存版（Framework-Dependent）の発行仕様**:
  - `--no-self-contained` オプションを指定し、OSの .NET 10 デスクトップランタイムを参照。
  - `-p:PublishSingleFile=true` および `-p:IncludeNativeLibrariesForSelfExtract=true` により、ネイティブライブラリ（`pdfium.dll`）を同梱した単一EXEを出力。
- **後方互換性とディレクトリ構成**:
  - `dist/self-contained/PDFBinder.exe` および従来のルート `dist/PDFBinder.exe` に自己完結版を配置。
  - `dist/framework-dependent/PDFBinder.exe` およびルート `dist/PDFBinder-FrameworkDependent.exe` にフレームワーク依存版を配置。
- **サマリー表示の刷新**:
  - 各生成物のパス、サイズ（MB）、および特徴を色分けして視覚的に一覧出力。

---

## 2. 発行成果物の検証結果

`powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target all` を実行した結果：

| 発行形態 | 出力パス | ファイルサイズ | 特徴・用途 |
| :--- | :--- | :---: | :--- |
| **自己完結版 (Self-Contained)** | `dist/self-contained/PDFBinder.exe`<br>（`dist/PDFBinder.exe`） | **66.26 MB** | .NET 10 ランタイム同梱。未インストール環境でも完全オフライン・ポータブル動作 |
| **フレームワーク依存版 (Framework-Dependent)** | `dist/framework-dependent/PDFBinder.exe`<br>（`dist/PDFBinder-FrameworkDependent.exe`） | **8.41 MB** | OSの .NET 10 デスクトップランタイムを利用。**約 87% の軽量化**とコールドスタート高速化 |

---

## 3. 変更ファイル一覧

1. **[`build.ps1`](file:///c:/Git/PDFBinder/build.ps1)**:
   - デュアル発行ロジック（`-Target` パラメータ対応）の実装。
2. **[`README.md`](file:///c:/Git/PDFBinder/README.md)**:
   - 単一実行ファイル発行コマンドおよび両版の特徴・使い分けを追記。
3. **[`docs/basic_design.md`](file:///c:/Git/PDFBinder/docs/basic_design.md)**:
   - コア設計原則（ポータブル＆完全オフライン）にデュアル発行仕様を反映。
4. **[`docs/PROJECT.md`](file:///c:/Git/PDFBinder/docs/PROJECT.md)**:
   - 機能インベントリ（F04）をデュアル発行対応に更新。

---

## 4. 自動テストおよびビルド検証

- **単体テスト (`dotnet test`)**: **全71件 PASS（成功）**
- **ビルド (`dotnet build`)**: **警告 0、エラー 0 で成功**
