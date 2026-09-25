using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Helpers;
using PDFBinder.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PDFBinder.Core.Services;

/// <summary>
/// 画像ファイル（JPEG / PNG）の読み込み・保存・PDF書き出しを提供するサービス実装
/// </summary>
public class ImageService : IImageService
{
    /// <inheritdoc/>
    public bool IsSupportedImage(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<PdfDocumentModel> LoadImageDocumentAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("指定された画像ファイルが見つかりません。", filePath);
        }

        byte[] bytes = await File.ReadAllBytesAsync(filePath);
        return await Task.Run(() => CreateDocumentFromBytes(filePath, bytes));
    }

    private static PdfDocumentModel CreateDocumentFromBytes(string filePath, byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        double widthPt = frame.PixelWidth * 72.0 / 96.0;
        double heightPt = frame.PixelHeight * 72.0 / 96.0;

        var doc = new PdfDocumentModel
        {
            FilePath = filePath,
            DocumentKind = DocumentKind.Image,
            IsModified = false
        };

        var page = new PdfPageModel
        {
            SourceFilePath = filePath,
            OriginalPageIndex = 0,
            Width = widthPt,
            Height = heightPt,
            OriginalRotation = PageRotation.Rotate0,
            Rotation = PageRotation.Rotate0
        };

        doc.AddPage(page);
        doc.ResetModifiedState();
        return doc;
    }

    /// <inheritdoc/>
    public async Task SaveImageAsync(PdfDocumentModel doc, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (doc.Pages.Count == 0)
        {
            throw new InvalidOperationException("保存対象のページが存在しません。");
        }

        var page = doc.Pages[0];
        string tempPath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? Path.GetTempPath(),
            $"{Guid.NewGuid()}.tmp");

        try
        {
            await Task.Run(() => BuildAndSaveImageFile(doc, page, outputPath, tempPath));
            PdfService.SafeReplaceFile(tempPath, outputPath);
            doc.FilePath = outputPath;
            page.SourceFilePath = outputPath;
            doc.ResetModifiedState();
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void BuildAndSaveImageFile(PdfDocumentModel doc, PdfPageModel page, string outputPath, string tempPath)
    {
        string readPath = page.SourceFilePath ?? doc.FilePath ?? outputPath;
        byte[] sourceBytes = File.ReadAllBytes(readPath);
        using var ms = new MemoryStream(sourceBytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource baseBitmap = decoder.Frames[0];

        if (page.Rotation != PageRotation.Rotate0)
        {
            baseBitmap = BitmapTransformHelper.CreateRotatedBitmap(baseBitmap, (int)page.Rotation) ?? baseBitmap;
        }

        var rtb = RenderImageWithInk(baseBitmap, page);

        BitmapEncoder encoder = CreateEncoderForPath(outputPath);
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }

    private static RenderTargetBitmap RenderImageWithInk(BitmapSource baseBitmap, PdfPageModel page)
    {
        int targetPixelWidth = baseBitmap.PixelWidth;
        int targetPixelHeight = baseBitmap.PixelHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double scaleX = (double)targetPixelWidth / page.DisplayWidth;
            double scaleY = (double)targetPixelHeight / page.DisplayHeight;

            dc.PushTransform(new ScaleTransform(scaleX, scaleY));
            dc.DrawImage(baseBitmap, new Rect(0, 0, page.DisplayWidth, page.DisplayHeight));

            if (page.InkStrokes.Count > 0)
            {
                page.InkStrokes.Draw(dc);
            }

            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(targetPixelWidth, targetPixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapEncoder CreateEncoderForPath(string path)
    {
        string ext = Path.GetExtension(path);
        if (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return new JpegBitmapEncoder { QualityLevel = 92 };
        }
        return new PngBitmapEncoder();
    }

    /// <inheritdoc/>
    public async Task SaveImageAsPdfAsync(PdfDocumentModel doc, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (doc.Pages.Count == 0)
        {
            throw new InvalidOperationException("保存対象のページが存在しません。");
        }

        var page = doc.Pages[0];
        string tempPath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? Path.GetTempPath(),
            $"{Guid.NewGuid()}.tmp");

        try
        {
            await Task.Run(() => BuildAndSaveImagePdf(doc, page, tempPath));
            PdfService.SafeReplaceFile(tempPath, outputPath);
            doc.ResetModifiedState();
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void BuildAndSaveImagePdf(PdfDocumentModel doc, PdfPageModel page, string tempPath)
    {
        using var pdfDoc = new PdfDocument();
        var pdfPage = pdfDoc.AddPage();
        pdfPage.Width = XUnit.FromPoint(page.DisplayWidth);
        pdfPage.Height = XUnit.FromPoint(page.DisplayHeight);

        string readPath = page.SourceFilePath ?? doc.FilePath ?? string.Empty;
        byte[] sourceBytes = File.ReadAllBytes(readPath);
        using var ms = new MemoryStream(sourceBytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource baseBitmap = decoder.Frames[0];

        if (page.Rotation != PageRotation.Rotate0)
        {
            baseBitmap = BitmapTransformHelper.CreateRotatedBitmap(baseBitmap, (int)page.Rotation) ?? baseBitmap;
        }

        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(baseBitmap));
        using var imgStream = new MemoryStream();
        pngEncoder.Save(imgStream);
        imgStream.Position = 0;

        using (var xImage = XImage.FromStream(imgStream))
        using (var gfx = XGraphics.FromPdfPage(pdfPage))
        {
            gfx.DrawImage(xImage, 0, 0, page.DisplayWidth, page.DisplayHeight);
        }

        if (page.InkStrokes.Count > 0)
        {
            PdfService.AttachInkAnnotationToPage(pdfDoc, pdfPage, page.InkStrokes, PageRotation.Rotate0);
        }

        pdfDoc.Save(tempPath);
    }
}
