# 実装計画: 起動・表示パフォーマンスの大幅高速化と表示・ライフサイクル関連不具合の解消 (Issue #204 改訂第2版)

## 1. 概要
本改修では、PDF Binder における起動処理、ファイルオープン、サムネイル一覧レンダリング、詳細エディタ表示、高DPI文字描画、および二重起動制御におけるパフォーマンスボトルネックと不具合を包括的に解消します。

ユーザーからのフィードバックに基づき、**「レンダラーでの不要な白矩形挿入（`doc.Save`）を完全撤廃し、UI（XAML）側の白用紙背景に委譲する」** 方針を採用します。自前手書き注釈がない通常の外部PDFでは再保存処理を一切スキップして直接描画することで、1ページ目の表示ラグを 0ms（待たされない）にしつつ、サムネイル生成を極限まで高速化します。また、4Kディスプレイでの高倍率ズーム時の鮮明度を維持するため、**最大描画寸法は 8192px を維持** します。

---

## 2. 変更対象コンポーネントと詳細設計

### 2.1 【P1 & P2】レンダラー白矩形挿入の完全撤廃と直接高速描画 (`PdfiumRenderer.cs`, `PdfService.cs`)
- **課題と背景**:
  - 現状の `RenderPageAsync` は1ページ描画ごとに `SanitizeForRendering` を呼び出し、PdfSharp で白矩形を描いて `doc.Save`（PDF全体の再シリアライズ）を実行している。
  - しかし、背景が透明なPDFであっても、すでに `GridView.xaml`（`PageBorder` の `Background="White"`）および `DetailEditorView.xaml`（用紙枠の `Background="White"`）において用紙背景が白に設定されているため、WPF の DirectX レンダリングエンジンが背後の白地の上に完全なアンチエイリアスで自動ブレンド表示する。
  - したがって、レンダラーがPDF内部に白矩形を差し込む処理は、完全に余計な「二重防御」であり、巨大なボトルネック（500ページPDFでの初期表示ラグやサムネイル生成遅延）の原因となっていた。
- **改修方針**:
  - **白矩形描画（`SanitizeForRendering` 内の `doc.Save`）の完全撤廃**:
    - 白矩形描画のための `PdfSharp` によるPDF書き換え・再保存処理を全廃。
    - 外部から開いた通常のPDFは、ディスクから読み込んだ生のバイト列をそのまま Docnet（PDFium）に渡して直接レンダリングを行う。
  - **自前手書き注釈の二重表示防止の具体的実装方法**:
    - **背景**: 本アプリで手書きを保存したPDFには `/AP`（外観ストリーム）付きの注釈が埋め込まれている。PDFium はこれを自動描画するため、注釈を除去しないとアプリ前面の編集可能なインクレイヤー（`EditorInkCanvas` や `StrokeCache`）と二重に重なって表示されてしまう（消しゴムで消しても背景に古い手書きが残る）。
    - **判定方法**: `PdfService.LoadDocumentAsync` でファイルを読み込む際、全ページ走査時に自前の手書き注釈（`PdfBinderInkAnnotation`）がファイル内に存在するかを判定し、ドキュメントモデルに `HasBinderInkAnnotations` フラグ（bool）を設定。
    - **通常の外部PDF（99%以上のケース）**: `HasBinderInkAnnotations == false` の場合、注釈除去処理（PdfSharp）を一切通さず、読み込んだ生のバイト列をそのまま直接 Docnet（PDFium）に渡してレンダリング（`doc.Save` は 0回）。
    - **自前注釈が存在するファイルのみ**: `HasBinderInkAnnotations == true` の場合のみ、初回レンダリング時にファイルから自前注釈を除去したバイト列を1回だけ生成（キャッシュ）し、レンダリング時にそれを使用。
  - **サムネイル合成時の描画**:
    - `CompositeStrokes` 内への白矩形追加は行わない（`GridView.xaml` の `PageBorder` に `Background="White"` が設定されているため、表示上何ら問題ないため不要）。
  - **テキスト・リンク抽出の直接化**:
    - `ExtractInteractiveDataAsync` も同一のPDFバイト列から直接抽出。
- **効果**:
  - 通常のPDFでは `doc.Save`（再保存）が **最初から完全に 0回**。
  - 500ページの巨大PDFであっても、**1ページ目の表示ラグは 0ms（待たされない）**。
  - 全ページのサムネイル生成が、純粋な PDFium のネイティブ C++ 速度そのものとなり、数倍〜10倍高速化。

