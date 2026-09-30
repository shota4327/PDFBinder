# 実装計画: 詳細エディタで透過背景PDFの文字が影のように二重表示・ぼやける不具合の修正

- **Issue**: [#194](https://github.com/shota4327/PDFBinder/issues/194)
- **作業ブランチ**: `issue-194-fix-detail-view-text-blur`
- **対象コンポーネント**: `PDFBinder.App` (詳細エディタView / XAML)

---

## 1. 概要と背景
透過背景を持つ外部PDF（WordやPowerPoint等から出力されたPDF）を詳細エディタで閲覧した際、低解像度の仮プレビューサムネイル（`Page.Thumbnail`）と高解像度背景画像（`PageBackground`）が二重に重なって表示され、文字の輪郭に影や滲みが生じてぼやけて表示される不具合を修正します。

### 原因のメカニズム
1. 詳細エディタ（`DetailEditorView.xaml`）において、高解像度画像（`PageBackground`）の読み込み完了前の仮プレビューとして背面にサムネイル画像（`Page.Thumbnail`）を配置している。
2. 外部PDFではPDFsharpの仕様制限（Object Streams非対応）によりレンダリング前の最下層白矩形挿入処理（`SanitizeForRendering`）がフォールバックし、PDFiumから背景透明な画像が返される。
3. 高解像度画像が読み込まれた後も背面の低解像度サムネイルが表示され続けているため、文字が二重に重なって影のように見えていた。
4. なお、用紙の外枠（`Border`）はすでに白色（`Background="White"`）となっているため、サムネイルを非表示にするだけで綺麗な白地の上に高解像度の文字だけが表示される。

---

## 2. 修正内容と設計

### 2.1 `DetailEditorView.xaml` のサムネイル表示制御
`DetailPageItemTemplate` 内の仮サムネイル `Image` 要素にスタイル・トリガーを追加し、高解像度画像（`PageBackground`）が存在する時は非表示にします。

```xml
<!-- 高解像度レンダリング完了前の仮プレビューサムネイル画像 -->
<Image Source="{Binding Page.Thumbnail}" Stretch="Fill"
       RenderOptions.BitmapScalingMode="HighQuality">
    <Image.Style>
        <Style TargetType="Image">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding PageBackground}" Value="{x:Null}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Image.Style>
</Image>
```

- **高解像度画像生成前 (`PageBackground == null`)**:
  - サムネイル画像を表示（`Visibility="Visible"`）し、白飛びを防止。
- **高解像度画像生成後 (`PageBackground != null`)**:
  - サムネイル画像を非表示（`Visibility="Collapsed"`）にし、文字の二重露光（影・ぼやけ）を根本から解消。
  - GPUのオーバードロー負荷もゼロ化。
- **ズーム変更時**:
  - `DetailEditorViewModel` のダブルバッファリングにより、新画像生成完了まで旧 `PageBackground` が維持されるため、切り替え時の白飛びやチラつきは発生しない。
- **動的アンロード時**:
  - 画面外スクロールで `UnloadBackground()` が呼ばれた際は `PageBackground = null` となるため、自動的にサムネイルが再表示されプレビューが保護される。

---

## 3. 影響範囲の確認
- **単一ページ表示および連続表示**:
  - 双方で共通の `DetailPageItemTemplate` を使用しているため、どちらの表示モードでも一貫して正しく機能する。
- **フォントレンダリング品質**:
  - ピクセル後処理を行わないため、PDFium ネイティブのフォントアンチエイリアス・ガンマ補正の品質がそのまま保たれる。

---

## 4. 検証計画

### 4.1 単体テスト
- `DetailEditorViewModelTests` 等の既存テストスイートの実行（破壊的変更がないことの確認）。
- `DetailPageItemViewModel` において、`PageBackground` の設定およびアンロード（`UnloadBackground`）時のプロパティ通知が正常に機能することを検証するテストの確認。

### 4.2 コマンドライン検証
1. `dotnet test`: 全単体テストが 100% PASS すること。
2. `dotnet build`: Release ビルドが警告およびエラー 0 件で成功すること。

---

## 5. ドキュメントおよびバージョン管理
- `Directory.Build.props`: パッチバージョンインクリメント（`0.10.0` → `0.10.1`）
- `CHANGELOG.md`: `[0.10.1]` セクションを作成し、エンドユーザー向けに改善内容を明記
- `docs/basic_design.md`: サムネイル表示切り替え仕様の反映
- `docs/PROJECT.md`: 機能インベントリの更新
