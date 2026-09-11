# Issue #37 自己完結版およびフレームワーク依存版のデュアル発行ビルドスクリプト改修計画

## 1. 概要と目的
現在 `build.ps1` では、.NET 10 ランタイムを同梱した自己完結型（Self-Contained / 約66MB）の単一EXEのみを発行しています。
本改修では、.NET 10 デスクトップランタイムが導入済みの環境向けに、より軽量（約20MB）でコールドスタートが高速なフレームワーク依存型（Framework-Dependent）の単一EXEも併せて発行できるように `build.ps1` を拡張します。

---

## 2. 設計方針

### 2.1 発行ターゲットの柔軟な指定
PowerShell スクリプトのパラメータとして `-Target` を追加し、用途に応じた発行を可能にします：
- `all`（デフォルト）: 自己完結版とフレームワーク依存版の両方を一括ビルド
- `self-contained`: 自己完結版のみビルド
- `framework-dependent`: フレームワーク依存版のみビルド

### 2.2 出力ディレクトリ構造と後方互換性の維持
既存の運用ルール（GEMINI.md 第3.1項の `dist/PDFBinder.exe` 出力要件）および他のスクリプトとの互換性を崩さない構成とします：

```
dist/
├── PDFBinder.exe                         # 自己完結版（従来のパス維持 / 約66MB）
├── PDFBinder-FrameworkDependent.exe      # フレームワーク依存版（ルート直下エイリアス / 約20MB）
├── self-contained/
│   └── PDFBinder.exe                     # 自己完結版の正規配置
└── framework-dependent/
    └── PDFBinder.exe                     # フレームワーク依存版の正規配置
```

### 2.3 発行コマンド仕様
1. **自己完結版 (Self-Contained)**:
   ```powershell
   dotnet publish $projectPath `
       -c Release `
       -r win-x64 `
       --self-contained true `
       -p:PublishSingleFile=true `
       -p:IncludeNativeLibrariesForSelfExtract=true `
       -p:EnableCompressionInSingleFile=true `
       -o $selfContainedDir
   ```
2. **フレームワーク依存版 (Framework-Dependent)**:
   ```powershell
   dotnet publish $projectPath `
       -c Release `
       -r win-x64 `
       --self-contained false `
       -p:PublishSingleFile=true `
       -p:IncludeNativeLibrariesForSelfExtract=true `
       -p:EnableCompressionInSingleFile=true `
       -o $frameworkDependentDir
   ```

### 2.4 サマリー表示の改善
各生成物のファイルパス、サイズ（MB）、および特徴を色分けして視覚的にわかりやすく一覧出力します。

---

## 3. 変更対象コンポーネント

1. **[`build.ps1`](file:///c:/Git/PDFBinder/build.ps1)**:
   - `-Target` パラメータ（`all`, `self-contained`, `framework-dependent`）の追加。
   - 各ターゲットごとの `dotnet publish` 実行ロジック。
   - ルート配置（`dist/PDFBinder.exe` 等）のコピー処理。
   - 結果サマリー出力の刷新。
2. **ドキュメントの同期更新**:
   - `README.md`: ビルド手順（オプション指定方法や両版の違い）の解説を追記。
   - `docs/basic_design.md`: 配布形態の仕様を更新。
   - `docs/PROJECT.md`: 機能インベントリ（F04）の更新。

---

## 4. 検証計画

### 4.1 ビルド・発行検証
1. `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target all` を実行し、両方の単一EXEが正常に生成されること。
2. 生成された各EXEのファイルサイズが想定通りであること：
   - 自己完結版: 約 66 MB
   - フレームワーク依存版: 約 20〜25 MB
3. `dist/PDFBinder.exe` に自己完結版が正常に配置されていること。
4. `-Target self-contained` および `-Target framework-dependent` の個別指定が正常に動作すること。

### 4.2 動作検証
- 生成された各EXEが正常に起動し、PDF閲覧および手書き編集が動作すること。
