# PRマージ時単一EXE自動発行 & ドキュメント更新時Issue必須ルール整備 検証結果報告（Walkthrough）

Issue #5 において、開発運用ルール（`GEMINI.md`）にマージ完了時の単一EXE自動発行要件およびドキュメント更新時のIssue必須ルールを明記しました。

---

## 実施した変更内容

### 1. `GEMINI.md` セクション 3.1 への追記
- **ドキュメント更新時のIssue起票必須化**:
  - ドキュメント（`GEMINI.md`、`README.md`、`docs/*` 等）の更新のみであっても、例外なく必ず GitHub Issue をもとにブランチを作成して進め、`master` ブランチへの直接コミットを厳禁とすることを明文化。
- **マージ完了時の単一EXE自動発行の義務化**:
  - PR マージおよび `master` ブランチ最新化・ブランチ整理完了後に、必ず `powershell -ExecutionPolicy Bypass -File .\build.ps1` を実行して `dist/PDFBinder.exe` を最新化し、ファイルパス・サイズを完了報告に記載することを明文化。

---

## 検証結果
- `dist/PDFBinder.exe`（66.24 MB）が `build.ps1` により正常に発行可能であることを実証済み。
