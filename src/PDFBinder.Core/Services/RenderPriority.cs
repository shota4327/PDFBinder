namespace PDFBinder.Core.Services;

/// <summary>
/// PDFレンダリング要求の実行優先度を表します。
/// </summary>
public enum RenderPriority
{
    /// <summary>低優先度（バックグラウンドサムネイル生成、事前先読みなど）</summary>
    Low = 0,

    /// <summary>通常優先度（印刷プレビュー、標準処理など）</summary>
    Normal = 1,

    /// <summary>高優先度（手書き詳細エディタのカレントページ表示プレビューなど）</summary>
    High = 2
}
