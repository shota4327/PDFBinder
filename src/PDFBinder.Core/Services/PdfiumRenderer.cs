using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// PDFium（Docnet.Core）を利用したPDFページレンダリングサービス実装
/// </summary>
public class PdfiumRenderer : IPdfRenderer
{
    /// <inheritdoc/>
    public async Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
        }

        return await Task.Run(() =>
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                var dimensions = new PageDimensions(targetWidth, targetHeight);

                using var docReader = DocLib.Instance.GetDocReader(bytes, dimensions);
                if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
                {
                    return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
                }

                using var pageReader = docReader.GetPageReader(pageIndex);
                int actualWidth = pageReader.GetPageWidth();
                int actualHeight = pageReader.GetPageHeight();
                byte[] rawBytes = pageReader.GetImage();

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
            catch
            {
                return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
            }
        });
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
}
