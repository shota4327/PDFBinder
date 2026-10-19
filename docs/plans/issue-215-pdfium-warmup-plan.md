# Issue #215 実装計画: PDFiumネイティブライブラリのコールドスタート遅延の解消（事前初期化）

## 1. 概要・目的
PDF Binder 起動後、最初にPDFファイルを開いた際に発生する `Docnet.Core`（内部のネイティブ `pdfium.dll`）の初回ロードおよび初期化コストを、バックグラウンドでのプリウォーム（事前初期化）によって隠蔽し、初回ファイルオープン時の1ページ目表示までの体感待ち時間を大幅に短縮します。

---

## 2. 現状の課題と原因
- 現在、`Docnet.Core` のシングルトン `DocLib.Instance` は、ユーザーが最初にPDFファイルを開いて描画（`PdfiumRenderer.RenderPageAsync` 等）が実行されるタイミングで初めてアクセスされます。
- `DocLib.Instance` 初回呼び出し時には、ネイティブライブラリ `pdfium.dll` のロード・メモリ展開・初期化処理が実行されるため、PDF自体のパース時間に加えてネイティブライブラリのコールドスタート遅延が上乗せされ、初回表示の初動レイテンシの原因となっていました。

---

## 3. 実装方針・設計

### 3.1 `IPdfRenderer` および `PdfiumRenderer` への `Warmup` メソッドの追加
- `IPdfRenderer` に事前初期化用メソッド `void Warmup()` を定義（インターフェースデフォルト実装として空実装を提供）。
- `PdfiumRenderer` に `public static void Warmup()` および `public void Warmup()` を実装。
- 内部で `_ = DocLib.Instance;` を呼び出し、ネイティブライブラリのロードと初期化を完了させる。
- 万が一の初期化エラー時にもメインUIや後続の処理に影響を与えないよう、try-catch で例外を捕捉してログ出力（`Debug.WriteLine`）し、安全に握り潰す設計とする（初回レンダリング時に再度ロードが試行されます）。

```csharp
/// <summary>
/// PDFiumネイティブライブラリ（DocLib.Instance）をバックグラウンド等で事前に初期化（プリウォーム）します。
/// </summary>
public static void Warmup()
{
    try
    {
        _ = DocLib.Instance;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[PdfiumRenderer] Warmup failed: {ex.Message}");
    }
}

/// <inheritdoc/>
public void Warmup() => Warmup();
```

### 3.2 `App.OnStartup` におけるバックグラウンド初期化のキック
- `src/PDFBinder.App/App.xaml.cs` の `OnStartup` において、`mainWindow.Show()` および `WindowActivationHelper.BringToForeground(mainWindow)` の直後に `Task.Run(() => PdfiumRenderer.Warmup());` を実行する。
- これにより、メインウィンドウの初期描画を一切ブロックすることなく、バックグラウンドスレッドで先行して `pdfium.dll` の展開・初期化が完了する。
- 起動時コマンドライン引数（ファイルパス指定）がある場合も、UI初期化およびファイル読み込み処理と並行してネイティブ初期化が走るため、初回のページレンダリング開始時点ですでにネイティブ初期化が完了している状態を実現する。

---

## 4. 変更対象ファイル
1. `src/PDFBinder.Core/Services/IPdfRenderer.cs`
   - `void Warmup()` メソッド追加
2. `src/PDFBinder.Core/Services/PdfiumRenderer.cs`
   - `public static void Warmup()` および `public void Warmup()` 実装追加
3. `src/PDFBinder.App/App.xaml.cs`
   - `OnStartup` にて `Task.Run(() => PdfiumRenderer.Warmup());` の非同期プリウォーム処理を追加
4. `tests/PDFBinder.Tests/PdfiumRendererTests.cs`
   - `Warmup_ExecutesWithoutException` 単体テストの追加

---

## 5. テスト・検証計画
1. **単体テスト検証**:
   - `PdfiumRendererTests` に `Warmup` メソッドを呼び出すテストケースを追加し、例外なく安全に終了することを確認。
   - `dotnet test` を実行し、全単体テストが 100% パスすることを確認。
2. **ビルド検証**:
   - `dotnet build` を実行し、警告およびエラーなく成功することを確認。
