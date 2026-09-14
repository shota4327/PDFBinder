# [実装計画] ビルド成果物の配置構成変更 (Issue #56)

`build.ps1` による単一EXE発行の成果物配置を見直し、`dist/` 直下にフレームワーク依存版（Framework-Dependent）の内容を直接配置し、自己完結版（Self-Contained）は `dist/self-contained/` フォルダ配下に配置する構成に変更します。

## ユーザー確認事項
- **成果物パスの変更**:
  - フレームワーク依存版: `dist/PDFBinder.exe`（これまでの `dist/framework-dependent/PDFBinder.exe` および `dist/PDFBinder-FrameworkDependent.exe` から変更・集約）
  - 自己完結版: `dist/self-contained/PDFBinder.exe`（`dist/` 直下へのコピーは行わず、サブディレクトリ内に配置）
- **エイリアス廃止**: `PDFBinder-FrameworkDependent.exe` は廃止され、`dist/PDFBinder.exe` がフレームワーク依存版となります。

## 変更内容

### 1. ビルドスクリプトの改修
#### [MODIFY] [build.ps1](file:///c:/Git/PDFBinder/build.ps1)
- **クリーンアップ処理の改善**:
  - `$Target -eq "all"`: `dist/` ディレクトリ全体をクリーンアップ。
  - `$Target -eq "self-contained"`: `dist/self-contained/` フォルダのみをクリーンアップ。
  - `$Target -eq "framework-dependent"`: `dist/` 直下のファイル（および旧 `dist/framework-dependent/` があれば）を削除し、`dist/self-contained/` フォルダは保持。
- **発行先パスの変更**:
  - フレームワーク依存版の発行先 `-o` を `$distDir`（`dist/` 直下）に変更。
  - 自己完結版の発行先 `-o` は引き続き `$selfContainedDir`（`dist/self-contained/`）。
  - `dist/` ルートへのファイルコピー処理（旧エイリアスや旧自己完結版コピー）を削除。
- **サマリー表示の更新**:
  - `dist/PDFBinder.exe` をフレームワーク依存版として表示。
  - `dist/self-contained/PDFBinder.exe` を自己完結版として表示。

### 2. ドキュメントおよびルールの同期更新
#### [MODIFY] [README.md](file:///c:/Git/PDFBinder/README.md)
- 「単一実行ファイル（Single-File EXE）の発行」セクションの成果物パス・説明を新構成に更新。

#### [MODIFY] [docs/PROJECT.md](file:///c:/Git/PDFBinder/docs/PROJECT.md)
- Feature Inventory（F04）の成果物パス記述を新構成に更新。

#### [MODIFY] [GEMINI.md](file:///c:/Git/PDFBinder/GEMINI.md)
- セクション 3.1 の「マージ完了時の単一EXE自動発行」において、成果物として `dist/PDFBinder.exe`（フレームワーク依存版）および `dist/self-contained/PDFBinder.exe`（自己完結版）が発行される旨を明記。

---

## 検証手順

### 自動テスト / スクリプト検証
1. `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target all` を実行
   - `dist/PDFBinder.exe`（フレームワーク依存版: 約8〜9MB）が存在すること
   - `dist/self-contained/PDFBinder.exe`（自己完結版: 約66MB）が存在すること
   - `dist/framework-dependent/` フォルダおよび `PDFBinder-FrameworkDependent.exe` が存在しないこと
2. `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target framework-dependent` を実行
   - `dist/self-contained/` フォルダとその中身が削除されずに保持されていること
   - `dist/PDFBinder.exe` が正常に再ビルド・更新されること
3. `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target self-contained` を実行
   - `dist/PDFBinder.exe` は削除されずに保持されること
   - `dist/self-contained/PDFBinder.exe` が正常に再ビルド・更新されること
4. `dotnet test`
   - 全単体テストが 100% PASS すること
5. `dotnet build`
   - エラー・警告なくビルドが成功すること
