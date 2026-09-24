# 実装計画: アプリアイコンの新デザインへの更新 (Issue #159)

新しく作成されたアプリアイコン（`icon_new.png`, `icon_new.ico`）をプロジェクトに反映し、ルートの正本アイコン、WPFアプリケーションのウィンドウアイコン、タスクバーアイコン、リソースアイコン、Aboutダイアログ等のアイコンを最新デザインに更新します。

## 1. 現状分析と形式要件・設計決定事項
プロジェクト内で使用されているアプリアイコンの形式と参照箇所、および `/grill-me` で確認・決定した方針は以下の通りです：

| ファイルパス | 形式 | 役割・参照箇所 | 反映方針 |
| :--- | :--- | :--- | :--- |
| `icon.png` | PNG (1468x1468) | プロジェクトルートの正本アイコン、`README.md` 等での参照 | `icon_new.png` で更新して保持 |
| `src/PDFBinder.App/Assets/icon.png` | PNG (1468x1468) | `MainWindow.xaml`（Window.Icon, WindowChrome）、`AboutOverlayControl.xaml` | `icon_new.png` で更新 |
| `src/PDFBinder.App/Assets/icon.ico` | ICO (9フレーム: 16x16〜256x256) | `PDFBinder.App.csproj`（`<ApplicationIcon>`）、実行可能ファイルアイコン | `icon_new.ico` で更新 |

**確認・決定事項**:
1. **形式の充足状況**:
   - 提供いただいた `icon_new.png`（高解像度 1468x1468 PNG）および `icon_new.ico`（16x16〜256x256 の全9解像度フレームを内包する完全な ICO ファイル）により、本プロジェクトで必要な形式はすべて満たされており、不足形式はありません。
2. **ルート `icon.png` の扱い**:
   - `README.md` のヘッダー画像および設計規約（`GEMINI.md`, `docs/basic_design.md`）で定義された「正本アイコン」として機能するため、削除せず新しいアイコンで更新して保持します。
3. **一時ファイルの扱い**:
   - 各対象ファイルへの上書き配置完了後、ルートに配置された `icon_new.png` および `icon_new.ico` は削除してリポジトリをクリーンに保ちます。

## 2. 変更・作業内容
1. **アイコンファイルの更新**:
   - `icon_new.png` の内容を `icon.png` にコピー（上書き）
   - `icon_new.png` の内容を `src/PDFBinder.App/Assets/icon.png` にコピー（上書き）
   - `icon_new.ico` の内容を `src/PDFBinder.App/Assets/icon.ico` にコピー（上書き）
2. **一時ファイルのクリーンアップ**:
   - ルートに配置された `icon_new.png` および `icon_new.ico` を削除
3. **ビルドおよびテストの検証**:
   - `dotnet build` によるリソース埋め込み・コンパイルの正常性確認
   - `dotnet test` による全単体テストの合格確認
4. **検証報告の作成**:
   - `docs/plans/issue-159-update-app-icon-walkthrough.md` の作成

## 3. 完了条件
- [ ] すべての対象アイコンファイル（`icon.png`, `Assets/icon.png`, `Assets/icon.ico`）が新デザインに置き換わっていること
- [ ] 提供された一時ファイル（`icon_new.png`, `icon_new.ico`）がクリーンアップされていること
- [ ] `dotnet build` がエラーなく成功すること
- [ ] `dotnet test` ですべての単体テストが PASS すること
