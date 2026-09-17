namespace PDFBinder.Core.Models;

/// <summary>
/// 印刷の向き
/// </summary>
public enum PrintOrientation
{
    /// <summary>縦向き（ポートレート）</summary>
    Portrait,

    /// <summary>横向き（ランドスケープ）</summary>
    Landscape
}

/// <summary>
/// 印刷用紙サイズ
/// </summary>
public enum PrintPaperSize
{
    /// <summary>A4用紙（210mm × 297mm）</summary>
    A4,

    /// <summary>A3用紙（297mm × 420mm）</summary>
    A3
}

/// <summary>
/// 両面印刷モード
/// </summary>
public enum PrintDuplexMode
{
    /// <summary>片面印刷</summary>
    OneSided,

    /// <summary>両面印刷（長辺とじ）</summary>
    TwoSidedLongEdge,

    /// <summary>両面印刷（短辺とじ）</summary>
    TwoSidedShortEdge
}

/// <summary>
/// 印刷ページ範囲種別
/// </summary>
public enum PrintRangeType
{
    /// <summary>すべてのページ</summary>
    AllPages,

    /// <summary>現在のページのみ</summary>
    CurrentPage,

    /// <summary>カスタム指定（例: 1-3, 5）</summary>
    Custom
}

/// <summary>
/// 印刷面付け・レイアウトモード
/// </summary>
public enum PrintLayoutMode
{
    /// <summary>用紙サイズに合わせる（1用紙に1ページ）</summary>
    FitToPage,

    /// <summary>1ページに集約（N-up印刷）</summary>
    NUp,

    /// <summary>冊子形式（中綴じ製本面付け）</summary>
    Booklet
}

/// <summary>
/// 1枚あたりの集約ページ数（N-up）
/// </summary>
public enum NUpPagesPerSheet
{
    /// <summary>2ページ集約</summary>
    Two = 2,

    /// <summary>4ページ集約</summary>
    Four = 4,

    /// <summary>8ページ集約</summary>
    Eight = 8
}