### 2.2 【P5】ドキュメント初期化の二重実行排除 (`MainViewModel.cs`)
- **課題**:
  - `OnActiveSessionChanged` 内で `Document = newValue.Document;` が実行されると、プロパティセッター経由で `OnDocumentChanged` がトリガーされる。
  - `OnDocumentChanged` の末尾で `DetailEditor?.InitializeDocument(newValue)` が実行され、その直後の `OnActiveSessionChanged` 内でも再度 `DetailEditor?.InitializeDocument(newValue.Document)` が実行されている。
  - `InitializeDocument` は既存の全ページアイテムを破棄・再生成し、先行描画タスク（`LoadInitialDocumentBackgroundAsync`）をキックするため、無駄な二重生成・二重タスク起動と画面のちらつきが生じる。
- **改修方針**:
  - `Document` セッターからの初期化呼び出しとセッション変更ハンドラでの初期化呼び出しの責務を整理し、同一ドキュメントインスタンスに対する重複した `InitializeDocument` の実行を抑止。

### 2.3 【P6】ファイル読み込み構造解析の完全バックグラウンド化 (`PdfService.cs`)
- **課題**:
  - `LoadDocumentAsync` において、`await File.ReadAllBytesAsync` の後の `PdfReader.Open(stream, Import)`、全ページの走査、および手書き注釈のBase64デシリアライズ処理が呼び出し元のUIスレッドに復帰してから同期待ちで実行されている。
  - ページ数が多いPDFや手書き注釈が多いファイルを開く際、UIスレッドが数百ms〜数秒フリーズする。
- **改修方針**:
  - `ReadAllBytesAsync` 以降の `PdfReader.Open`、ページモデル構築、手書き注釈復元ループ全体を `Task.Run` に委譲。
  - UIスレッドのフリーズ時間を 0ms（完全非同期）化。

### 2.4 【P7】自己完結EXEの起動時圧縮解除 (`build.ps1`)
- **課題**:
  - `build.ps1` において `-p:EnableCompressionInSingleFile=true` が指定されており、コールドスタート時に約150MBのアセンブリ群を解凍展開するオーバーヘッド（約250〜500ms）が発生している。
- **改修方針**:
  - `-p:EnableCompressionInSingleFile=false` に変更。
  - コールドスタート速度をフレームワーク依存版と同等水準まで高速化。

### 2.5 【P8方針維持】詳細エディタ最大描画寸法 8192px の維持 (`DetailEditorViewModel.cs`)
- **確認結果**:
  - 4Kディスプレイ（3840×2160）でA4サイズの上半分などを高倍率拡大表示（ズーム380%〜500%等）した場合、必要なピクセル数は 4,320 px や 5,615 px に達する。
  - これを 4096 px に制限すると、拡大時に物理ピクセルに対して解像度が不足し、文字が引き伸ばされてボケてしまう。
- **改修方針**:
  - 「見た目の品質を落とさない」という大原則を厳守し、**`MaxRenderDimension = 8192` を維持**。
  - メモリ対策は、既存の「画面外ページの動的アンロード（LRUエビクション）」機構により適切に管理する。

### 2.6 【B1】高DPI環境での詳細ビュー鮮明化 (`DetailEditorViewModel.cs`, `DetailEditorView.xaml.cs`)
- **課題**:
  - `CalculateRenderDimensions` において、`page.DisplayWidth * (96.0 / 72.0) * zoom` のみで計算しており、Windowsの画面拡大率（`DpiScale`: 125%, 150%, 200%等）が掛けられていない。
  - 150%や200%の高DPI環境において、画面上の物理ピクセル数に対して小さなビットマップが拡大表示され、文字がぼやける。
- **改修方針**:
  - `DetailEditorViewModel` に `DpiScale` プロパティ（初期値 1.0）を追加。
  - `DetailEditorView.xaml.cs` の初期化時（`Loaded`）および `DpiChanged` イベントにおいて、`VisualTreeHelper.GetDpi(this).DpiScaleX` を取得して ViewModel の `DpiScale` に反映。
  - `CalculateRenderDimensions` のスケール計算式を `double scale = PtToDipScale * zoom * DpiScale;` に更新。
  - 高DPIディスプレイ環境でもRetina品質の極めて鮮明な文字描画を実現。

