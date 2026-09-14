namespace PDFBinder.App.Models;

/// <summary>
/// 詳細手書きエディタにおけるページの表示・配置モードを定義します。
/// </summary>
public enum DetailPageViewMode
{
    /// <summary>
    /// 単一ページ表示（1ページずつ画面に表示し、スクロールや操作でページを切り替える）
    /// </summary>
    SinglePage,

    /// <summary>
    /// 連続表示（全ページを縦一列に並べて連続スクロール表示する）
    /// </summary>
    Continuous
}
