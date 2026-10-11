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

    [Fact]
    public async Task SaveImageAsync_JpegFormat_HighDpi_DoesNotClipContent()
    {
        // 100x100のビットマップを用意し、左上を赤、右下を青にする
        int width = 100;
        int height = 100;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        // 全体を白で初期化
        Array.Fill(pixels, (byte)255);

        // 左上(0..19, 0..19)を赤(B=0, G=0, R=255, A=255)
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                int offset = y * stride + x * 4;
                pixels[offset] = 0;       // B
                pixels[offset + 1] = 0;   // G
                pixels[offset + 2] = 255; // R
                pixels[offset + 3] = 255; // A
            }
        }

        // 右下(80..99, 80..99)を青(B=255, G=0, R=0, A=255)
        for (int y = 80; y < 100; y++)
        {
            for (int x = 80; x < 100; x++)
            {
                int offset = y * stride + x * 4;
                pixels[offset] = 255;     // B
                pixels[offset + 1] = 0;   // G
                pixels[offset + 2] = 0;   // R
                pixels[offset + 3] = 255; // A
            }
        }

        var sourceBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        sourceBitmap.Freeze();

        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        try
        {
            // 300 DPI でJPEG保存（修正前は300/96倍に拡大されて右下の青が切り取られていた）
            await _service.SaveImageAsync(sourceBitmap, tempFile, ImageExportFormat.Jpeg, 300, quality: 100);

            using var stream = File.OpenRead(tempFile);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            Assert.Equal(width, frame.PixelWidth);
            Assert.Equal(height, frame.PixelHeight);

            // 右下(x=90, y=90)のピクセル色を取得
            byte[] pixelData = new byte[4];
            var cb = new CroppedBitmap(frame, new System.Windows.Int32Rect(90, 90, 1, 1));
            cb.CopyPixels(pixelData, 4, 0);

            // JPEGの非可逆圧縮を考慮しても青成分が強く、赤・緑成分が低いことを確認
            // （切り取られていた場合は全体が白になり B/G/R すべて約255になる）
            byte b = pixelData[0];
            byte g = pixelData[1];
            byte r = pixelData[2];

            Assert.True(b > 180, $"青成分が十分高いこと (実際: {b})");
            Assert.True(r < 80, $"赤成分が低いこと (実際: {r})");
            Assert.True(g < 80, $"緑成分が低いこと (実際: {g})");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
