using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// PDFページの画像（PNG / JPEG）書き出しおよび解像度計算を提供するサービス実装
/// </summary>
public class ImageExportService : IImageExportService
{
    /// <inheritdoc/>
    public (int Width, int Height) CalculatePixelSize(double pointWidth, double pointHeight, int dpi)
    {
        if (pointWidth <= 0 || pointHeight <= 0 || dpi <= 0)
        {
            return (0, 0);
        }

        int width = (int)Math.Round(pointWidth * dpi / 72.0);
        int height = (int)Math.Round(pointHeight * dpi / 72.0);
        return (Math.Max(1, width), Math.Max(1, height));
    }

    /// <inheritdoc/>
    public async Task SaveImageAsync(
        BitmapSource bitmap,
        string filePath,
        ImageExportFormat format,
        int dpi,
        int quality = 90)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = Path.Combine(
            string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir,
            $"{Guid.NewGuid()}.tmp");

        try
        {
            await Task.Run(() => WriteImageFile(bitmap, tempPath, format, dpi, quality));
            PdfService.SafeReplaceFile(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* 一時ファイル削除失敗は無視 */ }
            }
        }
    }

    /// <summary>
    /// ビットマップを指定フォーマット・解像度で一時ファイルへエンコード出力します。
    /// </summary>
    private static void WriteImageFile(
        BitmapSource sourceBitmap,
        string targetFilePath,
        ImageExportFormat format,
        int dpi,
        int quality)
    {
        BitmapSource prepared = PrepareBitmapForExport(sourceBitmap, format, dpi);
        BitmapEncoder encoder = CreateEncoder(format, quality);
        encoder.Frames.Add(BitmapFrame.Create(prepared));

        using var fs = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }

    /// <summary>
    /// フォーマットおよびDPIに合わせたBitmapSource（白背景合成やDPIメタデータ設定）を準備します。
    /// </summary>
    private static BitmapSource PrepareBitmapForExport(BitmapSource bitmap, ImageExportFormat format, int dpi)
    {
        if (format == ImageExportFormat.Jpeg)
        {
            return CompositeOnWhiteBackground(bitmap, dpi);
        }

        return CreateBitmapWithDpi(bitmap, dpi);
    }

    /// <summary>
    /// 透過ピクセルの黒ずみを防ぐため白背景へ合成し、指定DPIのBitmapSourceを生成します。
    /// </summary>
    private static BitmapSource CompositeOnWhiteBackground(BitmapSource bitmap, int dpi)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        // 96 DPI (1 DIP = 1 ピクセル) で白背景上に元ビットマップを描画
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawImage(bitmap, new Rect(0, 0, width, height));
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        return CreateBitmapWithDpi(rtb, dpi);
    }

    /// <summary>
    /// ビットマップのピクセルデータを引き継ぎ、指定DPIメタデータを設定したBitmapSourceを生成します。
    /// </summary>
    private static BitmapSource CreateBitmapWithDpi(BitmapSource source, int dpi)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = (width * source.Format.BitsPerPixel + 7) / 8;
        byte[] pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        var created = BitmapSource.Create(
            width,
            height,
            dpi,
            dpi,
            source.Format,
            source.Palette,
            pixels,
            stride);
        created.Freeze();
        return created;
    }

    /// <summary>
    /// フォーマットに応じたBitmapEncoderインスタンスを生成します。
    /// </summary>
    private static BitmapEncoder CreateEncoder(ImageExportFormat format, int quality)
    {
        if (format == ImageExportFormat.Jpeg)
        {
            int clampedQuality = Math.Clamp(quality, 1, 100);
            return new JpegBitmapEncoder { QualityLevel = clampedQuality };
        }

        return new PngBitmapEncoder();
    }
}
