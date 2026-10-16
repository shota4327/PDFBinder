using System.Windows;
using PDFBinder.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfAnnotation = PdfSharp.Pdf.Annotations.PdfAnnotation;
using PdfLinkAnnotation = PDFBinder.Core.Models.PdfLinkAnnotation;

namespace PDFBinder.Core.Helpers;

/// <summary>
/// PDFリンク注釈の抽出・解析および表示座標変換を支援するヘルパークラス
/// </summary>
public static class PdfLinkHelper
{
    /// <summary>
    /// PDFSharp のページオブジェクトから生のリンク注釈（ポイント座標単位）を抽出します。
    /// </summary>
    public static List<PdfRawLinkAnnotation> ExtractRawLinks(
        PdfPage page,
        PdfDocument doc,
        CancellationToken cancellationToken = default)
    {
        var rawLinks = new List<PdfRawLinkAnnotation>();
        if (page == null || doc == null || page.Annotations == null) return rawLinks;

        double ptWidth = page.Width.Point;
        double ptHeight = page.Height.Point;
        if (ptWidth <= 0 || ptHeight <= 0) return rawLinks;

        for (int i = 0; i < page.Annotations.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var annot = page.Annotations[i];
            if (annot == null || annot.Elements.GetString("/Subtype") != "/Link") continue;

            var rawLink = ParseRawLinkAnnotation(annot, doc, ptWidth, ptHeight);
            if (rawLink != null)
            {
                rawLinks.Add(rawLink);
            }
        }

        return rawLinks;
    }

    /// <summary>
    /// 個々のリンク注釈オブジェクトを生リンク注釈モデルに変換します。
    /// </summary>
    public static PdfRawLinkAnnotation? ParseRawLinkAnnotation(
        PdfAnnotation annot,
        PdfDocument doc,
        double ptWidth,
        double ptHeight)
    {
        var pdfRect = annot.Elements.GetRectangle("/Rect");
        double pdfX = Math.Min(pdfRect.X1, pdfRect.X2);
        double pdfY = Math.Min(pdfRect.Y1, pdfRect.Y2);
        double pdfW = Math.Abs(pdfRect.X2 - pdfRect.X1);
        double pdfH = Math.Abs(pdfRect.Y2 - pdfRect.Y1);
        if (pdfW <= 0 || pdfH <= 0 || ptWidth <= 0 || ptHeight <= 0) return null;

        var rawRect = new Rect(pdfX, pdfY, pdfW, pdfH);
        var action = annot.Elements.GetDictionary("/A");
        if (action != null)
        {
            string s = action.Elements.GetString("/S");
            if (s == "/URI")
            {
                string uri = action.Elements.GetString("/URI");
                return new PdfRawLinkAnnotation(rawRect, ptWidth, ptHeight, PdfLinkType.Uri, uri, -1);
            }
            if (s == "/GoTo")
            {
                int targetPage = ResolveGoToPage(action, doc);
                return new PdfRawLinkAnnotation(rawRect, ptWidth, ptHeight, PdfLinkType.PageJump, null, targetPage);
            }
        }

        var dest = annot.Elements.GetArray("/Dest");
        if (dest != null)
        {
            int targetPage = ResolveDestinationPage(dest, doc);
            return new PdfRawLinkAnnotation(rawRect, ptWidth, ptHeight, PdfLinkType.PageJump, null, targetPage);
        }

        return null;
    }

    /// <summary>
    /// 生のリンク注釈コレクションを WPF 表示座標系に変換します。
    /// </summary>
    public static List<PdfLinkAnnotation> TransformRawLinks(
        IEnumerable<PdfRawLinkAnnotation> rawLinks,
        double displayWidth,
        double displayHeight,
        PageRotation rotation)
    {
        var links = new List<PdfLinkAnnotation>();
        if (rawLinks == null) return links;

        foreach (var raw in rawLinks)
        {
            var box = TransformPdfPointRect(
                raw.RawPdfRect.X, raw.RawPdfRect.Y, raw.RawPdfRect.Width, raw.RawPdfRect.Height,
                raw.PtWidth, raw.PtHeight, displayWidth, displayHeight, rotation);

            if (box.Width > 0 && box.Height > 0)
            {
                links.Add(new PdfLinkAnnotation(box, raw.LinkType, raw.Uri, raw.TargetPageIndex));
            }
        }

        return links;
    }

    /// <summary>
    /// PdfSharp のポイント単位矩形（左下原点）を WPF 表示座標系に変換します。
    /// </summary>
    public static Rect TransformPdfPointRect(
        double pdfX, double pdfY, double pdfW, double pdfH,
        double ptWidth, double ptHeight,
        double displayWidth, double displayHeight,
        PageRotation rotation)
    {
        double normLeft = pdfX / ptWidth;
        double normTop = (ptHeight - (pdfY + pdfH)) / ptHeight;
        double normW = pdfW / ptWidth;
        double normH = pdfH / ptHeight;

        double x = normLeft * displayWidth;
        double y = normTop * displayHeight;
        double w = normW * displayWidth;
        double h = normH * displayHeight;

        return ApplyRotation(new Rect(x, y, w, h), displayWidth, displayHeight, rotation);
    }

    /// <summary>
    /// 回転角度に応じた矩形のアフィン変換を適用します。
    /// </summary>
    public static Rect ApplyRotation(Rect rect, double displayWidth, double displayHeight, PageRotation rotation)
    {
        return rotation switch
        {
            PageRotation.Rotate90 => new Rect(
                displayHeight - (rect.Y + rect.Height),
                rect.X,
                rect.Height,
                rect.Width),
            PageRotation.Rotate180 => new Rect(
                displayWidth - (rect.X + rect.Width),
                displayHeight - (rect.Y + rect.Height),
                rect.Width,
                rect.Height),
            PageRotation.Rotate270 => new Rect(
                rect.Y,
                displayWidth - (rect.X + rect.Width),
                rect.Height,
                rect.Width),
            _ => rect
        };
    }

    /// <summary>
    /// GoTo アクションから遷移先ページ番号を解決します。
    /// </summary>
    private static int ResolveGoToPage(PdfDictionary action, PdfDocument doc)
    {
        var dest = action.Elements.GetArray("/D");
        return dest != null ? ResolveDestinationPage(dest, doc) : -1;
    }

    /// <summary>
    /// Destination 配列から遷移先ページインデックスを解決します。
    /// </summary>
    private static int ResolveDestinationPage(PdfArray dest, PdfDocument doc)
    {
        if (dest.Elements.Count == 0) return -1;
        var first = dest.Elements[0];
        if (first is PdfReference pref && pref.Value is PdfDictionary pageDict)
        {
            for (int i = 0; i < doc.PageCount; i++)
            {
                if (doc.Pages[i] == pageDict) return i;
            }
        }
        if (first is PdfInteger pNum)
        {
            return pNum.Value;
        }
        return -1;
    }
}
