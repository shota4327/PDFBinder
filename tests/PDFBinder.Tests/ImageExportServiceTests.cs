using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 画像書き出しサービス（ImageExportService）の単体テスト
/// </summary>
public class ImageExportServiceTests
{
    private readonly ImageExportService _service = new();

    [Theory]
    [InlineData(595.28, 841.89, 200, 1654, 2339)]
    [InlineData(595.28, 841.89, 300, 2480, 3508)]
    [InlineData(595.28, 841.89, 400, 3307, 4677)]
    [InlineData(595.28, 841.89, 600, 4961, 7016)]
    public void CalculatePixelSize_ValidPointsAndDpi_ReturnsExpectedPixelDimensions(
        double pointW, double pointH, int dpi, int expectedW, int expectedH)
    {
        var (w, h) = _service.CalculatePixelSize(pointW, pointH, dpi);
        Assert.Equal(expectedW, w);
        Assert.Equal(expectedH, h);
    }

    [Theory]
    [InlineData(0, 100, 300)]
    [InlineData(100, 0, 300)]
    [InlineData(100, 100, 0)]
    public void CalculatePixelSize_InvalidInput_ReturnsZero(double pointW, double pointH, int dpi)
    {
        var (w, h) = _service.CalculatePixelSize(pointW, pointH, dpi);
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public async Task SaveImageAsync_PngFormat_CreatesValidImageFile()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        try
        {
            var bitmap = CreateTestBitmap(100, 100);
            await _service.SaveImageAsync(bitmap, tempFile, ImageExportFormat.Png, 300);

            Assert.True(File.Exists(tempFile));
            var fileInfo = new FileInfo(tempFile);
            Assert.True(fileInfo.Length > 0);

            using var stream = File.OpenRead(tempFile);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.Equal(100, decoder.Frames[0].PixelWidth);
            Assert.Equal(100, decoder.Frames[0].PixelHeight);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SaveImageAsync_JpegFormat_CreatesValidImageFile()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        try
        {
            var bitmap = CreateTestBitmap(80, 60);
            await _service.SaveImageAsync(bitmap, tempFile, ImageExportFormat.Jpeg, 400, quality: 90);

            Assert.True(File.Exists(tempFile));
            var fileInfo = new FileInfo(tempFile);
            Assert.True(fileInfo.Length > 0);

            using var stream = File.OpenRead(tempFile);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.Equal(80, decoder.Frames[0].PixelWidth);
            Assert.Equal(60, decoder.Frames[0].PixelHeight);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static BitmapSource CreateTestBitmap(int width, int height)
    {
        int stride = (width * 32 + 7) / 8;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;     // Blue
            pixels[i + 1] = 0;   // Green
            pixels[i + 2] = 0;   // Red
            pixels[i + 3] = 255; // Alpha
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
