using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="PdfiumRenderer"/> の単体テストクラス
/// </summary>
public class PdfiumRendererTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PdfiumRenderer _renderer;

    public PdfiumRendererTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_RendererTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _renderer = new PdfiumRenderer();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private string CreateSamplePdf(string fileName)
    {
        string filePath = Path.Combine(_testDirectory, fileName);
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(400);
        page.Height = XUnit.FromPoint(600);

        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(XBrushes.LightBlue, 10, 10, 200, 200);

        doc.Save(filePath);
        return filePath;
    }

    [Fact]
    public void CreateBlankPageBitmap_ReturnsValidWhiteBitmap()
    {
        // Act
        var bitmap = _renderer.CreateBlankPageBitmap(200, 300, PageRotation.Rotate0);

        // Assert
        Assert.NotNull(bitmap);
        Assert.Equal(200, bitmap.PixelWidth);
        Assert.Equal(300, bitmap.PixelHeight);
        Assert.True(bitmap.IsFrozen);
    }

    [Fact]
    public void CreateBlankPageBitmap_With90DegreeRotation_SwapsDimensions()
    {
        // Act
        var bitmap = _renderer.CreateBlankPageBitmap(200, 300, PageRotation.Rotate90);

        // Assert
        Assert.NotNull(bitmap);
        Assert.Equal(300, bitmap.PixelWidth);
        Assert.Equal(200, bitmap.PixelHeight);
    }

    [Fact]
    public async Task RenderPageAsync_ValidPdf_ReturnsRenderedBitmap()
    {
        // Arrange
        string pdfPath = CreateSamplePdf("test_render.pdf");

        // Act
        var bitmap = await _renderer.RenderPageAsync(pdfPath, 0, 200, 300, PageRotation.Rotate0);

        // Assert
        Assert.NotNull(bitmap);
        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
        Assert.True(bitmap.IsFrozen);
    }

    [Fact]
    public void CompositeStrokes_EmptyStrokes_ReturnsOriginalBaseImage()
    {
        // Arrange
        var baseBitmap = _renderer.CreateBlankPageBitmap(100, 150, PageRotation.Rotate0);
        var strokes = new StrokeCollection();

        // Act
        var result = _renderer.CompositeStrokes(baseBitmap, strokes, 100, 150);

        // Assert
        Assert.Same(baseBitmap, result);
    }

    [Fact]
    public void CompositeStrokes_WithStrokes_ReturnsCompositedFrozenBitmap()
    {
        // Arrange
        var baseBitmap = _renderer.CreateBlankPageBitmap(100, 150, PageRotation.Rotate0);
        var points = new StylusPointCollection
        {
            new StylusPoint(10, 10),
            new StylusPoint(50, 50)
        };
        var strokes = new StrokeCollection { new Stroke(points) };

        // Act
        var result = _renderer.CompositeStrokes(baseBitmap, strokes, 100, 150);

        // Assert
        Assert.NotNull(result);
        Assert.NotSame(baseBitmap, result);
        Assert.Equal(100, result.PixelWidth);
        Assert.Equal(150, result.PixelHeight);
        Assert.True(result.IsFrozen);
    }

    [Fact]
    public async Task RenderPageAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        string pdfPath = CreateSamplePdf("test_cancel.pdf");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _renderer.RenderPageAsync(pdfPath, 0, 200, 300, PageRotation.Rotate0, cts.Token);
        });
    }

    [Fact]
    public async Task RenderPageAsync_ConcurrentCalls_ExecuteSafelyWithoutCrashing()
    {
        // Arrange
        string pdfPath = CreateSamplePdf("test_concurrent.pdf");
        int concurrency = 10;
        var tasks = new List<Task<BitmapSource?>>();

        // Act: 10個のタスクを並行起動して同時レンダリング
        for (int i = 0; i < concurrency; i++)
        {
            var priority = (i % 2 == 0) ? RenderPriority.High : RenderPriority.Low;
            tasks.Add(Task.Run(() => _renderer.RenderPageAsync(pdfPath, 0, 200, 300, PageRotation.Rotate0, CancellationToken.None, priority)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert: すべてのタスクがクラッシュせず正常なBitmapSourceを生成できたことを検証
        Assert.Equal(concurrency, results.Length);
        foreach (var bitmap in results)
        {
            Assert.NotNull(bitmap);
            Assert.Equal(200, bitmap.PixelWidth);
            Assert.Equal(300, bitmap.PixelHeight);
            Assert.True(bitmap.IsFrozen);
        }
    }

    [Fact]
    public void CompositeOverWhite_ConvertsTransparentAndSemiTransparentPixelsProperly()
    {
        // Arrange: 3つのピクセル（完全透明、完全不透明黒、半透明黒(A=128)）
        byte[] bgra = new byte[]
        {
            0, 0, 0, 0,       // 完全透明 -> 白 (255, 255, 255, 255)
            10, 20, 30, 255,  // 完全不透明 -> そのまま (10, 20, 30, 255)
            0, 0, 0, 128      // 半透明黒 (Premultiplied: B=0, G=0, R=0, A=128) -> (127, 127, 127, 255)
        };

        // Act
        PdfiumRenderer.CompositeOverWhite(bgra);

        // Assert: 1つ目
        Assert.Equal(255, bgra[0]);
        Assert.Equal(255, bgra[1]);
        Assert.Equal(255, bgra[2]);
        Assert.Equal(255, bgra[3]);

        // Assert: 2つ目
        Assert.Equal(10, bgra[4]);
        Assert.Equal(20, bgra[5]);
        Assert.Equal(30, bgra[6]);
        Assert.Equal(255, bgra[7]);

        // Assert: 3つ目
        Assert.Equal(127, bgra[8]);
        Assert.Equal(127, bgra[9]);
        Assert.Equal(127, bgra[10]);
        Assert.Equal(255, bgra[11]);
    }

    [Fact]
    public async Task RenderPageAsync_TransparentPdf_ProducesOpaqueWhiteBackground()
    {
        // Arrange: 白背景矩形を描画しない透明PDF
        string transparentPdf = Path.Combine(_testDirectory, "transparent.pdf");
        using (var doc = new PdfDocument())
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(200);
            page.Height = XUnit.FromPoint(300);
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawRectangle(XBrushes.Black, 50, 50, 50, 50);
            }
            doc.Save(transparentPdf);
        }

        // Act
        var bitmap = await _renderer.RenderPageAsync(transparentPdf, 0, 200, 300, PageRotation.Rotate0);

        // Assert: ビットマップが不透明かつ背景が白
        Assert.NotNull(bitmap);
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        // (0, 0) は背景なので完全な白 (255, 255, 255, 255)
        Assert.Equal(255, pixels[0]); // B
        Assert.Equal(255, pixels[1]); // G
        Assert.Equal(255, pixels[2]); // R
        Assert.Equal(255, pixels[3]); // A

        // 全ピクセルで Alpha が 255 であること（透明ピクセルが 0 件）
        for (int i = 3; i < pixels.Length; i += 4)
        {
            Assert.Equal(255, pixels[i]);
        }
    }
}
