using System.Windows;

namespace PDFBinder.Core.Models;

/// <summary>
/// PDF元座標系（ポイント単位）における未変換のリンク注釈情報
/// </summary>
public record PdfRawLinkAnnotation(
    Rect RawPdfRect,
    double PtWidth,
    double PtHeight,
    PdfLinkType LinkType,
    string? Uri,
    int TargetPageIndex
);
