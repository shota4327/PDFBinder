namespace PDFBinder.Core.Models;

/// <summary>
/// PDFの綴じ方向・読み方向（/ViewerPreferences の /Direction）
/// </summary>
public enum PdfReadingDirection
{
    /// <summary>左綴じ・左から右（/L2R）</summary>
    LeftToRight,

    /// <summary>右綴じ・右から左（/R2L）</summary>
    RightToLeft
}
