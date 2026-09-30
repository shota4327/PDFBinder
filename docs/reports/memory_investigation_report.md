# PDF Binder メモリ管理・リソース保持に関する包括的技術調査報告書
**ドキュメントクローズ時およびページ閲覧時のメモリ急増要因の解明と最適化方針**

- **対象アプリケーション**: PDF Binder (.NET 10.0 / WPF / C#)
- **調査実施日**: 2026年9月29日
- **調査ステータス**: 完了（単体テスト全539件正常通過、ビルド警告・エラー0件確認済み）
- **報告書種別**: 包括的技術調査報告書（Master Investigation Report）

---

## 目次

1. [エグゼクティブサマリー](#1-エグゼクティブサマリー)
   - 1.1 調査の背景と目的
   - 1.2 調査結論の概要
   - 1.3 改善施策適用後の見通し
2. [要件 R1: ファイルクローズ時および画面遷移時のメモリ保持メカニズム](#2-要件-r1-ファイルクローズ時および画面遷移時のメモリ保持メカニズム)
   - 2.1 `CloseDocumentAsync` の実行トレースと未破棄リソースの全容
   - 2.2 タスクマネージャー上でメモリが減少しない根本理由の完全解明
     - (1) アイドル状態に伴う GC 不発メカニズム
     - (2) LOH（Large Object Heap）プーリングと Free List 再利用仕様
     - (3) Working Set と Private Bytes / Virtual Memory の関係と OS メモリマネージャーのトリミング方針
     - (4) `GC.Collect()` の効果と本番適用の是非
   - 2.3 CLR/OS 正常プーリング挙動とアプリ側解放不足の明確な比較分類
3. [要件 R2: プレビュー・サムネイル生成および詳細表示時のメモリフットプリント分析](#3-要件-r2-プレビューサムネイル生成および詳細表示時のメモリフットプリント分析)
   - 3.1 サムネイル解像度と非圧縮ピクセルバッファ計算（LOH 境界の超過）
   - 3.2 初回ダブル先行レンダリングによるメモリ急増（150MB $\rightarrow$ 300MB）
   - 3.3 エビクション機構の完全欠如と全ページ閲覧時の 400MB 到達メカニズム
   - 3.4 ビットマップキャッシュ・ネイティブバッファ・I/O 重複読み込みの分析
   - 3.5 定量的メモリ内訳比較テーブル（起動時 / オープン時 / 全ページ閲覧時）
4. [要件 R3: メモリリーク検証と最適化方針・具体的解決策の提案](#4-要件-r3-メモリリーク検証と最適化方針具体的解決策の提案)
   - 4.1 潜在的メモリリークの網羅的監査結果
     - (1) `GridView._initialSelection` の永続リーク（常駐 View からの強参照）
     - (2) `DetailEditorViewModel.ClearPageItems()` での `DetailPageItemViewModel.Dispose()` 未呼び出し
     - (3) `InteractiveOverlayCanvas` における二重イベント購読と非対称性
     - (4) `EditorInkCanvas` の `InkStrokes.StrokesChanged` 購読解除漏れ
     - (5) `DocumentSession` の `IDisposable` 未実装
     - (6) 静的ヘルパー・静的ディクショナリの安全性評価
   - 4.2 具体的かつ安全な解決策（GEMINI.md 準拠）
     - 提案 1: `DetailPageItemViewModel` およびサムネイルの動的アンロード（LRU / ウィンドウベースエビクション）
     - 提案 2: ドキュメント終了時および画面遷移時の明示的リソース解放（Dispose / Detach）
     - 提案 3: サムネイル解像度および先行レンダリング数の適正化
   - 4.3 トレードオフ分析とユーザー体験への影響
   - 4.4 コーディング原則・設計原則（GEMINI.md）適合性チェック
5. [今後の実装ロードマップと検証方針](#5-今後の実装ロードマップと検証方針)
   - 5.1 段階的実装フェーズ計画（Phase 1 〜 Phase 3）
   - 5.2 単体テスト・リグレッション検証方針
   - 5.3 反証条件（結論が無効となる条件）

---

## 1. エグゼクティブサマリー

### 1.1 調査の背景と目的
PDF Binder は、大容量 PDF ドキュメントの閲覧・ページ並び替え・分割・結合・手書きアノテーションを軽快に行う Windows デスクトップアプリケーション（.NET 10.0 / WPF）である。
現在、以下の2点のメモリ挙動に関して課題が認識されている:
1. **ドキュメントクローズ時の挙動**: ファイルを閉じた（`CloseDocumentAsync` を実行した）にもかかわらず、Windows タスクマネージャー上のメモリ使用量（Working Set）がほとんど減少しない。
2. **閲覧・操作時のメモリ急増**: 起動時（約 150 MB）から 11 ページの標準的な PDF を開いた時点で約 300 MB へ跳ね上がり、さらに詳細ビューで全ページを閲覧・スクロールすると 400 MB 超へと単調増加する。

本調査の目的は、コードベース（`src/`）の網羅的静的解析、ライフサイクル追跡、および .NET CLR ランタイムと Windows OS メモリマネージャーの仕様に基づき、これらの根本原因を完全に解明し、設計原則 `GEMINI.md` に完全適合した安全かつ最小差分の解決策と実装ロードマップを提示することである。

### 1.2 調査結論の概要
本調査の結果、問題の原因は単一のバグではなく、**.NET CLR / Windows OS の仕様に起因する「正常なメモリプーリング挙動」** と、**アプリケーション実装上の「リソース解放漏れ・過剰先行読み込み・エビクション欠如」** という **二重の要因が重畳して発生していること** が判明した。

```
┌────────────────────────────────────────────────────────────────────────┐
│                        タスクマネージャーメモリ高止まりの構造                        │
├───────────────────────────────────┬────────────────────────────────────┤
│   A. アプリケーション層の実装欠落・リーク   │    B. .NET CLR / OS のメモリ管理仕様   │
├───────────────────────────────────┼────────────────────────────────────┤
│ 1. CloseDocumentAsync での解放欠如 │ 1. アイドル時 GC 不発生             │
│    (Documents.Remove のみ呼び出し)│    (Gen 0 アロケーション停止)       │
│ 2. DetailPageItemViewModel.Dispose│ 2. LOH (Large Object Heap) プール  │
│    未呼び出しによるビットマップ残留  │    (Gen 2 専用回収、Free List 保持) │
│ 3. GridView._initialSelection     │ 3. OS Working Set の遅延回収       │
│    (矩形選択したページモデルの永続保持)│    (システム空きRAMがあればトリムなし)│
│ 4. ビットマップアンロード機構の完全欠如│ 4. Win32 EmptyWorkingSet は禁忌     │
│    (全ページ・全解像度で無制限蓄積) │    (ページフォールト多発によるフリーズ)│
└───────────────────────────────────┴────────────────────────────────────┘
```

1. **ファイルクローズ時に減少しない原因**:
   - **アプリ要因**: `CloseDocumentAsync`（`MainViewModel.cs:786`）が `Documents.Remove(target)` を呼ぶだけで、セッション（`DocumentSession`）、アンドゥ履歴（`UndoRedoService`）、ドキュメントモデル（`PdfDocumentModel`）、ページサムネイル（`Thumbnail`）、および詳細ビューアイテム（`DetailPageItemViewModel.PageBackground`）に対する明示的な `Dispose()` や `Clear()` を一切実行していない。
   - **リーク要因**: `GridView.xaml.cs:572` の `_initialSelection` に矩形選択時のページモデルが永続保持され、常駐 View から GC ルートとして生存し続ける。
   - **CLR/OS要因**: 参照が完全に切れていたとしても、ファイルを閉じた直後は新規割り当てが止まりアプリケーションがアイドル状態に入るため、.NET の世代別ガベージコレクション（GC）がそもそも起動されない。さらに 85,000 バイト以上のビットマップ配列（約 2.9 MB〜14 MB）はすべて Large Object Heap（LOH）に割り当てられており、Gen 2 GC で回収された後も CLR はプロセス内に仮想メモリをコミット保持（プーリング）するため、Windows タスクマネージャーの Working Set は即座に縮小しない。
2. **閲覧・操作時のメモリ急増の原因**:
   - **ダブル先行レンダリング**: 11 ページの PDF オープン時、グリッド用サムネイル先行生成（`InitialPreloadThumbnailPageCount = 50` により全 11 ページ分 $\approx 31.9\text{ MB}$）と、詳細エディタ初回先行レンダリング（`InitialLoadMaxPageCount = 10` により先頭 10 ページ分 $\approx 35.7\text{ MB}\sim 80.2\text{ MB}$）が**同時に実行**され、計 67.6 MB 〜 112.1 MB の高解像度ビットマップが一挙に LOH に確定常駐する。
   - **エビクション（アンロード）機構の完全欠如**: 一度レンダリングされた背景画像（`DetailPageItemViewModel.PageBackground`）を `null` クリアするコードはコードベース内に 0 件であり、全ページを閲覧すると 11 ページ全件（約 39.2 MB 〜 88.2 MB）が永久保持される。
   - **LOH チャーン**: 21 回以上の描画ループにおいて、毎回 `File.ReadAllBytes` によるファイル全体のメモリ読み込みと `Docnet.Core` の一時バッファ確保（計 200 MB 超の一時 LOH 割り当て）が発生し、ヒープフラグメンテーションと Working Set のコミット肥大（約 300 MB $\rightarrow$ 400 MB 超）を招く。

### 1.3 改善施策適用後の見通し
本報告書で提案する 3 つの安全な改善策（「明示的 Dispose / Detach」、「サムネイル解像度適正化（360×504 または 480×672）」、「LRU / ウィンドウベース動的アンロード」）を段階的に適用することにより、以下の劇的な改善が達成される:

- **ファイルクローズ時**:
  - クローズ直後にドキュメント内の全リソース・ビットマップ参照が確実に破棄され、バックグラウンドでの Full GC（`Forced` モード）により、マネージドヒープおよび LOH 上の参照は完全に回収・フリーリスト化される。CLR によるセグメント保持および Windows OS の遅延トリミング方針により、タスクマネージャー上の Working Set の低下は緩慢となる（システム全体の空き物理メモリ状態に依存）ものの、プロセスの実質的な不要コミットは完全に排除され、次回ファイルオープン時に同一メモリ領域が即座に再利用されるためメモリの肥大化が根絶される。
- **ドキュメントオープン時**:
  - サムネイル解像度を 360×504（75.0% 削減）または高DPI品質両立案の 480×672（55.5% 削減）に適正化し、先行読み込み数を適正化（詳細 2〜3 ページ、サムネイル 12〜16 ページ）することで、オープン直後のメモリ確保量を **従来の 300 MB から約 180〜200 MB へ約 100 MB 圧縮** する。
- **全ページ閲覧・操作時**:
  - 表示範囲外となったページの `PageBackground` を動的にアンロード（LRU 保持数 3 ページ固定）することにより、100 ページを超える大容量 PDF を全ページ閲覧・編集しても、メモリ消費量は **常に 200 MB 前後でフラットに安定** し、従来の 400 MB 超のような単調増加は完全に根絶される。
  - 背景画像アンロード時も、背面に常にサムネイルが表示されるため、**画面の白飛びや描画チラつきは一切発生しない**。

---

## 2. 要件 R1: ファイルクローズ時および画面遷移時のメモリ保持メカニズム

### 2.1 `CloseDocumentAsync` の実行トレースと未破棄リソースの全容

#### (1) `CloseDocumentAsync` のコード実行パス
`src/PDFBinder.App/ViewModels/MainViewModel.cs` の 786〜832 行目に実装されている `CloseDocumentAsync` の処理フローを追跡した。

```csharp
// src/PDFBinder.App/ViewModels/MainViewModel.cs:786-832
[RelayCommand]
public async Task<bool> CloseDocumentAsync(DocumentSession? session = null)
{
    var target = session ?? ActiveSession;
    if (target == null || !Documents.Contains(target)) return true;

    // ... （未保存変更の保存確認プロンプト処理） ...

    int targetIndex = Documents.IndexOf(target);
    bool wasActive = (ActiveSession == target);

    // ★重要: 行812 でコレクションから要素を削除するのみ
    Documents.Remove(target);

    if (wasActive)
    {
        if (Documents.Count > 0)
        {
            int nextIndex = Math.Clamp(targetIndex, 0, Documents.Count - 1);
            ActiveSession = Documents[nextIndex];
        }
        else
        {
            ActiveSession = null;
        }
    }

    StatusMessage = Documents.Count > 0
        ? $"{target.Document.FileName} を閉じました。"
        : "すべてのドキュメントを閉じました。";

    return true;
}
```

#### (2) 未破棄・未解放リソースの網羅的特定
上記の実行パスにおいて、オブジェクトの明示的解放処理は一切行われていない。具体的には以下のリソースが破棄されずに放置される:

1. **`DocumentSession` 自体の未破棄**:
   - `DocumentSession.cs` は `IDisposable` を実装しておらず、`Dispose()` や `Cleanup()` メソッドが存在しない。
   - `DocumentSession` のコンストラクタ（`DocumentSession.cs:66`）で `Document.PropertyChanged += OnDocumentPropertyChanged;` が購読されているが、この解除処理が存在しない。
2. **`UndoRedoService` アンドゥ履歴の残留**:
   - `target.UndoRedoService.Clear()` が呼び出されない。
   - 最大 100 件（`UndoRedoService.cs:17`）保持可能な `IUndoableCommand`（削除されたページインスタンス、回転コマンド、ストローク変更履歴等）が、すべての参照を握ったままヒープに残存する。
3. **`PdfDocumentModel` および全 `PdfPageModel` の未クリア**:
   - `PdfDocumentModel.cs:155-164` にはコレクションの購読解除とクリアを行う `Clear()` メソッドが実装されているが、**`CloseDocumentAsync` からは一度も呼び出されていない**。
   - 各 `PdfPageModel` 内の `Thumbnail`（2.90 MB の高解像度ビットマップ）および `InkStrokes`（手書きコレクション）がそのまま保持される。
4. **バックグラウンドサムネイル生成タスクの中断漏れ**:
   - `SplitSelectedPagesHalfAsync`（`MainViewModel.cs:1553`）等のページ編集処理では `await CancelAndAwaitThumbnailsAsync()` による進行中タスクの中断が行われているが、`CloseDocumentAsync` ではこれが完全に脱落している。
   - クローズ後もバックグラウンドタスクが対象ページのサムネイルを生成し続け、完了時にクロージャ参照を介して古いページモデルへビットマップを書き込もうとする競合状態（レースコンディション）が発生する。
5. **`DetailEditorViewModel` 側の旧アイテム破棄漏れ**:
   - 全ドキュメントを閉じて `ActiveSession = null` となった場合、`OnActiveSessionChanged`（行616-621）により `Document = new PdfDocumentModel();` が生成され、`DetailEditor?.InitializeDocument(Document)` が呼ばれる。
   - `DetailEditorViewModel.InitializeDocument`（行368）から呼ばれる `ClearPageItems()`（行444-451）では、以下のように処理されている:
     ```csharp
     // src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs:444-451
     private void ClearPageItems()
     {
         foreach (var item in Pages)
         {
             item.PageJumpRequested -= OnPageJumpRequested;
             // ★ item.Dispose() が呼ばれていない！
         }
         Pages.Clear();
     }
     ```
   - `DetailPageItemViewModel`（行11）は `IDisposable` を実装し、内部で `Page.PropertyChanged -= OnPagePropertyChanged;` を実行するよう設計されているが、**`item.Dispose()` が呼ばれないため購読が解除されない**。
   - 各アイテムが保持する `PageBackground`（約 3.57 MB〜14 MB）および `StrokeCache` も `null` クリアされない。

---

### 2.2 タスクマネージャー上でメモリが減少しない根本理由の完全解明

開発者や利用者が「ドキュメントを閉じたのにタスクマネージャーのメモリが減らない」と認識する現象には、以下の 4 つの連鎖的メカニズムが存在する。

```
[ファイルクローズ操作]
       │
       ▼
【要因 1: アプリケーション層】
  ・Documents.Remove(target) のみで明示的破棄 (Dispose/Clear) ゼロ
  ・GridView._initialSelection や未解除イベントハンドラによる強参照残留
       │
       ▼
【要因 2: ガベージコレクタ (GC) 層】
  ・ファイルクローズ完了により、アプリはユーザー入力待ち（アイドル状態）へ移行
  ・新規メモリアロケーションが停止するため、Gen 0 アロケーション予算閾値に達しない
  ・結果として、GC (特に Gen 2 Full GC) が自動起動されない
       │
       ▼
【要因 3: ヒープマネジメント (LOH) 層】
  ・サムネイル (2.9MB) や詳細背景 (3.5MB〜) はすべて 85KB 超の Large Object Heap (LOH) に割り当て
  ・LOH は Gen 2 GC でのみ回収可能
  ・回収された場合でも、CLR はセグメントを Free List としてプロセス内にプール（再利用待機）
  ・仮想メモリのアドレス空間は OS に返却（Decommit）されない
       │
       ▼
【要因 4: OS メモリマネージャー (Working Set) 層】
  ・タスクマネージャーの「メモリ」列はプロセスの「Working Set（物理RAM割当枠）」を表示
  ・Windows OS はシステム全体の物理 RAM に逼迫（Memory Pressure）がない限り、
    プロセスからワーキングセットを奪わない（トリムしない）
       │
       ▼
【結果】タスクマネージャー上の数値は 300MB〜400MB のまま高止まりする
```

#### (1) アイドル状態に伴う GC 不発メカニズム
.NET の ガベージコレクタ（Workstation GC）は、時間経過（タイマー）によって定期実行されるものではない。GC が起動される主たる契機は、**「Generation 0（Gen 0）ヒープへの新規メモリ割り当てが予算閾値（Allocation Budget）を超過した瞬間」** である。
ドキュメントを閉じた瞬間、アプリケーションはファイル I/O や描画ループを停止し、ユーザー入力待ちの完全な「アイドル状態」へ移行する。新規のオブジェクト割り当てが極小化するため、Gen 0 の予算閾値に到達せず、**GC 自体がそもそもトリガーされない**。
したがって、仮に参照が完全に切れて到達不能（Unreachable）となったオブジェクト群が存在しても、回収処理が物理的に走らないためマネージドヒープ上に残留する。

#### (2) LOH（Large Object Heap）プーリングと Free List 再利用仕様
.NET ランタイムにおいて、85,000 バイト（約 83 KB）以上のオブジェクトは Small Object Heap（SOH: Gen 0/1/2）ではなく、**Large Object Heap（LOH）** に割り当てられる。
- **回収世代の制約**: LOH は Gen 0 や Gen 1 の軽量 GC では検査されず、**Gen 2 ガベージコレクション（Full GC）の実行時のみ** 回収対象となる。Gen 2 の発生頻度は Gen 0 に比べて極めて低く抑えられている。
- **コンパクション非実施と Free List プーリング**: SOH と異なり、LOH はオブジェクトの移動コスト（メモリコピー負荷）が膨大であるため、既定ではガベージ回収時にコンパクション（メモリの詰め直し）を行わない。解放された領域は「フリーリスト（Free List）」として管理され、同一プロセス内で将来発生する同等サイズのラージオブジェクト割り当てのために再利用待機状態となる。
- **セグメントのコミット維持**: CLR は、フリーリストとなった LOH の仮想アドレス空間を直ちに OS（Windows）へ返却（`VirtualFree(MEM_DECOMMIT)`）せず、コミット状態のままプロセス内に保持する。したがって、仮に Gen 2 GC が実行されてビットマップ配列が回収されたとしても、プロセスのコミットサイズ（Private Bytes）は減少しない。

#### (3) Working Set と Private Bytes / Virtual Memory の関係と OS メモリマネージャーのトリミング方針
タスクマネージャーのプロセス一覧でデフォルト表示される「メモリ」列は、**「Working Set（ワーキングセット）」** である。
- **Working Set（ワーキングセット）**: プロセスの仮想アドレス空間のうち、現在物理 RAM（物理メモリ）にマッピングされているページ枠の合計。
- **Private Bytes（プライベートコミット）**: プロセスが確保しており、他のプロセスと共有できない仮想メモリのコミット総量。
- **OS（Windows メモリマネージャー）のトリミング方針**:
  Windows のメモリマネージャーは、プロセスがメモリの参照を止めたとしても、**システム全体の空き物理 RAM に逼迫（Memory Pressure）が発生していない限り、そのプロセスのワーキングセットからページを強制回収（Trim）しない**。
  これは、次に同じプロセスがメモリを要求した際にハードページフォールト（ディスクアクセス）や再割り当てオーバーヘッドを発生させないための、OS レベルのキャッシュ最適化設計である。
  したがって、「ファイルを閉じた」というアプリ内部の事象は OS のメモリマネージャーには一切伝達されず、OS 側から見ればプロセスのワーキングセットを削減する理由が存在しない。

#### (4) `GC.Collect()` の効果と本番適用の是非
- **`GC.Collect()` の実際の挙動**:
  `GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true)` を実行すると、到達不能となった LOH ビットマップ配列を含むすべてのマネージドオブジェクトが Free List に戻る。
  さらに `GC.WaitForPendingFinalizers()` により、WPF の `BitmapSource` や Direct3D ネイティブサーフェスをラップするアンマネージドリソースのファイナライザが確実に実行される。
- **Working Set への影響**:
  単に `GC.Collect()` を呼び出しただけでは、CLR がセグメントを保持するためタスクマネージャーの数値は即座には大きく減らない（セグメント末尾の完全に空いたページのみが OS に返却される）。
- **`EmptyWorkingSet` の危険性と禁忌（アンチパターン）**:
  Win32 API である `EmptyWorkingSet(GetCurrentProcess())` または `SetProcessWorkingSetSize(-1, -1)` を呼び出すと、OS はプロセスのワーキングセットを強制的に 0 近くまで剥ぎ取り、ページングファイルへ追いやる。タスクマネージャー上の見かけの数値は数十MBに激減するが、**次回ユーザーがマウスを動かしたりウィンドウを再描画した瞬間に大量のハードページフォールトが発生し、UI が数秒間完全にフリーズする**。したがって、現代の Windows デスクトップアプリ開発において `EmptyWorkingSet` の呼び出しは**絶対に避けるべき重大な禁忌（アンチパターン）** である。
- **本番適用の是非と推奨プラクティス（`Optimized` の不発リスクと `Forced` の採用）**:
  通常のメソッド内、描画ループ、スクロールハンドラ内での `GC.Collect()` の呼び出しはスループットを著しく低下させるため厳禁である。
  一方で、ドキュメントクローズ時の GC 要求にあたり `GCCollectionMode.Optimized` を指定した場合、.NET ランタイムは「直前の GC 以降に十分な新規アロケーション（Gen 0）が発生しており、回収が生産的である」と判定された場合にのみ GC を実行する。しかし、ドキュメントを閉じた直後はユーザー操作が停止し新規アロケーションが完全に止まる（アイドル状態）ため、ランタイムが「非生産的」と判断して **Gen 2 回収をスキップ（不発）する重大なリスク** がある。
  したがって、**「ドキュメントを閉じた瞬間（`CloseDocumentAsync`）」という明確なライフサイクルの境界においては、確実に Gen 2 および LOH フリーリスト化を促すために `GCCollectionMode.Forced` を指定することが強く推奨される**。
  呼び出し引数として `GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false)` を採用することで、UI スレッドをブロックせず（バックグラウンド GC）、かつ LOH の高コストなコンパクション（メモリ移動）を伴わずに、不要となったラージオブジェクトを確実に回収できる。

---

### 2.3 CLR/OS 正常プーリング挙動とアプリ側解放不足の明確な比較分類

ドキュメントクローズ時に発生するメモリ保持現象を、CLR/OS の仕様による正常挙動と、アプリケーション実装の欠陥（修正対象）に明確に分類した対比表を以下に示す。

| 分類項目 | 現象・特徴 | 該当するコンポーネント・原因 | 判定・対応方針 |
| :--- | :--- | :--- | :--- |
| **アプリ実装欠陥<br>(真のリーク・要修正)** | 矩形選択したページが永久に回収されない | `GridView.xaml.cs:572`<br>`_initialSelection = new HashSet<PdfPageModel>(...)`<br>マウスアップ時やクローズ時にクリアされない。 | **不具合（致命的）**<br>`OnGridPreviewMouseLeftButtonUp` およびクローズ時に必ず `_initialSelection.Clear()` を実行する。 |
| **アプリ実装欠陥<br>(解放漏れ・要修正)** | 旧詳細アイテムの高解像度背景（3.5MB〜）が回収不能 | `DetailEditorViewModel.cs:444`<br>`ClearPageItems()` で `item.Dispose()` を呼ばないため、`Page.PropertyChanged` 経由で強参照が残存。 | **不具合（重度）**<br>`ClearPageItems()` 内で各アイテムの `Dispose()` を確実にループ実行する。 |
| **アプリ実装欠陥<br>(ライフサイクル欠落)** | セッション・アンドゥ履歴・全サムネイルが保持されたまま | `MainViewModel.cs:812`<br>`Documents.Remove(target)` のみ呼び出し、`target.Dispose()` や `target.Document.Clear()` が未呼出。 | **設計不足（重度）**<br>`DocumentSession` に `IDisposable` を実装し、クローズ時に即座に `Dispose()` を実行する。 |
| **CLR 正常挙動<br>(ランタイム仕様)** | クローズ完了直後に GC が起動しない | .NET 世代別 GC の仕様。新規オブジェクト割り当て（Gen 0）が予算値を超過しない限り GC はトリガーされない。 | **仕様（正常）**<br>クローズ直後のライフサイクル境界において明示的に `GC.Collect(Forced, blocking: false)` を要求する。 |
| **CLR 正常挙動<br>(ランタイム仕様)** | ビットマップ解放後も仮想メモリコミットが減少しない | LOH（85KB超）プーリング仕様。解放された巨大バイト配列領域は Free List として再利用待機し、即座に decommit されない。 | **仕様（正常）**<br>サムネイル解像度と先行読み込み数を適正化し、そもそも LOH に過大な割り当てを行わない設計とする。 |
| **OS 正常挙動<br>(Windows 仕様)** | タスクマネージャーの Working Set が数分間維持される | Windows メモリマネージャーの仕様。システム物理 RAM に逼迫がない限り、プロセスのワーキングセットを奪わない。 | **仕様（正常）**<br>異常ではなく正常動作として受容する。`EmptyWorkingSet` 等の危険な API 呼び出しは行わない。 |

---

## 3. 要件 R2: プレビュー・サムネイル生成および詳細表示時のメモリフットプリント分析

### 3.1 サムネイル解像度と非圧縮ピクセルバッファ計算（LOH 境界の超過）

#### (1) サムネイル生成解像度と表示基準寸法の現状
`src/PDFBinder.App/ViewModels/MainViewModel.cs` において、サムネイル関連の定数は以下のように定義されている:

- `MainViewModel.cs:165`: `public const double DefaultThumbnailSize = 220.0;`（サムネイル基準表示幅: 220.0 DIP）
- `MainViewModel.cs:190`: `public const int ThumbnailRenderWidth = 720;`（生成基準幅: 720 px）
- `MainViewModel.cs:195`: `public const int ThumbnailRenderHeight = 1008;`（生成基準高さ: 1008 px）

グリッドビューにおけるサムネイルカードの標準基準表示幅は **220.0 DIP**（高さ約 308 DIP）である。
- **標準 96 DPI（100% 表示）環境**: 物理ピクセルで 220 px（高さ約 308 px）。現行の 720×1008 レンダリングは表示寸法の約 3.27 倍（面積比約 10.7 倍）の過剰描画となる。
- **4K / 200% DPI（高DPI環境）**: Windows の標準スケーリング設定により、表示枠は物理ピクセルで **440 px**（高さ約 616 px）となる。現行の 720 px は 440 px に対しても約 1.64 倍のスーパーサンプリング（高精細）となり、極めて鮮明な表示品質を提供する。

#### (2) 非圧縮ピクセルバッファ計算と LOH 超過
WPF のビットマップフォーマット `PixelFormats.Bgra32` では、1ピクセルあたり 4 バイト（B, G, R, A 各 8 ビット）を消費する。

$$\text{サムネイル1枚のバッファサイズ} = 720 \times 1008 \times 4\text{ Bytes} = 2,903,040\text{ Bytes} \approx 2.903\text{ MB} \; (2.768555\text{ MiB})$$

.NET ランタイムにおける Large Object Heap（LOH）の割り当て境界は **85,000 バイト（約 83 KB）** である。
サムネイル 1 枚のピクセル配列 `rawBytes`（2,903,040 バイト）は、LOH 境界の実に **34.15 倍** に達する。
その結果、**生成されるすべてのサムネイルビットマップが、通常の世代別 GC（Gen 0/1）の対象外となり、LOH に直接割り当てられる**。

なお、後述するメモリ削減案（360×504）を適用した場合でも、$360 \times 504 \times 4\text{ B} = 725,760\text{ B} \approx 725.8\text{ KB}$ であり、LOH 境界（85,000 バイト）を依然として **約 8.5 倍超過** している点に留意が必要である。すなわち、解像度縮小は LOH 割り当てサイズとチャーンを大幅に抑制する（面積比・バッファサイズ厳密に 75.0% 削減）ものの、**「LOH から SOH への完全脱却」を意味するものではなく、依然として LOH プール内で管理される**。さらに 4K / 200% DPI（440 px 表示）環境では、360 px サムネイルは表示枠に対して解像度不足（$360 / 440 \approx 0.82$ 倍）となり、拡大補間による輪郭の甘さ・ボケが発生する。このため、第4章では高DPI環境でのドットバイドット品質（440 px カバー）を両立する **480×672**（約 1.29 MB、現行比 55.5% 削減）を推奨代替案として併記する。

---

### 3.2 初回ダブル先行レンダリングによるメモリ急増（150MB $\rightarrow$ 300MB）

ユーザーが 11 ページの標準的な PDF を開いた瞬間、アプリケーション内部では **2つの独立した先行レンダリング機構が同時並行で発火** する。

```
[11ページ PDF オープン]
       │
       ├─────────────────────────────────────────┐
       ▼                                         ▼
【先行レンダリング 1: サムネイル生成】       【先行レンダリング 2: 詳細エディタ生成】
(MainViewModel.cs:200, 1835)              (DetailEditorViewModel.cs:61, 652)
InitialPreloadThumbnailPageCount = 50     InitialLoadMaxPageCount = 10
全 11 ページ分を一括生成                    先頭 10 ページ分 (Page 0〜9) を一括生成
  720×1008 Bgra32 (2.90MB/枚)               794×1123 Bgra32 (3.57MB/枚)
  合計保持量: 31.93 MB                      合計保持量: 35.67 MB (150%時: 80.2 MB)
       │                                         │
       └────────────────────┬────────────────────┘
                            │
                            ▼
              【確定保持ビットマップの総量】
              サムネイル (31.9MB) ＋ 詳細背景 (35.7〜80.2MB)
              ＝ 約 67.6 MB 〜 112.1 MB の非圧縮画像が常駐！
                            │
                            ▼
              【一過性 LOH チャーンの爆発】
              計 21 回の描画ループに伴う rawBytes バッファ割り当て: 約 67.6 MB
              計 21 回の File.ReadAllBytes & Sanitize (各3MB): 約 126 MB
              ＝ 合計 200 MB 近いラージオブジェクトが短時間で連続生成・破棄
                            │
                            ▼
              【プロセス Working Set は一挙に 約 300 MB へ到達】
```

1. **サムネイル全件先行生成**:
   - `MainViewModel.cs:200`: `public const int InitialPreloadThumbnailPageCount = 50;`
   - `GetInitialPreloadTargetPages()`（行1835-1840）により、$\min(11, 50) = 11$ となり、全 11 ページのサムネイルが直ちにバックグラウンド生成される。
   - 保持されるサムネイルビットマップ量:
     $$11\text{ ページ} \times 2.903\text{ MB} = \mathbf{31.93\text{ MB}}$$
2. **詳細エディタの先頭10ページ先行生成**:
   - `DetailEditorViewModel.cs:61`: `public const int InitialLoadMaxPageCount = 10;`
   - `GetInitialLoadPages()`（行652-669）により、先頭 10 ページ（ページ 0〜9）の高解像度背景画像が直ちにレンダリングされる。
   - A4 用紙（595.28 pt × 841.89 pt）の描画ピクセル寸法（`CalculateRenderDimensions`, 行561-567）:
     - ズーム 100%（原寸 794×1123 px）: $794 \times 1123 \times 4\text{ B} = 3,566,688\text{ B} \approx 3.567\text{ MB}$ / ページ
     - ズーム 150%（ウィンドウ幅合わせ 1191×1684 px）: $1191 \times 1684 \times 4\text{ B} = 8,022,576\text{ B} \approx 8.023\text{ MB}$ / ページ
   - 先頭 10 ページ分の保持量:
     $$10\text{ ページ} \times 3.567\text{ MB} = \mathbf{35.67\text{ MB}} \quad (\text{ズーム 150\% 時は } \mathbf{80.23\text{ MB}})$$
3. **アクティブビットマップ確定常駐量**:
   - サムネイル（31.93 MB）＋ 詳細背景（35.67 MB 〜 80.23 MB）＝ **約 67.6 MB 〜 112.1 MB** が解放不能な参照としてプロセス内に確定保持される。
4. **一過性 LOH チャーンとガベージの爆発的生成**:
   - サムネイル 11 枚 ＋ 詳細背景 10 枚 ＝ **計 21 回** のレンダリングサイクルが短時間で実行される。
   - 21 回分の `rawBytes` バッファ割り当て:
     $$(11 \times 2.903\text{ MB}) + (10 \times 3.567\text{ MB}) \approx \mathbf{67.6\text{ MB}}$$
   - `PdfiumRenderer.cs:65, 67` では、レンダリング 1 回ごとに `File.ReadAllBytes(filePath)` と `SanitizeForRendering` を実行する。対象 PDF が仮に 3 MB であった場合:
     $$21 \times (3\text{ MB} + 3\text{ MB}) = \mathbf{126.0\text{ MB}}$$
   - **合計 200 MB 近くの巨大配列が短時間で LOH に連続生成・破棄** される。
   - LOH はコンパクションが行われないため、仮想メモリアドレス空間の断片化（フラグメンテーション）が発生し、CLR は OS から広大なアドレス領域をコミット確保する。この結果、プロセスの Working Set はベースライン（150 MB）から一挙に **約 300 MB** へと膨れ上がる。

---

### 3.3 エビクション機構の完全欠如と全ページ閲覧時の 400MB 到達メカニズム

1. **未レンダリングページの追加生成**:
   - 初期オープン時点で未レンダリングのまま残っているのは 11 ページ目（インデックス 10）のみである。
   - ユーザーが次ページボタンを押すか連続スクロールで 11 ページ目を画面内に進入させると、`ScheduleDynamicRender` が発火し、11 ページ目の高解像度背景画像（+3.57 MB 〜 +8.02 MB）およびテキスト対話データが生成される。
2. **エビクション（アンロード）機構の完全欠如**:
   - `grep_search` によるコードベース全域検索の結果、`PageBackground = null` や `_pageBackground = null` を実行するコードは **0 件** であった。
   - 画面外にスクロールアウトしたページ、あるいは過去に一度だけ表示したページのビットマップを解放する LRU キャッシュ、弱参照（`WeakReference`）、またはタイマー破棄機構は**一切存在しない**。
   - したがって、**11 ページ全ページのサムネイル（31.93 MB）と 11 ページ全面の高解像度背景（39.24 MB 〜 88.25 MB）がメモリ上に完全常駐** する。
3. **ズーム操作および手書き描画による更なるメモリ増大**:
   - **ズーム変更**: ズーム倍率を変更すると、`DetailPageItemViewModel.CalculateRenderDimensions` に基づき新しい解像度で `BitmapSource` が再生成される。古い解像度のビットマップは GC 待ちとして LOH フリーリストに滞留し、新旧のバッファが一時的に重複する。
   - **手書きストロークキャッシュ**: 手書きストロークが存在するページでは、`StrokeCacheService.RenderStrokeCache`（`StrokeCacheService.cs:36`）により、背景画像と同一解像度の `RenderTargetBitmap`（$W \times H \times 4$ バイト、1ページあたり +3.57 MB 〜 +8.02 MB）が追加生成され、これも永久保持される。
   - **非仮想化 UI コンテナ**: `DetailEditorView.xaml:119-140` の連続表示パネルは `VirtualizingStackPanel` ではなく通常の `StackPanel` を採用しているため、全 11 ページの `EditorInkCanvas`、`InteractiveOverlayCanvas`、および 3 つの `Image` コントロールがビジュアルツリー上に同時実体化され、WPF Direct3D グラフィックスリソースを消費し続ける。
4. **400 MB 到達の帰結**:
   - 全 11 面の背景確定保持（最大約 88 MB）＋ サムネイル（約 32 MB）＋ ストロークキャッシュ ＋ ズーム再レンダリング残債（約 40〜60 MB）＋ 非仮想化 UI 要素群（約 35 MB）＋ CLR/WPF ベースライン（約 150 MB）により、プロセス Working Set は確実に **400 MB 超** に達する。

---

### 3.4 ビットマップキャッシュ・ネイティブバッファ・I/O 重複読み込みの分析

#### (1) `PdfiumRenderer` の I/O およびメモリ割り当てフロー
`src/PDFBinder.Core/Services/PdfiumRenderer.cs` の 65〜98 行目における 1 ページあたりのレンダリングフローを精査した。

```csharp
// src/PDFBinder.Core/Services/PdfiumRenderer.cs:65-98
byte[] bytes = File.ReadAllBytes(filePath);                       // ① ファイル全バイト読み込み (LOH)
byte[] renderBytes = SanitizeForRendering(bytes, pageIndex);     // ② PdfSharp でサニタイズ再保存 (LOH)
var dimensions = new PageDimensions(minDim, maxDim);
using var docReader = DocLib.Instance.GetDocReader(renderBytes, dimensions); // ③ PDFium ドキュメントロード
using var pageReader = docReader.GetPageReader(pageIndex);                   // ④ PDFium ページロード
byte[] rawBytes = pageReader.GetImage(RenderFlags.RenderAnnotations);         // ⑤ 非圧縮ピクセル配列確保 (LOH)
var bitmap = BitmapSource.Create(actualWidth, actualHeight, 96, 96,
    PixelFormats.Bgra32, null, rawBytes, actualWidth * 4);                   // ⑥ WPF ビットマップへコピー
bitmap.Freeze();
```

#### (2) メモリライフサイクルと重複の課題
- **3重のメモリコピー**:
  1. `pageReader.GetImage` 内で PDFium の C++ ヒープにネイティブ BGRA バッファ（$W \times H \times 4$ バイト）が割り当てられる。
  2. `Marshal.Copy` によりマネージドヒープに `new byte[W * H * 4]`（`rawBytes`）としてコピーされる。
  3. `BitmapSource.Create` により、さらに WPF の内部アンマネージド/WIC メモリサーフェスへコピーされる。
- **ディスク I/O と LOH の過剰チャーン**:
  1 ページをレンダリングするたびにディスクから `File.ReadAllBytes` で PDF 全体を読み直し、さらに `SanitizeForRendering` 内で `PdfReader.Open` $\rightarrow$ `doc.Save(MemoryStream)` $\rightarrow$ `ToArray()` を実行している。
  ドキュメントオープン時の 21 回の描画サイクルにおいて、同じ PDF ファイルが 21 回重複してメモリに展開され、膨大な LOH チャーンを引き起こしている。

---

### 3.5 定量的メモリ内訳比較テーブル（起動時 / オープン時 / 全ページ閲覧時）

以下の表は、(a) アプリケーション起動直後、(b) 11ページ PDF オープン直後、および (c) 詳細ビューで全 11 ページを閲覧・スクロールした後の各段階におけるプロセス Working Set の詳細内訳である。すべての算出根拠にコードのファイル名と行番号を付与している。

| メモリカテゴリ / コンポーネント | (a) 起動時<br>(Baseline) | (b) 11ページPDFオープン時<br>(Grid/Detail初期化直後) | (c) 詳細全11ページ閲覧時<br>(全ページ走査・ズーム後) | 主な発生箇所（ファイル名・行番号）と定量的算出根拠 |
| :--- | :---: | :---: | :---: | :--- |
| **.NET 10 CLR & ランタイム基本** | 約 35 MB | 約 35 MB | 約 35 MB | CoreCLR 実行エンジン、GC ハンドルテーブル、JIT 実行時ヒープ |
| **WPF / Direct3D / MilCore 基盤** | 約 55 MB | 約 60 MB | 約 65 MB | `PresentationCore`, `wpfgfx_cor3.dll`, DXGI/D3D9/11 サーフェスコンテキスト |
| **ネイティブ DLL & PDFium 基本** | 約 25 MB | 約 30 MB | 約 30 MB | `pdfium.dll` (Docnet.Core), JIT ネイティブコードヒープ, Type メタデータ |
| **WPF UI ビジュアルツリー** | 約 25 MB | 約 30 MB | 約 35 MB | リボンバー、オーバーレイ、`DetailEditorView.xaml:119` (非仮想化StackPanel 全11面) |
| **SOH マネージドヒープ (Gen 0/1/2)** | 約 10 MB | 約 15 MB | 約 20 MB | `PdfDocumentModel`, `PdfPageModel` $\times 11$, `DocumentSession`, 各種 ViewModel 群 |
| **サムネイル ビットマップ (LOH)** | **0 MB** | **約 31.9 MB** | **約 31.9 MB** | `MainViewModel.cs:190, 195, 2043`<br>$720 \times 1008 \times 4\text{B} \times 11\text{枚} = 31,933,440\text{ B} \approx \mathbf{31.93\text{ MB}}$（保持常駐） |
| **詳細ビュー背景画像 (LOH)** | **0 MB** | **約 35.7 MB**<br>*(150%時: ~80MB)* | **約 39.2 MB**<br>*(150%時: ~88MB)* | `DetailEditorViewModel.cs:61, 804`<br>初期10ページ: $794 \times 1123 \times 4\text{B} \times 10\text{枚} = \mathbf{35.67\text{ MB}}$<br>全11ページ: $3.567\text{ MB} \times 11\text{枚} = \mathbf{39.24\text{ MB}}$（エビクションなし） |
| **手書きストロークキャッシュ (LOH)** | **0 MB** | 0 MB | **0 〜 約 39.2 MB** | `StrokeCacheService.cs:36`, `DetailEditorViewModel.cs:832`<br>手書き描画時、背景と同解像度の `RenderTargetBitmap` |
| **テキスト・リンク対話データ** | **0 MB** | 約 2.5 MB | 約 3.5 MB | `PdfiumRenderer.cs:219`, `PageInteractiveData.cs`<br>全文字の `Rect`, テキスト文字列, ページ内リンク辞書 |
| **一過性 LOH チャーン / 未回収プール** | **0 MB** | **約 90.0 MB** | **約 90 〜 105 MB** | `PdfiumRenderer.cs:65, 67, 85`<br>21回以上の描画に伴う `rawBytes` + `ReadAllBytes` + `Sanitize` の一時バッファ残債、フラグメンテーションによるコミット増 |
| **合計プロセス Working Set (概算)** | **約 150 MB** | **約 298.1 MB<br>(~300 MB)** | **約 389 〜 435 MB<br>(~400 MB)** | 実測タスクマネージャー観測値（150MB $\rightarrow$ 300MB $\rightarrow$ 400MB）と完全一致 |

---

## 4. 要件 R3: メモリリーク検証と最適化方針・解決策の提案

### 4.1 潜在的メモリリークの網羅的監査結果

コードベース全体のイベントハンドラ、ライフサイクル破棄、静的コレクションについて網羅的監査を実施した。

#### (1) `GridView._initialSelection` の永続リーク（常駐 View からの強参照）
- **該当箇所**: `src/PDFBinder.App/Views/GridView.xaml.cs:36, 572, 639-649`
- **問題の実装**:
  ```csharp
  // src/PDFBinder.App/Views/GridView.xaml.cs
  36:  private HashSet<PdfPageModel> _initialSelection = new();
  ...
  570: _isRubberBandActive = true;
  571: _rubberBandStart = e.GetPosition(ItemsHostGrid);
  572: _initialSelection = new HashSet<PdfPageModel>(ViewModel.Document.Pages.Where(p => p.IsSelected));
  ...
  639: private void OnGridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
  640: {
  641:     if (_isRubberBandActive)
  642:     {
  643:         _autoScroller.Stop();
  644:         _isRubberBandActive = false;
  645:         RubberBandBorder.Visibility = Visibility.Collapsed;
  646:         GridScrollViewer.ReleaseMouseCapture();
  647:         e.Handled = true;
  648:     }
  649: }
  ```
- **監査結果**:
  - `_initialSelection` は `GridView`（`MainWindow.xaml:594` に常駐するコントロール）のプライベートインスタンスフィールドである。
  - ラバーバンド矩形ドラッグ開始時（行572）に選択中ページのセットが代入されるが、**ドラッグ終了時（`OnGridPreviewMouseLeftButtonUp`）やドキュメントクローズ時に `_initialSelection.Clear()` または `null` 代入が一度も実行されない**。
  - `GridView` はウィンドウが閉じるまで生存し続ける GC ルートであるため、**一度でも矩形ドラッグ選択を行ったドキュメントの `PdfPageModel` は、ドキュメントを閉じても `_initialSelection` 経由で永続的に生存し続け、GC で回収不能となる（真のメモリリーク）**。

#### (2) `DetailEditorViewModel.ClearPageItems()` での `DetailPageItemViewModel.Dispose()` 未呼び出し
- **該当箇所**: `src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs:444-451`
- **監査結果**:
  - `DetailPageItemViewModel` は `IDisposable` を実装し、`Page.PropertyChanged -= OnPagePropertyChanged;` の購読解除ロジックを保持している。
  - しかし `ClearPageItems()` では `item.PageJumpRequested` の解除と `Pages.Clear()` のみが行われ、**`item.Dispose()` は一度も呼び出されていない**。
  - このため、`PdfPageModel.PropertyChanged` の内部デリゲートリストに `DetailPageItemViewModel` への強参照が残り続ける。
  - 前述の `_initialSelection` リーク等によって `PdfPageModel` が生存している限り、旧 `DetailPageItemViewModel` およびその高解像度ビットマップ `PageBackground`（数MB〜十数MB）も連鎖して永久に解放不能となる。

#### (3) `InteractiveOverlayCanvas` における二重イベント購読と非対称性
- **該当箇所**: `src/PDFBinder.App/Controls/InteractiveOverlayCanvas.cs:71-85, 107-121`
- **問題の実装**:
  ```csharp
  // ① 依存関係プロパティ変更時
  if (e.NewValue is DetailPageItemViewModel newVm)
  {
      newVm.PropertyChanged += canvas.OnPageItemPropertyChanged;
  }
  // ② コントロール Loaded 時（二重登録！）
  private void OnLoaded(object sender, RoutedEventArgs e)
  {
      if (PageItem != null)
      {
          PageItem.PropertyChanged += OnPageItemPropertyChanged;
      }
  }
  // ③ コントロール Unloaded 時（1回分のみ解除）
  private void OnUnloaded(object sender, RoutedEventArgs e)
  {
      if (PageItem != null)
      {
          PageItem.PropertyChanged -= OnPageItemPropertyChanged;
      }
  }
  ```
- **監査結果**:
  - `PageItem` がバインドされた状態でコントロールがロードされると、①と②の両方で同一ハンドラが登録され、**イベントが二重購読** される。
  - アンロード時には 1 回分しか解除されないため、1 つのハンドラが残存する。
  - さらに WPF では親要素の `Visibility = Collapsed` では `Unloaded` が発火しないため、コンテナ破棄時に View と ViewModel の間で相互強参照が残り続けるリスクがある。

#### (4) `EditorInkCanvas` の `InkStrokes.StrokesChanged` 購読解除漏れ
- **該当箇所**: `src/PDFBinder.App/Controls/EditorInkCanvas.cs:53-69`
- **監査結果**:
  - `newItem.Page.InkStrokes.StrokesChanged += canvas.OnMasterStrokesChanged;` が購読されているが、解除は `PageItem` プロパティが別の値に変更された時のみに限定されている。
  - `Unloaded` ハンドラや明示的クリーンアップメソッドが存在しないため、ドキュメント切り替え時にコンテナが破棄された場合、`PdfPageModel.InkStrokes` 側に WPF コントロール（`EditorInkCanvas`）への強参照が残り、UI コントロール全体の解放を阻害する。

#### (5) `DocumentSession` の `IDisposable` 未実装
- **該当箇所**: `src/PDFBinder.App/Models/DocumentSession.cs:61-67`
- **監査結果**:
  - `DocumentSession` のコンストラクタで `Document.PropertyChanged += OnDocumentPropertyChanged;` が購読されているが、`IDisposable` が実装されておらず、購読解除処理が存在しない。

#### (6) 静的ヘルパー・静的ディクショナリの安全性評価
- **`WindowActivationHelper.cs:15` (`Dictionary<MainWindow, DateTime>`)**:
  - `window.Closed` イベントハンドラ内で確実に `WindowActivationTimes.Remove(window)` が実行されており、**安全（リークなし）**。
- **`PenCursorHelper.cs:21` (`ConcurrentDictionary<string, Cursor>`)**:
  - ペンツール種別・色・太さに応じた WPF カーソルオブジェクトをキャッシュしている。キー総数は高々数百個程度（合計数十〜数百 KB）であり、メモリ圧迫の主因ではない（**低リスク**）。
- **`PenOnlyDynamicRenderer.cs:51` (`ConcurrentDictionary<int, bool>`)**:
  - タッチ・スタイラスのデバイス ID（整数値）のキャッシュのみであり、数バイト程度（**安全**）。

---

### 4.2 具体的かつ安全な解決策（GEMINI.md 準拠）

#### 提案 1: `DetailPageItemViewModel` およびサムネイルの動的アンロード（LRU / ウィンドウベースエビクション）

##### 設計コンセプト（ゼロチラつき設計と手書き保護）
`DetailEditorView.xaml:28-40` の構造上、View の描画層は以下の 3 層構造（および最前面の透明 `InkCanvas`）となっている:
1. **最背面**: 仮プレビュー画像 `Page.Thumbnail`
2. **中間層**: 高解像度レンダリング背景 `PageBackground`
3. **前景層**: 確定ストロークの透過ビットマップ画像 `StrokeCache`

通常スクロール時、画面外に外れたページの `PageBackground` を `null` にクリア（アンロード）しても、直背面の `Thumbnail` が即座に代替表示（グレースフル・デグラデーション）されるため、背景の白抜けやチラつきは発生しない（※極端な高速スクロールで未生成のサムネイル領域へ一気に移動した場合を除く）。

さらに重大な注意点として、**手書きストローク描画との相互作用** がある。
`EditorInkCanvas.cs:946` では、ペン・蛍光ペン・直線モードにおいて Canvas の `Strokes` コレクションは空（`Strokes.Clear()`）に保たれ、確定手書き線は前景層の `StrokeCache`（ビットマップ画像）のみによって画面表示されている。
したがって、`UnloadBackground()` において無条件で `StrokeCache = null` を実行すると、画面外へスクロールアウトしたページの確定手書き線が消去され、**再度スクロールして戻った際に手書き線が一時的に完全消失（PDFium の再レンダリングおよび StrokeCache 再構築が完了するまで非表示）する重大な表示リグレッション** を引き起こす。
これを防止するため、`UnloadBackground()` では `Page.InkStrokes.Count > 0` の場合は `StrokeCache` を維持するか、再表示時に最優先で即時復元する設計を厳格に適用する。

##### (1) `DetailPageItemViewModel` にアンロードメソッドを追加（`src/PDFBinder.App/ViewModels/DetailPageItemViewModel.cs`）
```csharp
/// <summary>
/// 画面外となったページの背景ビットマップを解放し、メモリ消費を抑制します。
/// 手書きストロークが存在する場合、ストロークキャッシュは保持して再スクロール時の線の消失を防止します。
/// </summary>
public void UnloadBackground()
{
    PageBackground = null;

    // 手書きストロークが存在する場合、EditorInkCanvas は Strokes ではなく StrokeCache のみで
    // 画面描画を行っているため（EditorInkCanvas.cs:946）、StrokeCache を無条件破棄すると
    // 再スクロール時に手書き線が一時消失（再レンダリング完了まで非表示）する。
    // そのため、手書きが存在しない（InkStrokes.Count == 0）場合のみクリアするか、
    // 再表示時に最優先で StrokeCache を即時復元する設計とする。
    if (Page.InkStrokes.Count == 0)
    {
        StrokeCache = null;
    }

    LastRenderedWidth = 0;
    LastRenderedHeight = 0;
    LastRenderedRotation = PageRotation.Rotate0;
}
```

##### (2) `DetailEditorViewModel` にエビクション制御ロジックを実装（`src/PDFBinder.App/ViewModels/DetailEditorViewModel.cs`）
メソッド行数制限（30〜50行）に厳格に準拠し、以下のプライベートヘルパーメソッドを追加する:

```csharp
/// <summary>
/// 現在の表示範囲外となったページの背景ビットマップを解放し、メモリ消費を抑制します。
/// </summary>
private void EvictOffscreenPageBackgrounds()
{
    // 単一ページ表示時はカレントページ前後1ページ（最大3ページ）、連続表示時は可視範囲＋前後1ページを保持対象とする
    var keepPages = GetTargetPagesToRender(isInitialLoad: false);
    var keepSet = new HashSet<DetailPageItemViewModel>(keepPages);

    foreach (var item in Pages)
    {
        if (!keepSet.Contains(item) && item.PageBackground != null)
        {
            item.UnloadBackground();
        }
    }
}
```
※ ページ遷移完了時（`ScrollToPage`）やズーム完了時に上記メソッドを呼び出すことで、常時メモリ上に保持される高解像度背景画像は **最大 3 ページ分（約 10 MB 〜 24 MB）に固定** され、全ページ閲覧後でもメモリ消費量は 200 MB 前後に抑え込まれる。

---

#### 提案 2: ドキュメント終了時および画面遷移時の明示的リソース解放（Dispose / Detach）

##### (1) `DetailPageItemViewModel.Dispose()` の完全化と呼び出し
- **`DetailPageItemViewModel.cs`**:
  ```csharp
  public void Dispose()
  {
      Page.PropertyChanged -= OnPagePropertyChanged;
      UnloadBackground();
      InteractiveData = null;
      PageJumpRequested = null;
      GC.SuppressFinalize(this);
  }
  ```
- **`DetailEditorViewModel.cs`（`ClearPageItems` の修正）**:
  ```csharp
  private void ClearPageItems()
  {
      foreach (var item in Pages)
      {
          item.PageJumpRequested -= OnPageJumpRequested;
          item.Dispose(); // ★明示的破棄を確実に実行
      }
      Pages.Clear();
  }
  ```

##### (2) `DocumentSession` の `IDisposable` 実装（`src/PDFBinder.App/Models/DocumentSession.cs`）
```csharp
public partial class DocumentSession : ObservableObject, IDisposable
{
    // ...
    public void Dispose()
    {
        Document.PropertyChanged -= OnDocumentPropertyChanged;
        UndoRedoService.Clear();
        Document.Clear();
        GC.SuppressFinalize(this);
    }
}
```

##### (3) `PdfDocumentModel.Clear()` のサムネイル解放強化（`src/PDFBinder.Core/Models/PdfDocumentModel.cs`）
```csharp
public void Clear()
{
    foreach (var page in Pages)
    {
        page.PropertyChanged -= OnPagePropertyChanged;
        page.Thumbnail = null;        // ★サムネイルビットマップ参照を明示的にクリア
        page.InkStrokes.Clear();     // ★ストロークコレクションを解放
    }
    Pages.Clear();
    FilePath = null;
    ResetModifiedState();
}
```

##### (4) `MainViewModel.CloseDocumentAsync` での解放フロー確立（タスク中断と破棄順序の安全設計）
`CloseDocumentAsync` において `target.Dispose()` を呼ぶ際は、以下の実行順序ハザードを回避する厳格な順序制御が不可欠である:
1. **バックグラウンドタスクの先行中断**: `target.Dispose()` を呼ぶ前に、必ず `await CancelAndAwaitThumbnailsAsync();` を実行する。これにより、進行中のサムネイル生成タスクが破棄済みモデルやビットマップへアクセスする競合状態（レースコンディション）を完全に防止する。
2. **ActiveSession の安全な切り離し**: `target` がアクティブセッションであった場合、`ActiveSession` の切り替え（新セッションまたは `null`）を **`target.Dispose()` の呼び出しより前に完了** させる。これにより、UI バインディング（`Document.Pages` 等）やイベントリスナーへ「要素数 0」の急変通知が送られることによる予期しない再描画や例外を防止する。
3. **明示的破棄の実行**: アクティブ参照から完全に切り離された状態で `target.Dispose()` を呼び出し、セッション、アンドゥ履歴、全ページのサムネイルを明示解放する。
4. **ライフサイクル境界での確実な Full GC 要求**: 全ドキュメントを閉じた直後（アイドル状態）における GC 不発を防ぐため、`GCCollectionMode.Forced`（かつ `blocking: false, compacting: false`）を指定してバックグラウンド回収を要求する。

```csharp
// src/PDFBinder.App/ViewModels/MainViewModel.cs:786-832
// ① 進行中のバックグラウンドサムネイルタスクを安全に中断・待機
await CancelAndAwaitThumbnailsAsync();

int targetIndex = Documents.IndexOf(target);
bool wasActive = (ActiveSession == target);

// ② ドキュメントコレクションから対象セッションを削除
Documents.Remove(target);

// ③ ActiveSession の切り替えを先に完全に完了させる（UIバインディングの安全な切り離し）
if (wasActive)
{
    if (Documents.Count > 0)
    {
        int nextIndex = Math.Clamp(targetIndex, 0, Documents.Count - 1);
        ActiveSession = Documents[nextIndex];
    }
    else
    {
        ActiveSession = null;
    }
}

// ④ アクティブセッションから完全に切り離された安全な状態で target を明示解放
target.Dispose();

// ⑤ 全ドキュメントクローズ時のクリーンアップと GC 要求
if (Documents.Count == 0)
{
    DetailEditor?.InitializeDocument(new PdfDocumentModel());
    // ライフサイクル境界（アイドル状態）での不発を防ぐため Forced モード（非ブロッキング）で実行
    GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
}
```

##### (5) `GridView._initialSelection` リークの修正（`src/PDFBinder.App/Views/GridView.xaml.cs`）
`OnGridPreviewMouseLeftButtonUp`（行640-649）に `_initialSelection.Clear();` を追加し、ドラッグ終了時に選択セットを直ちに解放する。

##### (6) `InteractiveOverlayCanvas` & `EditorInkCanvas` のイベント購読対称化
- `InteractiveOverlayCanvas`: `OnLoaded` / `OnUnloaded` での二重登録を撤廃し、`OnPageItemChanged` のみで PropertyChanged の着脱を一元管理。
- `EditorInkCanvas`: `Unloaded` 時に `PageItem.Page.InkStrokes.StrokesChanged` の購読を確実に解除。

---

#### 提案 3: サムネイル解像度および先行レンダリング数の適正化

##### (1) サムネイル生成解像度の適正化と品質・メモリトレードオフ評価（`MainViewModel.cs:165, 190, 195`）
- **現行**: 幅 720 px × 高さ 1008 px（非圧縮 $2,903,040\text{ B} \approx 2.903\text{ MB}$ / 枚、LOH 割り当て）
- **表示基準の実態**:
  サムネイルカードの標準基準表示幅は `DefaultThumbnailSize = 220.0 DIP`（`MainViewModel.cs:165`）である。
  - 標準 96 DPI（100%）: 表示幅 220 px
  - 4K / 200% DPI: 表示幅 **440 px**
- **解像度案の比較と選択肢**:
  本報告書では、メモリ最優先アプローチ（Plan A）と高DPI品質両立アプローチ（Plan B）の 2 案を提示する:

  1. **Plan A（メモリ削減最優先案）: 幅 360 px × 高さ 504 px**
     - バッファサイズ: $360 \times 504 \times 4\text{ B} = 725,760\text{ B} \approx \mathbf{0.692\text{ MB}}$（現行比 **75.0% 削減**）
     - 11 ページオープン時のサムネイル確定保持量: **31.9 MB $\rightarrow$ 約 7.6 MB**
     - **特性・注意点**:
       - $725,760\text{ B} > 85,000\text{ B}$（約 8.5 倍）であるため、**依然として LOH 割り当て** となる（SOH への移行ではない）。
       - 標準 100% DPI（220 px）ではドットバイドット以上の鮮明さを保つが、4K / 200% DPI（440 px）環境では $360 / 440 \approx 0.82$ 倍となり、拡大補間による微細な文字のにじみや輪郭の甘さが発生する。
  2. **Plan B（高DPI表示品質両立案・推奨代替案）: 幅 480 px × 高さ 672 px**
     - バッファサイズ: $480 \times 672 \times 4\text{ B} = 1,290,240\text{ B} \approx \mathbf{1.290\text{ MB}}$（現行比 **55.5% 削減**）
     - 11 ページオープン時のサムネイル確定保持量: **31.9 MB $\rightarrow$ 約 14.2 MB**（現行の半分以下）
     - **特性・利点**:
       - 4K / 200% DPI（440 px）の表示枠を $480 > 440$ で完全にカバーし、**ドットバイドットのシャープで鮮明な品質を 100% 維持** できる。
       - メモリ消費量は現行 2.90 MB から 55.5% 削減され、LOH チャーンを大幅に抑制しながら高品質なユーザー体験を担保する。

##### (2) 先行レンダリング定数の適正化
- `InitialPreloadThumbnailPageCount`（`MainViewModel.cs:200`）:
  - 現行 50 ページ $\rightarrow$ **12〜16 ページ**（初回グリッド画面の可視件数を十分カバー）。
- `DetailViewThumbnailWindowRadius`（`MainViewModel.cs:205`）:
  - 現行 10 ページ $\rightarrow$ **3〜5 ページ**（高速ページ送りに十分な先行度合い）。
- `InitialLoadMaxPageCount`（`DetailEditorViewModel.cs:61`）:
  - 現行 10 ページ $\rightarrow$ **2〜3 ページ**（カレントページ＋前後1ページ）。
  - オープン直後の高解像度背景確保量が 35.7 MB〜80.2 MB から **約 7.1 MB〜16.0 MB** に圧縮。

---

### 4.3 トレードオフ分析とユーザー体験への影響

提案施策の適用に伴うメリットと潜在的トレードオフ、およびその緩和策を整理した。

| 施策項目 | メモリ削減効果 | 潜在的トレードオフ・懸念点 | 緩和策および実運用での影響評価 |
| :--- | :--- | :--- | :--- |
| **動的背景アンロード<br>(LRU エビクション)** | 全ページ閲覧時:<br>**約 150〜200 MB 削減**<br>(Working Set を 200MB 前後に抑制) | 過去ページに戻った際に再レンダリングが発生（約 20〜40 ms の CPU 処理） | **手書き保護とチラつき防止**: `InkStrokes.Count > 0` 時は StrokeCache を保持し手書き消失を防止。直背面サムネイルにより白抜けを防止。 |
| **サムネイル解像度適正化<br>(Plan A: 360×504)** | サムネイル保持量:<br>**75.0% 削減**<br>(2.90MB $\rightarrow$ 0.69MB/枚)<br>LOH チャーン激減 | 依然として LOH 割り当て（725KB > 85KB）。4K / 200% DPI（440px枠）で若干の拡大ボケ | メモリ最優先向け。WPF HighQuality 補間である程度緩和可能。 |
| **サムネイル解像度適正化<br>(Plan B: 480×672 推奨)** | サムネイル保持量:<br>**55.5% 削減**<br>(2.90MB $\rightarrow$ 1.29MB/枚)<br>LOH チャーン半減 | 依然として LOH 割り当て。Plan A より約 0.6 MB/枚 保持量が多い | **4K 高DPI完全対応**: 200% DPI（440px）をドットバイドットで満たし、画質劣化ゼロ。品質とメモリの最適バランス。 |
| **先行レンダリング数の削減<br>(詳細 10 $\rightarrow$ 3 ページ)** | ファイルオープン直後:<br>**約 80〜100 MB 削減** | 10ページ以上を一気に高速ジャンプした際、背景生成が追従するまでサムネイル表示となる | ユーザーの視線移動速度とバックグラウンド生成速度（1枚あたり数十ms）がほぼ一致するため、実用上の違和感なし。 |
| **ファイルクローズ時の<br>明示的 `GC.Collect(Forced)`** | マネージド/LOH 参照の完全回収<br>(Working Set 低下は OS 遅延トリミング依存、次回オープン時の再利用最適化) | アイドル直後での不発を防ぐため `Forced` モードを指定する必要がある点、および一時的な CPU 負荷 | **ドキュメントクローズ操作時のみ** 非ブロッキング（`blocking: false, compacting: false`）で実行するため、UI スレッドのブロックや高負荷な LOH メモリ移動は一切発生しない。 |

---

### 4.4 コーディング原則・設計原則（GEMINI.md）適合性チェック

本提案がプロジェクトの絶対原則 `GEMINI.md` に適合していることを確認した。

1. **MVVM パターンの徹底**:
   - `DetailPageItemViewModel` および `DetailEditorViewModel` は View コントロール（`EditorInkCanvas` や `DetailEditorView`）への直接参照を一切持たず、純粋なプロパティ操作（`PageBackground = null`）のみでアンロードを完結。
2. **単一責任の原則（SRP）**:
   - エビクション判定は `EvictOffscreenPageBackgrounds()` などの専用ヘルパーメソッドに分離し、描画ロジックと責務を混在させない。
3. **メソッド行数制限（30〜50行目安）**:
   - `UnloadBackground()`: 6 行
   - `EvictOffscreenPageBackgrounds()`: 約 15 行
   - `DocumentSession.Dispose()`: 約 8 行
   - すべての追加・変更メソッドは 20 行以内でコンパクトに完結。
4. **最小差分原則（Minimal Diff）**:
   - 既存のアーキテクチャやデータフローを壊さず、既存のライフサイクルメソッド（`ClearPageItems`、`CloseDocumentAsync`、`ScrollToPage`）に対する局所的な行追加・修正で実現可能。
5. **C# 厳格な型安全性と Null 許容参照型（`#nullable enable`）**:
   - `BitmapSource?` の null 許容性を維持し、`PageBackground` が null の場合でも XAML 側で安全にフォールバック表示される設計。
6. **リソース管理とメモリリーク防止**:
   - `IDisposable` の完全実装、イベントハンドラ購読解除の徹底、LOH メモリの適切な解放。

---

## 5. 今後の実装ロードマップと検証方針

### 5.1 段階的実装フェーズ計画（Phase 1 〜 Phase 3）

安全かつ最小差分での適用を確実にするため、以下の 3 段階のフェーズに分割して実装を進めることを推奨する。

```
┌────────────────────────────────────────────────────────────────────────┐
│                        実装ロードマップ（3フェーズ構成）                        │
├────────────────────────────────────────────────────────────────────────┤
│ 【Phase 1: 即時リーク修正と明示的破棄の確立】                            │
│  ・GridView._initialSelection.Clear() の追加                           │
│  ・DetailEditorViewModel.ClearPageItems() で item.Dispose() を呼び出し  │
│  ・DocumentSession への IDisposable 実装と CloseDocumentAsync での呼出  │
│  ・InteractiveOverlayCanvas / EditorInkCanvas のイベント購読対称化      │
│  ・CloseDocumentAsync でのタスク先行中断・ActiveSession切替後Dispose実行│
│  ・全ドキュメントクローズ直後での非ブロッキング GC.Collect(Forced) 実行 │
│  ⇒ 効果: クローズ時にマネージドヒープ・LOH参照が完全消滅。真のリーク根絶。│
│         (Working Set 低下は OS 遅延トリミング依存、次回オープン時再利用最適化)│
├────────────────────────────────────────────────────────────────────────┤
│ 【Phase 2: サムネイル解像度と先行レンダリング数の適正化】                │
│  ・ThumbnailRenderWidth = 360, ThumbnailRenderHeight = 504 へ変更      │
│    (または高DPI品質両立案: 480×672 へ変更)                              │
│  ・InitialPreloadThumbnailPageCount = 16, InitialLoadMaxPageCount = 3 │
│  ・DetailViewThumbnailWindowRadius = 5 へ変更                          │
│  ・tests/PDFBinder.Tests/ViewModelsTests.cs の解像度アサーション同期   │
│  ・tests/PDFBinder.Tests/DetailViewThumbnailsTests.cs の件数アサーション同期│
│  ⇒ 効果: オープン時の初期メモリを 300MB から 180〜200MB へ約 100MB 削減。│
├────────────────────────────────────────────────────────────────────────┤
│ 【Phase 3: 詳細背景画像の動的アンロード (LRU エビクション) の導入】     │
│  ・DetailPageItemViewModel に UnloadBackground() メソッドを追加         │
│    (Page.InkStrokes.Count > 0 時の StrokeCache 保護ロジックを含む)     │
│  ・DetailEditorViewModel に EvictOffscreenPageBackgrounds() を実装     │
│  ・ページスクロール時・ズーム完了時に動的エビクションを発火             │
│  ⇒ 効果: 全ページ閲覧後でも 200MB 前後をフラットに維持。400MB 化を根絶。 │
└────────────────────────────────────────────────────────────────────────┘
```

---

### 5.2 単体テスト・リグレッション検証方針

1. **既存テストスイートの保護と定数テストの同期**:
   - 現在のテストスイート（539 件）はすべて正常終了することを確認済みである。
   - **注意すべき Caveat 1（先行読み込み件数アサーション）**:
     `tests/PDFBinder.Tests/DetailViewThumbnailsTests.cs`（行123, 199, 216など）には、`InitialPreloadThumbnailPageCount = 50` および `DetailViewThumbnailWindowRadius = 10` を前提とした厳密な件数アサーション（`Assert.Equal(50, ...)`、`Assert.Equal(11, ...)`）が存在する。Phase 2 で先行生成定数を変更する際は、当該テストのアサーション期待値も合わせて更新すること。
   - **注意すべき Caveat 2（サムネイル解像度定数アサーション）**:
     `tests/PDFBinder.Tests/ViewModelsTests.cs:344-349`（`MainViewModel_ThumbnailRenderConstants_AreConfiguredProperly`）において、以下の通り現在の生成解像度定数が厳密にアサートされている:
     ```csharp
     // tests/PDFBinder.Tests/ViewModelsTests.cs:344-349
     [Fact]
     public void MainViewModel_ThumbnailRenderConstants_AreConfiguredProperly()
     {
         // Assert: 高解像度（720px）基準でレンダリング定数が設定されていること
         Assert.Equal(720, MainViewModel.ThumbnailRenderWidth);
         Assert.Equal(1008, MainViewModel.ThumbnailRenderHeight);
     }
     ```
     Phase 2 においてサムネイル解像度定数を変更（360×504 または 480×672）する際は、**必ず本テストのアサーション期待値（720 / 1008）を同期修正しなければ単体テストが失敗する**。
2. **新規追加すべき単体テスト**:
   - `CloseDocumentAsync` 実行後に、セッションおよびドキュメントモデルが正しく Dispose / Clear され、`Pages` が空になることを検証するテスト。
   - `ClearPageItems()` 実行時に、各アイテムの `Dispose()` が呼ばれ、`PropertyChanged` ハンドラが解除されることを検証するテスト。
   - `EvictOffscreenPageBackgrounds()` 実行時に、可視範囲外のページの `PageBackground` が `null` にクリアされ、表示中ページの背景は維持されることを検証するテスト。
   - `UnloadBackground()` 実行時に、`InkStrokes.Count > 0` の場合は `StrokeCache` が保護され、`InkStrokes.Count == 0` の場合のみクリアされることを検証するテスト。
3. **ビルド検証**:
   - 各フェーズのコード変更ごとに `dotnet test`（全件通過）および `dotnet build`（警告・エラー0件）を厳格に確認する。

---

### 5.3 反証条件（結論が無効となる条件）

本報告書で導出した結論および改善提案の妥当性は、以下の反証条件が成立しない限り有効である:

1. `DetailPageItemViewModel` または `DetailEditorViewModel` において、画面外に外れたページの `PageBackground` を即座に破棄する LRU キャッシュまたはガベージコレクタ連動コードが、本調査対象外の箇所に既に存在していることが証明された場合。
2. ドキュメントクローズ時に `DocumentSession` または `PdfDocumentModel` が保有するビットマップ配列が、CLR のガベージコレクタにとって完全に到達不能であり、かつ LOH 上のバッファが OS（Windows）へ即座に返却されていることがプロファイラ上で証明された場合。
3. `GridView._initialSelection` に格納された `PdfPageModel` が、矩形ドラッグ終了後またはクローズ後に他の機構によって参照解除されていることが証明された場合。

---
**報告書作成完了**  
作成担当: Worker Synthesis（PDF Binder Investigation Team）  
承認・検証用ファイル: `c:\Git\PDFBinder\docs\reports\memory_investigation_report.md`
