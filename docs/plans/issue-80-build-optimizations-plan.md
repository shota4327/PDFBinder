# ビルド構成の最適化（DLL分離・ReadyToRun・トリミング・GC設定・PDB埋め込み）実装計画

## 概要
フレームワーク依存版（`dist/` 直下）の単一 EXE 化を解除して DLL 分離形式とし、自己完結版（`dist/self-contained/`）は単一 EXE を維持しつつトリミング（`TrimMode=partial`）を導入します。また、両ターゲットに対して ReadyToRun（起動高速化）、PDB 埋め込み、ガベージコレクション（GC）最適化、決定論的ビルドを適用します。

## ユーザー合意事項（確認済み要件）
- **ディレクトリ構造**: `dist/` 直下にフレームワーク依存版の EXE と DLL 群を展開し、`dist/self-contained/` に自己完結版の単一 EXE を配置。
- **トリミング**: 自己完結版に `TrimMode=partial` を適用し、ランタイムの不要コードを削減しつつ動作検証を行う。
- **ReadyToRun**: フレームワーク依存版・自己完結版の両方に適用し、起動速度（JIT コスト）を最適化。
- **デバッグ情報（PDB）**: 両ターゲットに `DebugType=embedded` を適用。
- **デジタル署名**: 対象外。

## 変更対象ファイルと設計

### 1. プロジェクト設定 (`src/PDFBinder.App/PDFBinder.App.csproj`)
- `<PropertyGroup>` に以下の設定を追加:
  - `<ServerGarbageCollection>false</ServerGarbageCollection>`: デスクトップアプリに最適なワークステーション GC を強制（メモリ消費抑制）。
  - `<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>`: バックグラウンド GC を有効化し、UI スレッドの停止（カクつき）を防止。
  - `<Deterministic>true</Deterministic>`: 同一ソースから常に同一バイナリを生成する決定論的ビルドを有効化。

### 2. ビルドスクリプト (`build.ps1`)
- **クリーン処理の改修**:
  - `dist/` 直下をクリーンする際、`dist/self-contained/` サブディレクトリを保護・維持しつつ、フレームワーク依存版のファイル群（EXE, DLL, json, runtimeconfig 等）のみを確実に削除するロジックに変更。
- **Self-Contained（自己完結版）発行オプション**:
  - `-p:PublishSingleFile=true`
  - `-p:IncludeNativeLibrariesForSelfExtract=true`
  - `-p:EnableCompressionInSingleFile=true`
  - `-p:PublishReadyToRun=true` (追加)
  - `-p:PublishTrimmed=true` (追加)
  - `-p:TrimMode=partial` (追加)
  - `-p:DebugType=embedded` (追加)
- **Framework-Dependent（フレームワーク依存版）発行オプション**:
  - `-p:PublishSingleFile=false`（DLL 分離形式）
  - `-p:PublishReadyToRun=true` (追加)
  - `-p:DebugType=embedded` (追加)
- **サマリー出力の更新**:
  - フレームワーク依存版（EXE + 分離 DLL 群）および自己完結版（単一 EXE）のファイル構成とサイズを正確に表示。

### 3. プロジェクトドキュメント同期
- `GEMINI.md`: 単一 EXE 発行スクリプトに関する記述を最新仕様（フレームワーク依存版: DLL分離、自己完結版: 単一EXE）に同期。
- `README.md` / `docs/basic_design.md`: ビルド成果物の仕様（フレームワーク依存版は DLL 分離、自己完結版は単一 EXE）および最適化内容を反映。

## 検証計画

### 自動テスト
- `dotnet test`: 全単体テストが 100% PASS することを確認。

### ビルドおよび動作検証
1. `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target framework-dependent`
   - `dist/` 直下に `PDFBinder.exe`, `PDFBinder.Core.dll`, `Docnet.Core.dll`, `PdfSharp.dll`, `pdfium.dll` 等が展開されることを確認。
   - 外部 `.pdb` ファイルが存在しない（埋め込み済み）ことを確認。
2. `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target self-contained`
   - `dist/self-contained/PDFBinder.exe` が単一 EXE として生成されることを確認。
   - トリミングによるサイズ削減効果を確認。
3. `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target all`
   - 両ターゲットのビルド・クリーニングが競合なく一括完了することを確認。
