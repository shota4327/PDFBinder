namespace PDFBinder.Core.Models;

/// <summary>
/// 詳細エディタビューの表示フィットモード
/// </summary>
public enum DetailViewFitMode
{
    /// <summary>
    /// カスタム倍率（手動ズーム中）
    /// </summary>
    None,

    /// <summary>
    /// ウィンドウ全体に1ページが収まるように自動調整（標準デフォルト）
    /// </summary>
    FitToWindow,

    /// <summary>
    /// 表示領域の横幅いっぱいに収まるように自動調整
    /// </summary>
    FitToWidth,

    /// <summary>
    /// 100%（原寸・等倍表示）
    /// </summary>
    ActualSize
}