### 2.7 【B2】サムネイル中断時の古い画像残存防止 (`MainViewModel.cs`)
- **課題**:
  - `UpdatePageThumbnailAsync` がキャンセルやnullで中断された場合でも、呼び出し側（`RunEnsureThumbnailsAsync`, `GenerateWindowThumbnailsAsync` 等）が無条件で `page.IsThumbnailDirty = false` を実行している。
  - サムネイル画像は古い回転や手書きのままなのに、フラグがリセットされて二度と再生成されなくなる。
- **改修方針**:
  - `UpdatePageThumbnailAsync` の戻り値を `Task<bool>` に変更（新しいビットマップが正常に設定された場合のみ `true`）。
  - 呼び出し側で `if (await UpdatePageThumbnailAsync(page, token)) { page.IsThumbnailDirty = false; }` とし、中断時はダーティフラグを維持して次回のパイプラインで確実に再試行されるように改善。

### 2.8 【B3】二重起動ミューテックスのクリーンアップ漏れ修正 (`SingleInstanceManager.cs`)
- **課題**:
  - 既存インスタンスへのIPC送信がタイムアウトした際の再試行時、1回目に生成した `_mutex` を解放・破棄しないまま再度 `new Mutex` を生成しており、ハンドルがリークし、所有権の再取得判定が阻害される。
- **改修方針**:
  - `TryAcquireOwnership()` の冒頭で、既存の `_mutex` が存在し所有権未保持の場合は事前に `_mutex.Dispose()` を実施してハンドルを解放。クリーンな状態で再試行できるように修正。

---

## 3. 実装手順とタスク分解

1. **フェーズ1: コアレンダラーとサービスの改修（P1, P2, P6）**
   - `PdfService.cs`:
     - `LoadDocumentAsync` 時に `HasBinderInkAnnotations` 判定を追加。
     - 解析・注釈復元ループを `Task.Run` に委譲。
   - `PdfiumRenderer.cs`:
     - 不要な白矩形挿入 `SanitizeForRendering` を撤廃し、通常の外部PDFは生のバイト列から直接 PDFium レンダリングを行うように改修。
     - 自前注釈（`PdfBinderInkAnnotation`）が含まれる場合のみ注釈除去を適用。
     - `ExtractInteractiveDataAsync` も直接バイト列から高速抽出。
2. **フェーズ2: ViewModelおよびViewの改修（P5, B1, B2）**
   - `DetailEditorViewModel.cs`:
     - `MaxRenderDimension = 8192` を維持。
     - `DpiScale` プロパティを追加し、`CalculateRenderDimensions` に乗算。
   - `DetailEditorView.xaml.cs`:
     - `Loaded` および `DpiChanged` で ViewModel の `DpiScale` を更新するハンドラを実装。
   - `MainViewModel.cs`:
     - `OnActiveSessionChanged` と `OnDocumentChanged` での `InitializeDocument` 重複呼び出しを解消。
     - `UpdatePageThumbnailAsync` の `bool` 成功判定導入と、成功時のみの `IsThumbnailDirty = false` 設定。
3. **フェーズ3: プロセス制御とビルド設定の改修（P7, B3）**
   - `SingleInstanceManager.cs`:
     - `TryAcquireOwnership` における既存 `_mutex` の明示的 Dispose とクリーンアップ処理を追加。
   - `build.ps1`:
     - `-p:EnableCompressionInSingleFile=false` に変更。
4. **フェーズ4: 単体テストの追加と全件検証**
   - `PdfiumRendererTests`: 白矩形挿入なしでの高速直接レンダリング動作検証、および自前注釈存在時の除去検証。
   - `DetailEditorViewModelTests`: `DpiScale` 反映時のレンダリング寸法計算検証。
   - `SingleInstanceManagerTests`: `TryAcquireOwnership` 再試行時の既存ミューテックス破棄検証。
   - `dotnet test`（全テスト 100% PASS）および `dotnet build` の検証。

---

## 4. 品質基準と完了条件
- [ ] 既存の単体テストスイートがすべて PASS すること。
- [ ] 新規追加・更新した単体テスト（DPI計算、サムネイル中断、ミューテックス再取得、直接レンダリング）がすべて PASS すること。
- [ ] `dotnet build` が警告・エラー 0 件で成功すること。
- [ ] ドキュメント（`docs/basic_design.md`, `README.md`, `docs/PROJECT.md`, `CHANGELOG.md`）の同期更新。
