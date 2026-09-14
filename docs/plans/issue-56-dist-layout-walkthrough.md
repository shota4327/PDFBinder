# [検証報告] ビルド成果物の配置構成変更 (Issue #56)

Issue #56 に基づき、`build.ps1` による単一実行可能ファイル（Single-File EXE）の成果物配置を見直し、`dist/` 直下にフレームワーク依存版を直接配置し、自己完結版は `dist/self-contained/` フォルダ配下に独立配置するよう改修しました。

## 変更内容まとめ

### 1. `build.ps1` の改修
- **成果物配置の変更**:
  - フレームワーク依存版の出力先を `-o $distDir`（`dist/` 直下）に変更。`dist/framework-dependent` サブフォルダおよびエイリアス（`PDFBinder-FrameworkDependent.exe`）の生成を廃止。
  - 自己完結版の出力先は `-o $selfContainedDir`（`dist/self-contained/`）のまま保持し、`dist/` 直下への複製処理を削除。
- **クリーンアップ処理の改善**:
  - `-Target all`: `dist/` ディレクトリ全体をクリーンアップ。
  - `-Target self-contained`: `dist/self-contained/` のみをクリーンアップ。
  - `-Target framework-dependent`: `dist/` 直下のファイル（および旧 `dist/framework-dependent/` があれば）を削除し、`dist/self-contained/` フォルダは保持。
- **サマリー表示の更新**:
  - `dist/PDFBinder.exe` をフレームワーク依存版（約8.4MB）として表示。
  - `dist/self-contained/PDFBinder.exe` を自己完結版（約66MB）として表示。

### 2. 関連ドキュメントの更新
- **`README.md`**: 単一実行ファイル（Single-File EXE）の発行セクションのパス・説明を新構成に更新。
- **`docs/PROJECT.md`**: Feature Inventory（F04）の成果物パス記述を更新。
- **`GEMINI.md`**: 第3.1項の「マージ完了時の単一EXE自動発行」において、成果物として `dist/PDFBinder.exe`（フレームワーク依存版）および `dist/self-contained/PDFBinder.exe`（自己完結版）が発行される旨を更新。

---

## 成果物ディレクトリ構造

```text
dist/
├── PDFBinder.exe                      # フレームワーク依存版（約8.4MB / OSの .NET 10 を利用）
├── PDFBinder.pdb / PDFBinder.Core.pdb
└── self-contained/
    ├── PDFBinder.exe                  # 自己完結版（約66MB / .NET 10 ランタイム内包）
    └── PDFBinder.pdb / PDFBinder.Core.pdb
```

---

## 検証結果

### 1. ビルドスクリプトの実行検証
- `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target all`
  - `dist/PDFBinder.exe`（8.43 MB）が生成されることを確認
  - `dist/self-contained/PDFBinder.exe`（66.28 MB）が生成されることを確認
  - `dist/framework-dependent` や `PDFBinder-FrameworkDependent.exe` が生成されないことを確認
- `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target framework-dependent`
  - `dist/self-contained/` ディレクトリが保護されたまま、直下の `dist/PDFBinder.exe` が正常に再発行されることを確認
- `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target self-contained`
  - 直下の `dist/PDFBinder.exe` が保護されたまま、`dist/self-contained/PDFBinder.exe` が正常に再発行されることを確認

### 2. 単体テストおよびビルド検証
- `dotnet test`: 117件中 117件 合格（100% PASS）
- `dotnet build`: 警告0件、エラー0件でビルド成功
