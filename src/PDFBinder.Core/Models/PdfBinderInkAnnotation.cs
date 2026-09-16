using System.IO;
using System.Reflection;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;

namespace PDFBinder.Core.Models;

/// <summary>
/// PDF Binder 専用の手書きストローク注釈オブジェクト
/// </summary>
public class PdfBinderInkAnnotation : PdfAnnotation
{
    /// <summary>
    /// ISF（Ink Serialized Format）データを格納する辞書キー
    /// </summary>
    public const string InkKey = "/PdfBinderInk";

    /// <summary>
    /// 新しい手書き注釈オブジェクトを初期化します。
    /// </summary>
    public PdfBinderInkAnnotation(PdfDocument document) : base(document)
    {
        Elements.SetName("/Type", "/Annot");
        Elements.SetName("/Subtype", "/Ink");
    }

    /// <summary>
    /// ストロークコレクションを ISF の Base64 文字列にシリアライズします。
    /// </summary>
    public static string SerializeStrokes(StrokeCollection strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        using var ms = new MemoryStream();
        strokes.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Base64 文字列から ISF を復元し、StrokeCollection を生成します。
    /// </summary>
    public static StrokeCollection DeserializeStrokes(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return [];
        }

        byte[] bytes = Convert.FromBase64String(base64);
        using var ms = new MemoryStream(bytes);
        return new StrokeCollection(ms);
    }

    /// <summary>
    /// ページ内の PDF Binder 専用手書き注釈をすべて削除します。
    /// </summary>
    public static void RemoveBinderInkAnnotations(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        for (int i = page.Annotations.Count - 1; i >= 0; i--)
        {
            var annot = page.Annotations[i];
            if (annot != null && annot.Elements.ContainsKey(InkKey))
            {
                page.Annotations.Remove(annot);
            }
        }
    }

    /// <summary>
    /// ページが PDF Binder 専用手書き注釈を含んでいるかどうかを判定します。
    /// </summary>
    public static bool HasBinderInkAnnotation(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        for (int i = 0; i < page.Annotations.Count; i++)
        {
            var annot = page.Annotations[i];
            if (annot != null && annot.Elements.ContainsKey(InkKey))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 他社製リーダー閲覧用のアピアランスストリーム辞書（/AP /N）を生成します。
    /// </summary>
    public static PdfDictionary CreateAppearanceStream(
        PdfDocument doc,
        StrokeCollection strokes,
        PageRotation rotation,
        double pageWidth,
        double pageHeight)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(strokes);

        var form = new XForm(doc, XUnit.FromPoint(pageWidth), XUnit.FromPoint(pageHeight));
        using (var gfx = XGraphics.FromForm(form))
        {
            foreach (var stroke in strokes)
            {
                DrawSingleStroke(gfx, stroke, rotation, pageWidth, pageHeight);
            }
        }

        var prop = typeof(XForm).GetProperty(
            "PdfForm",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var pdfForm = prop?.GetValue(form) as PdfDictionary
            ?? throw new InvalidOperationException("XForm から PdfForm の取得に失敗しました。");

        var apDict = new PdfDictionary(doc);
        apDict.Elements["/N"] = pdfForm;
        return apDict;
    }

    /// <summary>
    /// 1本のストロークを XGraphics に描画します。
    /// </summary>
    private static void DrawSingleStroke(
        XGraphics gfx,
        Stroke stroke,
        PageRotation rotation,
        double pageWidth,
        double pageHeight)
    {
        var points = stroke.StylusPoints;
        if (points.Count < 2)
        {
            return;
        }

        var attr = stroke.DrawingAttributes;
        var mediaColor = attr.Color;
        byte alpha = attr.IsHighlighter ? (byte)120 : mediaColor.A;

        var color = XColor.FromArgb(alpha, mediaColor.R, mediaColor.G, mediaColor.B);
        var pen = new XPen(color, attr.Width)
        {
            LineCap = XLineCap.Round,
            LineJoin = XLineJoin.Round
        };

        var xPoints = new XPoint[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            xPoints[i] = TransformDisplayToPagePoint(
                points[i].X,
                points[i].Y,
                rotation,
                pageWidth,
                pageHeight);
        }

        gfx.DrawLines(pen, xPoints);
    }

    /// <summary>
    /// 表示座標系の点を PDF ページの未回転用紙座標系へ逆回転変換します。
    /// </summary>
    private static XPoint TransformDisplayToPagePoint(
        double x,
        double y,
        PageRotation rotation,
        double pageWidth,
        double pageHeight)
    {
        return rotation switch
        {
            PageRotation.Rotate90 => new XPoint(y, pageHeight - x),
            PageRotation.Rotate180 => new XPoint(pageWidth - x, pageHeight - y),
            PageRotation.Rotate270 => new XPoint(pageWidth - y, x),
            _ => new XPoint(x, y)
        };
    }
}
