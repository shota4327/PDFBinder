# 検証報告書 (Walkthrough): 単一EXEファイル（Self-Contained Single-File）発行環境の構築 (Issue #1 - 第2版)

## 1. 実施概要
Issue #1 の追加要件として、PDF Binder を .NET 10 ランタイムがインストールされていない環境でも単体で動作する「自己完結型単一実行ファイル（Self-Contained Single-File EXE）」としてビルド・発行できるよう環境を構築・検証しました。

---

## 2. 成果物一覧

### プロジェクト構成・発行スクリプト
- `src/PDFBinder.App/PDFBinder.App.csproj`:
  - `<AssemblyName>PDFBinder</AssemblyName>` を設定
  - アプリアイコン（`Assets/icon.ico`）のリソース統合
- `build.ps1`:
  - ワンコマンドでクリーンアップから Release / win-x64 / Self-Contained 単一exe発行までを実行する自動化スクリプト
  - `dist/PDFBinder.exe` に出力
- `.gitignore`:
  - 出力ディレクトリ `dist/` を Git 管理から除外
- **ドキュメント**:
  - `README.md`: 単一EXE発行手順および起動説明を追記
  - `docs/PROJECT.md`: 機能インベントリに `F04 (単一EXE発行環境)` を追加し完了更新
  - `docs/plans/issue-1-single-file-publish-2-plan.md`: 実装計画書の永続保管

---

## 3. 検証結果

### 1. 単一EXEの発行実行 (`build.ps1`)
```powershell
==========================================================
 発行成功: C:\Git\PDFBinder\dist\PDFBinder.exe
 ファイルサイズ: 66.23 MB
 この単一exeファイルのみで完全オフライン・ランタイム不要で起動可能です。
==========================================================
```
- 出力ファイル: `dist/PDFBinder.exe`
- ファイルサイズ: 約 66.2 MB（.NET 10 ランタイム、WPFコア、PDFiumネイティブバイナリを完全同梱・圧縮）

### 2. 単体テスト全件検証 (`dotnet test`)
```powershell
成功!   -失敗: 0、合格: 18、スキップ: 0、合計: 18 (PDFBinder.Tests.dll)
```
- 全 18 件の単体テストがすべて PASS。

### 3. ソリューションビルド検証 (`dotnet build`)
```powershell
ビルドに成功しました。
    0 個の警告
    0 エラー
```
- 警告・エラー 0 件でビルド成功。
