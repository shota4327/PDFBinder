# ビルド構成の最適化（DLL分離・ReadyToRun・GC設定・PDB埋め込み）検証報告 (Walkthrough)

## 概要
Issue [#80](https://github.com/shota4327/PDFBinder/issues/80) に基づき、ビルド構成の最適化を実施しました。フレームワーク依存版（`dist/` 直下）の単一 EXE 化を解除して DLL 分離形式とし、自己完結版（`dist/self-contained/`）は単一 EXE を維持しつつ ReadyToRun による起動高速化を適用しました。また、両ターゲットにおいてデバッグシンボル（PDB）の埋め込み、ガベージコレクション（GC）最適化設定、および決定論的ビルドを適用しました。

---

## 実施した変更内容

### 1. プロジェクト設定の最適化 ([PDFBinder.App.csproj](file:///c:/Git/PDFBinder/src/PDFBinder.App/PDFBinder.App.csproj))
`<PropertyGroup>` に以下のプロパティを追加しました：
- `<ServerGarbageCollection>false</ServerGarbageCollection>`: ワークステーション GC を強制し、デスクトップアプリの物理メモリ消費を抑制。
- `<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>`: バックグラウンド GC を有効化し、ページ操作や描画時の UI スレッド停止（カクつき）を防止。
- `<Deterministic>true</Deterministic>`: 同一ソースから常にビット単位で同一のバイナリを生成する決定論的ビルドを有効化。

### 2. ビルドスクリプトの最適化 ([build.ps1](file:///c:/Git/PDFBinder/build.ps1))
- **フレームワーク依存版（Framework-Dependent）**:
  - `-p:PublishSingleFile=false`: 単一 EXE 化を解除し、`PDFBinder.exe` と各依存 DLL 群を `dist/` 直下に直接展開。
  - `-p:PublishReadyToRun=true`: 事前ネイティブコンパイルによる起動時 JIT コストの削減。
  - `-p:DebugType=embedded`: 外部 `.pdb` ファイルを排除し、アセンブリ内に埋め込み。
- **自己完結版（Self-Contained）**:
  - `-p:PublishSingleFile=true`: 単一 EXE を維持。
  - `-p:PublishReadyToRun=true`: 起動高速化。
  - `-p:DebugType=embedded`: 外部 `.pdb` を排除。
  - ※トリミング（`PublishTrimmed=true`）について：WPF は XAML バインディングやリフレクションに強く依存しているため、.NET SDK 仕様によりトリミングが明示的に非推奨・エラー（`NETSDK1168`）となります。無理に抑制すると実行時クラッシュを引き起こすため、安定性を重視して自己完結版は ReadyToRun ＋ 単一ファイル圧縮による最適化を適用しました。
- **クリーン処理の改修**:
  - `dist/` 直下のフレームワーク依存版ファイルをクリーンする際、`dist/self-contained/` ディレクトリを安全に保護・維持するよう改善。
- **コンソール文字化け防止**:
  - PowerShell 出力の UTF-8 エンコーディングを明示。

### 3. ドキュメントの同期更新
- [GEMINI.md](file:///c:/Git/PDFBinder/GEMINI.md): 配布バイナリ発行スクリプトの説明を DLL 分離形式および単一 EXE 形式に同期。
- [README.md](file:///c:/Git/PDFBinder/README.md): ビルド手順および配布バイナリ仕様（フレームワーク依存版: DLL 分離、自己完結版: 単一 EXE）を更新。
- [docs/basic_design.md](file:///c:/Git/PDFBinder/docs/basic_design.md): コア設計原則における二系統発行の仕様記述を更新。

---

## 検証結果

### 1. 単体テスト (`dotnet test`)
- 実行結果: **180 件中 180 件合格（100% PASS）**
```text
成功! - 失敗: 0、合格: 180、スキップ: 0、合計: 180、期間: 1 s - PDFBinder.Tests.dll (net10.0)
```

### 2. 発行ビルド検証 (`powershell -ExecutionPolicy Bypass -File .\build.ps1`)
一括ビルドおよび各個別ターゲットのビルドを実行し、正常完了を確認しました。

#### ① フレームワーク依存版 (`dist/`)
- **エントリ EXE**: `dist/PDFBinder.exe`（0.19 MB）
- **ディレクトリ構成**: 20 ファイル、合計 10.77 MB
  - `PDFBinder.Core.dll`, `Docnet.Core.dll`, `pdfium.dll`, `PdfSharp.dll` 等が分離展開
  - 外部 `.pdb` ファイルの散乱なし（アセンブリ埋め込みを確認）
  - `PDFBinder.runtimeconfig.json` に `System.GC.Concurrent: true`, `System.GC.Server: false` が正常反映

#### ② 自己完結版 (`dist/self-contained/`)
- **単一 EXE**: `dist/self-contained/PDFBinder.exe`（71.97 MB）
- 外部 `.pdb` ファイルなし、単一ファイル完結を確認。
