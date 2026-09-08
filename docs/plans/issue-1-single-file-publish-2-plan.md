# 実装計画書: 単一EXEファイル（Self-Contained Single-File）発行環境の構築 (Issue #1 - 第2版)

## 概要
PDF Binder を、.NET 10 ランタイムがインストールされていない環境でも単体で起動できる「自己完結型単一実行ファイル（Self-Contained Single-File EXE）」としてビルド・発行できるように構成します。

---

## 提案する変更内容

### 1. `PDFBinder.App.csproj` の発行構成追加
- `<AssemblyName>PDFBinder</AssemblyName>` を追加し、出力exeファイル名を `PDFBinder.exe` に統一。
- 単一ファイル発行プロパティの最適化:
  - `PublishSingleFile`: true
  - `SelfContained`: true
  - `RuntimeIdentifier`: win-x64
  - `IncludeNativeLibrariesForSelfExtract`: true (Docnet.Core / pdfium.dllの自己展開)
  - `EnableCompressionInSingleFile`: true (exeサイズ圧縮)

### 2. `.gitignore` の更新
- `dist/` ディレクトリを Git 管理から除外。

### 3. ワンコマンド発行スクリプト `build.ps1` の作成
- プロジェクトルートに PowerShell 発行スクリプトを作成。
- クリーンアップ、Releaseビルド、単一exe発行を `dist/` に自動出力。

### 4. ドキュメント更新
- `README.md` に単一exe発行コマンドおよびスクリプトの使用方法を追記。
- `docs/basic_design.md` の発行仕様セクションを更新。
- `docs/PROJECT.md` を更新。

---

## 検証計画

### 1. 単一EXEの発行実行
```powershell
.\build.ps1
```
または
```powershell
dotnet publish src/PDFBinder.App/PDFBinder.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/
```

### 2. 出力ファイルの検証
- `dist/PDFBinder.exe` が生成されていること。
- ファイルプロパティおよびアプリアイコンが適用されていること。

### 3. テストとビルドの完全性検証
```powershell
dotnet test
dotnet build
```
