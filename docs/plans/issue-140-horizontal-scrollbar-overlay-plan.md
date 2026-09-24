# Issue #140 横スクロールバーの浮遊配置およびステータスバー重複解消 実装計画

## 1. 概要・背景
現在、下部ステータスバーが半透明オーバーレイ（`Grid.Row="2" VerticalAlignment="Bottom" Panel.ZIndex="10"`、高さ約42px）としてコンテンツ領域の最前面に重ね配置されています。
詳細エディタ（`DetailEditorView`）で拡大表示（ズームイン）した際やウィンドウ幅を狭めた際に横スクロールバーが出現しますが、スクロールビューアーが画面最下部まで配置されているため、横スクロールバーがステータスバーの背後に完全に隠れてしまい、操作および視認ができない問題（Issue #140）が発生しています。また、縦スクロールバーの下端もステータスバー右端のズームコントロールと重なっています。

ヒアリング（Grill-me）に基づき、以下の設計方針で実装します：
1. ステータスバーの半透明オーバーレイ配置（用紙が背後を通過できる仕様）はそのまま維持する。
2. スクロールビューアーにカスタムテンプレート（浮遊オーバーレイスタイル）を適用し、横スクロールバーおよび縦スクロールバーをステータスバーの上（42px上）に配置する。
3. 詳細エディタ（`DetailEditorView`）およびグリッドビュー（`GridView`）の双方に適用し、操作性と外観の一貫性を確保する。
4. アプリ全体のダークテーマに合わせた、半透明・細身のモダンスクロールバーデザインを導入する。
5. Shift + マウスホイール操作による横スクロールに対応し、操作性を向上させる。

---

## 2. アーキテクチャおよびレイアウト設計

### 2.1 スクロールビューアー・レイアウト構成
WPF `ScrollViewer` の ControlTemplate をカスタマイズし、2層構造の Grid で構成します：

- **レイヤー0（最背面・全領域）**:
  - `ScrollContentPresenter`: スクロールビューアーの全領域（高さ・幅 100%）を専有。
  - 用紙コンテンツやグリッドカードはステータスバーの背後（最下部）まで自然にスクロール可能（Issue #77の仕様を維持）。
- **レイヤー1（前面・スクロールバー層）**:
  - 2行（Row 0: `*`, Row 1: `42px`）、2列（Col 0: `*`, Col 1: `Auto`）の Grid。
  - **Row 1**: ステータスバー分のスペーサー（Height="42"）。スクロールバーはこの領域には侵入しない。
  - **Row 0, Col 0**: `PART_HorizontalScrollBar` を `VerticalAlignment="Bottom"` で配置。
    - ステータスバーの直上（下端から42pxの位置）に浮遊。
    - 縦スクロールバーが表示されている場合は Col 1 の幅分だけ右端が自動的に収縮し、縦横スクロールバーの衝突を自動回避。
  - **Row 0, Col 1**: `PART_VerticalScrollBar` を `VerticalAlignment="Stretch"` で配置。
    - 下端がステータスバー直上（42px上）で止まるため、右下のズームボタンとの重複を完全に回避。

### 2.2 スクロールバー・ビジュアルデザイン
- ダークテーマパレット（`#424242`, `#4F4F4F`, `#686868`）に調和する角丸サムネイル（Thumb）を定義。
- スクロールバーの幅・高さをスリム化（約10〜12px）し、コンテンツの邪魔にならないモダンな外観とする。
- 不要な上下左右のスクロール矢印ボタンを排した、シンプルで洗練されたフラットデザイン。

### 2.3 操作性向上（Shift + マウスホイール）
- `DetailEditorView.xaml.cs` の `OnScrollViewerPreviewMouseWheel` にて、`Shift` キー押下時のホイール操作を検知し、水平オフセット（`HorizontalOffset`）のスクロールを実行。

---

## 3. 実装ステップ・作業項目

### Step 1: `App.xaml` にモダンスクロールバー＆オーバーレイスクロールビューアースタイルを追加
- `ScrollBarThumbStyle`、`ModernScrollBarStyle`（水平・垂直）の定義。
- `OverlayScrollViewerStyle` の定義（Row 0: `*`, Row 1: `42` の2層グリッド構造）。

### Step 2: `DetailEditorView.xaml` および `GridView.xaml` へのスタイル適用
- `DetailEditorView.xaml` の `NoAutoScrollScrollViewer` に `OverlayScrollViewerStyle` を適用。
- `GridView.xaml` の `GridScrollViewer` に `OverlayScrollViewerStyle` を適用。

### Step 3: `DetailEditorView.xaml.cs` に Shift+ホイール横スクロール処理を追加
- `OnScrollViewerPreviewMouseWheel` にて `ModifierKeys.Shift` 判定を追加。
- 1ノッチあたり適切なスクロール量（例: 48px または delta比例）で `ScrollToHorizontalOffset` を呼出。

### Step 4: ビルドおよび単体テスト実行
- `dotnet build`: 警告・エラー 0 件の確認。
- `dotnet test`: 全単体テストが 100% PASS することを確認。

### Step 5: ドキュメント更新
- `docs/basic_design.md`: スクロールバーの浮遊配置およびShift+ホイール仕様の追記。
- `docs/PROJECT.md`: 機能インベントリのステータス確認・更新。
- `CHANGELOG.md`: エンドユーザー向け新機能・改善点（`## [Unreleased]`）の記述。
- `docs/plans/issue-140-horizontal-scrollbar-overlay-walkthrough.md` の作成。

---

## 4. 検証項目
1. **横スクロールバーの視認性・操作性**:
   - 詳細エディタでズームイン時、ステータスバーの上部に横スクロールバーが表示されること。
   - スクロールバーをマウスドラッグして横スクロールできること。
2. **縦スクロールバーの配置**:
   - 縦スクロールバーの下端がステータスバーの上部で止まり、ステータスバー右側のズームコントロールと重ならないこと。
3. **コンテンツの透過スクロール**:
   - 用紙コンテンツがステータスバーの背後を通過してスクロールできること（透過性の維持）。
4. **Shift + ホイール操作**:
   - 詳細エディタで Shift + マウスホイールを回転させた際に、左右にスムーズにスクロールできること。
5. **既存機能への影響なし**:
   - 全自動テスト（210件以上）がすべてパスすること。
