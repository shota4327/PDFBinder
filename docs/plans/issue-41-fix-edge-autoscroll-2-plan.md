# 画面端スクロール後の手書き時における内側自動スクロール不具合の修正計画（追加改修-2）(Issue #41)

画面端（上端・左端等）までスクロールした状態で手書きを行った際に、WPF内部のフォーカス処理（`BringIntoView`）により画面が内側へ自動スクロール（ジャンプ）してしまう現象を根源から解消します。

## 課題と根本原因の分析

- **現象**: ページを上端や左端までスクロールしきった後、ペンを接地して手書きを開始した瞬間に、内側へ一定距離（余白・パディング分）スクロールしてしまう。
- **根本原因**:
  - WPFの `ScrollViewer` は内部で静的クラスハンドラー（`EventManager.RegisterClassHandler`）を用いて `RequestBringIntoViewEvent` を購読しており、イベント受信時に仮想メソッド `OnRequestBringIntoView` を呼び出して要素全体をビューポート内に引き込もうと自動スクロールを実行します。
  - 前回の修正では `DetailScrollViewer` インスタンスに対して `AddHandler(..., true)` を行いましたが、WPFのイベントルーティング規則上、同一要素においては**静的クラスハンドラーがインスタンスハンドラーよりも先に実行**されます。そのため、インスタンスハンドラーが実行された時点ですでに `ScrollViewer` 内部でスクロール計算と位置変更が完了してしまっていました。

---

## 決定事項（`/grill-me` にて合意済み）

1. **`NoAutoScrollScrollViewer` の導入（根源的遮断）**:
   - `ScrollViewer` を継承した `NoAutoScrollScrollViewer` クラスを新規作成。
   - 仮想メソッド `protected override void OnRequestBringIntoView(RequestBringIntoViewEventArgs e)` をオーバーライドし、`base` を呼び出さずに `e.Handled = true` を設定することで、WPFの内部自動スクロール処理を完全にバイパス・無力化する。
2. **多重防御（Defense in Depth）の実施**:
   - `NoAutoScrollScrollViewer` だけでなく、手書きキャンバス `EditorInkCanvas` および `PagesItemsControl` においても `RequestBringIntoViewEvent` を捕捉して `e.Handled = true` を設定し、イベントバブリングの段階でも安全に遮断する。
3. **明示的ページ移動スクロールの維持**:
   - サムネイルダブルクリック等によるページ移動要求（`ScrollToPageRequested`）は、引き続きコンテナ相対座標による `ScrollToVerticalOffset` の直接呼び出しで行うため、自動機能遮断の影響を受けず正常に動作する。

---

## 提案される変更

### コントロール層 (`PDFBinder.App.Controls`)

#### [NEW] [`NoAutoScrollScrollViewer.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/NoAutoScrollScrollViewer.cs)
- `ScrollViewer` の派生クラス。
- `OnRequestBringIntoView` をオーバーライドして `e.Handled = true` のみを行い、子要素フォーカス時の不要な自動スクロールを完全無効化。

#### [MODIFY] [`EditorInkCanvas.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Controls/EditorInkCanvas.cs)
- コンストラクタにて `RequestBringIntoViewEvent` を捕捉し、`e.Handled = true` を設定（多重防御）。

### ビュー層 (`PDFBinder.App.Views`)

#### [MODIFY] [`DetailEditorView.xaml`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml)
- `<ScrollViewer x:Name="DetailScrollViewer" ...>` を `<controls:NoAutoScrollScrollViewer x:Name="DetailScrollViewer" ...>` に置き換え。

#### [MODIFY] [`DetailEditorView.xaml.cs`](file:///c:/Git/PDFBinder/src/PDFBinder.App/Views/DetailEditorView.xaml.cs)
- `PagesItemsControl` に対しても `RequestBringIntoViewEvent` の `e.Handled = true` を追加（多重防御）。

### テスト層 (`tests/PDFBinder.Tests`)

#### [NEW] [`NoAutoScrollScrollViewerTests.cs`](file:///c:/Git/PDFBinder/tests/PDFBinder.Tests/NoAutoScrollScrollViewerTests.cs)
- `NoAutoScrollScrollViewer` の単体テスト:
  - 子要素から `BringIntoView` が呼ばれた際、スクロールオフセットが変更されず、`e.Handled = true` になることを検証。

---

## 検証計画

### 自動テスト
- `dotnet test`: 既存全102件および新規追加テストがすべて 100% PASS することを確認。
- `dotnet build`: ビルドが警告・エラー 0 件で成功することを確認。

### 手動検証確認項目
- アプリを起動し、詳細エディタを表示。
- ページを上端までスクロールしきった状態（上端の余白が見えない状態）でペンで描画を開始し、画面が内側へ勝手にスクロールしないことを確認。
- 拡大時に左端までスクロールしきった状態でペンで描画を開始し、画面が右（内側）へ勝手にスクロールしないことを確認。
- サムネイル一覧からのページ選択時に、目的のページへ正常に明示的スクロール移動できることを確認。
