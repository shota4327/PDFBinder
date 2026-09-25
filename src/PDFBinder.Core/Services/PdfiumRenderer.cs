using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using PDFBinder.Core.Helpers;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// PDFium（Docnet.Core）を利用したPDFページレンダリングサービス実装
/// </summary>
public class PdfiumRenderer : IPdfRenderer
{
    private readonly PriorityAsyncLock _renderLock = new();

    /// <summary>
    /// 現在のレンダリング排他ロックインスタンスを取得します（単体テスト検証用）。
    /// </summary>
    internal PriorityAsyncLock RenderLock => _renderLock;

    /// <inheritdoc/>
    public Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default)
        => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken, RenderPriority.Normal);

    /// <inheritdoc/>
    public async Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
        }

        if (IsSupportedImage(filePath))
        {
            return await Task.Run(() => RenderImagePage(filePath, targetWidth, targetHeight, rotation, cancellationToken), cancellationToken);
        }

        using var releaser = await _renderLock.AcquireAsync(priority, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                cancellationToken.ThrowIfCancellationRequested();
                byte[] renderBytes = SanitizeForRendering(bytes, pageIndex);
                cancellationToken.ThrowIfCancellationRequested();

                // Docnet の PageDimensions(dimOne, dimTwo) は dimOne <= dimTwo (短辺, 長辺) を厳格に要求するため正規化
                int minDim = Math.Min(targetWidth, targetHeight);
                int maxDim = Math.Max(targetWidth, targetHeight);
                var dimensions = new PageDimensions(Math.Max(1, minDim), Math.Max(1, maxDim));

                using var docReader = DocLib.Instance.GetDocReader(renderBytes, dimensions);
                if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
                {
                    return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
                }

                cancellationToken.ThrowIfCancellationRequested();
                using var pageReader = docReader.GetPageReader(pageIndex);
                int actualWidth = pageReader.GetPageWidth();
                int actualHeight = pageReader.GetPageHeight();
                byte[] rawBytes = pageReader.GetImage(RenderFlags.RenderAnnotations);

                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = BitmapSource.Create(
                    actualWidth,
                    actualHeight,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    rawBytes,
                    actualWidth * 4);

                bitmap.Freeze();

                if (rotation != PageRotation.Rotate0)
                {
                    var rotated = new TransformedBitmap(bitmap, new RotateTransform((int)rotation));
                    rotated.Freeze();
                    return rotated;
                }

                return bitmap;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
            }
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
    {
        int w = Math.Max(1, targetWidth);
        int h = Math.Max(1, targetHeight);

        if (rotation is PageRotation.Rotate90 or PageRotation.Rotate270)
        {
            (w, h) = (h, w);
        }

        var writeable = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = new byte[w * h * 4];
        Array.Fill(pixels, (byte)255);

        writeable.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        writeable.Freeze();
        return writeable;
    }

    /// <inheritdoc/>
    public BitmapSource CompositeStrokes(
        BitmapSource baseImage,
        System.Windows.Ink.StrokeCollection strokes,
        double originalPageWidth,
        double originalPageHeight)
    {
        if (baseImage == null) throw new ArgumentNullException(nameof(baseImage));
        if (strokes == null || strokes.Count == 0 || originalPageWidth <= 0 || originalPageHeight <= 0)
        {
            return baseImage;
        }

        int thumbWidth = baseImage.PixelWidth;
        int thumbHeight = baseImage.PixelHeight;
        if (thumbWidth <= 0 || thumbHeight <= 0)
        {
            return baseImage;
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(baseImage, new Rect(0, 0, thumbWidth, thumbHeight));

            double scaleX = (double)thumbWidth / originalPageWidth;
            double scaleY = (double)thumbHeight / originalPageHeight;
            dc.PushTransform(new ScaleTransform(scaleX, scaleY));
            strokes.Draw(dc);
            dc.Pop();
        }

        var renderTarget = new RenderTargetBitmap(thumbWidth, thumbHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    /// <inheritdoc/>
    public Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default)
        => ExtractInteractiveDataAsync(filePath, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, RenderPriority.Normal);

    /// <inheritdoc/>
    public async Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || IsSupportedImage(filePath))
        {
            return PageInteractiveData.Empty;
        }

        using var releaser = await _renderLock.AcquireAsync(priority, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                cancellationToken.ThrowIfCancellationRequested();

                var characters = ExtractCharacters(bytes, pageIndex, displayWidth, displayHeight, rotation, cancellationToken);
                var links = ExtractLinks(bytes, pageIndex, displayWidth, displayHeight, rotation, cancellationToken);

                return new PageInteractiveData(characters, links, rotation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return PageInteractiveData.Empty;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Docnet.Core を用いて指定ページの文字および座標を抽出します。
    /// </summary>
    private List<PdfTextCharacter> ExtractCharacters(
        byte[] pdfBytes,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken)
    {
        var result = new List<PdfTextCharacter>();
        var dimensions = new PageDimensions(1080, 1920);

        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, dimensions);
        if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
        {
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var pageReader = docReader.GetPageReader(pageIndex);
        int rawWidth = pageReader.GetPageWidth();
        int rawHeight = pageReader.GetPageHeight();
        if (rawWidth <= 0 || rawHeight <= 0) return result;

        var rawCharacters = pageReader.GetCharacters();
        if (rawCharacters == null) return result;

        int index = 0;
        foreach (var c in rawCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var box = TransformCharacterBox(
                c.Box.Left, c.Box.Top, c.Box.Right, c.Box.Bottom,
                rawWidth, rawHeight, displayWidth, displayHeight, rotation);

            result.Add(new PdfTextCharacter(c.Char, box, index++));
        }

        return result;
    }

    /// <summary>
    /// PdfSharp を用いて指定ページのリンク注釈を抽出します。
    /// </summary>
    private List<PdfLinkAnnotation> ExtractLinks(
        byte[] pdfBytes,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken)
    {
        var links = new List<PdfLinkAnnotation>();
        using var ms = new MemoryStream(pdfBytes);
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(ms, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

        if (pageIndex < 0 || pageIndex >= doc.PageCount) return links;

        var page = doc.Pages[pageIndex];
        double ptWidth = page.Width.Point;
        double ptHeight = page.Height.Point;
        if (ptWidth <= 0 || ptHeight <= 0) return links;

        for (int i = 0; i < page.Annotations.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var annot = page.Annotations[i];
            if (annot == null) continue;

            if (annot.Elements.GetString("/Subtype") != "/Link") continue;

            var link = ParseLinkAnnotation(annot, doc, ptWidth, ptHeight, displayWidth, displayHeight, rotation);
            if (link != null)
            {
                links.Add(link);
            }
        }

        return links;
    }

    /// <summary>
    /// 個々のリンク注釈オブジェクトを解析してモデル化します。
    /// </summary>
    private PdfLinkAnnotation? ParseLinkAnnotation(
        PdfSharp.Pdf.Annotations.PdfAnnotation annot,
        PdfSharp.Pdf.PdfDocument doc,
        double ptWidth,
        double ptHeight,
        double displayWidth,
        double displayHeight,
        PageRotation rotation)
    {
        var pdfRect = annot.Elements.GetRectangle("/Rect");
        double pdfX = Math.Min(pdfRect.X1, pdfRect.X2);
        double pdfY = Math.Min(pdfRect.Y1, pdfRect.Y2);
        double pdfW = Math.Abs(pdfRect.X2 - pdfRect.X1);
        double pdfH = Math.Abs(pdfRect.Y2 - pdfRect.Y1);

        var box = TransformPdfPointRect(
            pdfX, pdfY, pdfW, pdfH,
            ptWidth, ptHeight, displayWidth, displayHeight, rotation);

        if (box.Width <= 0 || box.Height <= 0) return null;

        var action = annot.Elements.GetDictionary("/A");
        if (action != null)
        {
            string s = action.Elements.GetString("/S");
            if (s == "/URI")
            {
                string uri = action.Elements.GetString("/URI");
                return new PdfLinkAnnotation(box, PdfLinkType.Uri, uri, -1);
            }
            if (s == "/GoTo")
            {
                int targetPage = ResolveGoToPage(action, doc);
                return new PdfLinkAnnotation(box, PdfLinkType.PageJump, null, targetPage);
            }
        }

        var dest = annot.Elements.GetArray("/Dest");
        if (dest != null)
        {
            int targetPage = ResolveDestinationPage(dest, doc);
            return new PdfLinkAnnotation(box, PdfLinkType.PageJump, null, targetPage);
        }

        return null;
    }

    /// <summary>
    /// GoTo アクションから遷移先ページ番号を解決します。
    /// </summary>
    private int ResolveGoToPage(PdfSharp.Pdf.PdfDictionary action, PdfSharp.Pdf.PdfDocument doc)
    {
        var dest = action.Elements.GetArray("/D");
        if (dest != null)
        {
            return ResolveDestinationPage(dest, doc);
        }
        return -1;
    }

    /// <summary>
    /// Destination 配列から遷移先ページインデックスを解決します。
    /// </summary>
    private int ResolveDestinationPage(PdfSharp.Pdf.PdfArray dest, PdfSharp.Pdf.PdfDocument doc)
    {
        if (dest.Elements.Count == 0) return -1;
        var first = dest.Elements[0];
        if (first is PdfSharp.Pdf.Advanced.PdfReference pref && pref.Value is PdfSharp.Pdf.PdfDictionary pageDict)
        {
            for (int i = 0; i < doc.PageCount; i++)
            {
                if (doc.Pages[i] == pageDict) return i;
            }
        }
        if (first is PdfSharp.Pdf.PdfInteger pNum)
        {
            return pNum.Value;
        }
        return -1;
    }

    /// <summary>
    /// Docnet.Core のピクセル文字座標を WPF 表示座標系に変換します。
    /// </summary>
    public static Rect TransformCharacterBox(
        int left, int top, int right, int bottom,
        int rawWidth, int rawHeight,
        double displayWidth, double displayHeight,
        PageRotation rotation)
    {
        double normLeft = (double)Math.Min(left, right) / rawWidth;
        double normTop = (double)Math.Min(top, bottom) / rawHeight;
        double normRight = (double)Math.Max(left, right) / rawWidth;
        double normBottom = (double)Math.Max(top, bottom) / rawHeight;

        double x = normLeft * displayWidth;
        double y = normTop * displayHeight;
        double w = Math.Max(1.0, (normRight - normLeft) * displayWidth);
        double h = Math.Max(1.0, (normBottom - normTop) * displayHeight);

        return ApplyRotation(new Rect(x, y, w, h), displayWidth, displayHeight, rotation);
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
    private static Rect ApplyRotation(Rect rect, double displayWidth, double displayHeight, PageRotation rotation)
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
    /// レンダリング用に PDF バイト列から自前の手書き注釈を除外します（他社製注釈はそのまま保持）。
    /// </summary>
    private static bool IsSupportedImage(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase);
    }

    private static BitmapSource? RenderImagePage(
        string filePath,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = File.ReadAllBytes(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource bitmap = decoder.Frames[0];

        if (rotation != PageRotation.Rotate0)
        {
            bitmap = BitmapTransformHelper.CreateRotatedBitmap(bitmap, (int)rotation) ?? bitmap;
        }

        if (targetWidth > 0 && targetHeight > 0 &&
            (bitmap.PixelWidth != targetWidth || bitmap.PixelHeight != targetHeight))
        {
            double sx = (double)targetWidth / bitmap.PixelWidth;
            double sy = (double)targetHeight / bitmap.PixelHeight;
            var scaled = new TransformedBitmap(bitmap, new ScaleTransform(sx, sy));
            scaled.Freeze();
            return scaled;
        }

        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] SanitizeForRendering(byte[] pdfBytes, int pageIndex)
    {
        try
        {
            using var msIn = new MemoryStream(pdfBytes);
            using var doc = PdfSharp.Pdf.IO.PdfReader.Open(msIn, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);

            if (pageIndex < 0 || pageIndex >= doc.PageCount)
            {
                return pdfBytes;
            }

            var page = doc.Pages[pageIndex];
            if (!PdfBinderInkAnnotation.HasBinderInkAnnotation(page))
            {
                return pdfBytes;
            }

            PdfBinderInkAnnotation.RemoveBinderInkAnnotations(page);

            using var msOut = new MemoryStream();
            doc.Save(msOut);
            return msOut.ToArray();
        }
        catch
        {
            return pdfBytes;
        }
    }
}
