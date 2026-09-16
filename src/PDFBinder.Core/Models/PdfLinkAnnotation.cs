using System.Windows;

namespace PDFBinder.Core.Models;

/// <summary>
/// PDFリンク注釈の種別
/// </summary>
public enum PdfLinkType
{
    /// <summary>外部WebサイトURLへのリンク</summary>
    Uri,
    /// <summary>ドキュメント内別ページへのジャンプ</summary>
    PageJump
}

/// <summary>
/// PDFページ内のリンク注釈（URLまたはページジャンプ）を表すモデル
/// </summary>
public record PdfLinkAnnotation(
    Rect BoundingBox,
    PdfLinkType LinkType,
    string? Uri,
    int TargetPageIndex
);
