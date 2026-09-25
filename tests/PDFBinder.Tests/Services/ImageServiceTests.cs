using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PDFBinder.Tests.Services;

/// <summary>
/// ImageService の画像読み込み・保存・PDF変換処理の単体テスト
/// </summary>
public class ImageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImageService _service;

    public ImageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ImageServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _service = new ImageService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // 一時ディレクトリのクリーンアップ失敗は無視
            }
        }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("photo.JPEG", true)]
    [InlineData("image.png", true)]
    [InlineData("doc.pdf", false)]
    [InlineData("anim.gif", false)]
    [InlineData("bitmap.bmp", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedImage_ReturnsExpectedResult(string? filePath, bool expected)
    {
        bool actual = _service.IsSupportedImage(filePath);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LoadImageDocumentAsync_ValidPng_CreatesImageDocument()
    {
        string pngPath = CreateTestImageFile("test.png", 200, 100, isPng: true);

        var doc = await _service.LoadImageDocumentAsync(pngPath);

        Assert.NotNull(doc);
        Assert.Equal(DocumentKind.Image, doc.DocumentKind);
        Assert.True(doc.IsImage);
        Assert.Single(doc.Pages);
        Assert.False(doc.IsModified);
        Assert.Equal(pngPath, doc.FilePath);

        var page = doc.Pages[0];
        Assert.Equal(200 * 72.0 / 96.0, page.Width, precision: 2);
        Assert.Equal(100 * 72.0 / 96.0, page.Height, precision: 2);
        Assert.Equal(PageRotation.Rotate0, page.Rotation);
    }

    [Fact]
    public async Task SaveImageAsync_WithRotationAndInk_SavesImageProperly()
    {
        string pngPath = CreateTestImageFile("source.png", 300, 200, isPng: true);
        var doc = await _service.LoadImageDocumentAsync(pngPath);
        var page = doc.Pages[0];

        // 90度時計回りに回転
        page.RotateClockwise();

        // 手書きストロークを追加
        var points = new StylusPointCollection
        {
            new StylusPoint(10, 10),
            new StylusPoint(50, 50)
        };
        var stroke = new Stroke(points);
        page.InkStrokes.Add(stroke);

        string outputPath = Path.Combine(_tempDir, "saved_rotated.png");
        await _service.SaveImageAsync(doc, outputPath);

        Assert.True(File.Exists(outputPath));

        // 保存された画像をデコードして確認
        using var stream = File.OpenRead(outputPath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        // 300x200 を 90度回転したため、幅200 x 高さ300 になる
        Assert.Equal(200, frame.PixelWidth);
        Assert.Equal(300, frame.PixelHeight);
        Assert.False(doc.IsModified);
    }

    [Fact]
    public async Task SaveImageAsync_AsJpeg_EncodesJpegFile()
    {
        string pngPath = CreateTestImageFile("source.png", 100, 100, isPng: true);
        var doc = await _service.LoadImageDocumentAsync(pngPath);

        string jpegPath = Path.Combine(_tempDir, "saved.jpg");
        await _service.SaveImageAsync(doc, jpegPath);

        Assert.True(File.Exists(jpegPath));
        using var stream = File.OpenRead(jpegPath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.IsType<JpegBitmapDecoder>(decoder);
    }

    [Fact]
    public async Task SaveImageAsPdfAsync_CreatesValidPdfDocument()
    {
        string pngPath = CreateTestImageFile("for_pdf.png", 200, 150, isPng: true);
        var doc = await _service.LoadImageDocumentAsync(pngPath);
        var page = doc.Pages[0];

        var points = new StylusPointCollection
        {
            new StylusPoint(5, 5),
            new StylusPoint(20, 20)
        };
        page.InkStrokes.Add(new Stroke(points));

        string pdfPath = Path.Combine(_tempDir, "exported.pdf");
        await _service.SaveImageAsPdfAsync(doc, pdfPath);

        Assert.True(File.Exists(pdfPath));

        using var pdfDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Equal(1, pdfDoc.PageCount);
        Assert.False(doc.IsModified);
    }

    [Fact]
    public async Task LoadImageDocumentAsync_HighDpiImage_CalculatesPhysicalDimensionsFromDpi()
    {
        // 300 DPI の画像（300x300 ピクセル -> 1インチ = 72pt）
        string highDpiPath = CreateTestImageFileWithDpi("scan300.png", 300, 300, 300, 300);

        var doc = await _service.LoadImageDocumentAsync(highDpiPath);

        Assert.NotNull(doc);
        var page = doc.Pages[0];
        // 300px / 300 DPI * 72pt/inch = 72 pt
        Assert.Equal(72.0, page.Width, precision: 1);
        Assert.Equal(72.0, page.Height, precision: 1);
        Assert.True(page.IsImage);
    }

    private string CreateTestImageFile(string fileName, int width, int height, bool isPng)
    {
        return CreateTestImageFileWithDpi(fileName, width, height, 96, 96, isPng);
    }

    private string CreateTestImageFileWithDpi(string fileName, int width, int height, double dpiX, double dpiY, bool isPng = true)
    {
        string filePath = Path.Combine(_tempDir, fileName);

        var rtb = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.SkyBlue, null, new Rect(0, 0, width, height));
            dc.DrawLine(new Pen(Brushes.Red, 2), new Point(0, 0), new Point(width, height));
        }
        rtb.Render(visual);
        rtb.Freeze();

        BitmapEncoder encoder = isPng ? new PngBitmapEncoder() : new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);

        return filePath;
    }
}
