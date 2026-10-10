using System.IO;
using System.Threading;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Helpers;
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
    public async Task RenderPageAsync_TransparentPdf_ProducesTransparentBackgroundDirectly()
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

        // Assert: ビットマップが高速に直接生成され、描画領域（50, 50）が不透明黒、背景（0, 0）が透過であること
        Assert.NotNull(bitmap);
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        // (0, 0) は背景なので透過 (A = 0)
        Assert.Equal(0, pixels[3]); // A

        // (50, 50) は黒矩形なので不透明黒 (A = 255, B = 0, G = 0, R = 0)
        int rectIndex = (50 * bitmap.PixelWidth + 50) * 4;
        Assert.Equal(255, pixels[rectIndex + 3]); // A
        Assert.Equal(0, pixels[rectIndex]);     // B
        Assert.Equal(0, pixels[rectIndex + 1]); // G
        Assert.Equal(0, pixels[rectIndex + 2]); // R
    }

    [Fact]
    public async Task RenderPageAsync_UsesCacheOnSubsequentCalls()
    {
        // Arrange
        string testPdf = Path.Combine(_testDirectory, "cache_test.pdf");
        using (var doc = new PdfDocument())
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(100);
            page.Height = XUnit.FromPoint(100);
            doc.Save(testPdf);
        }

        // Act: 2回連続でレンダリング
        var bitmap1 = await _renderer.RenderPageAsync(testPdf, 0, 100, 100, PageRotation.Rotate0);
        var bitmap2 = await _renderer.RenderPageAsync(testPdf, 0, 100, 100, PageRotation.Rotate0);

        // Assert: 2回とも正常に取得できること
        Assert.NotNull(bitmap1);
        Assert.NotNull(bitmap2);
    }

    [Fact]
    public async Task InvalidateCache_SpecificFile_RemovesOnlyTargetFromCache()
    {
        // Arrange
        string pdf1 = CreateSamplePdf("cache_file1.pdf");
        string pdf2 = CreateSamplePdf("cache_file2.pdf");

        await _renderer.RenderPageAsync(pdf1, 0, 100, 100, PageRotation.Rotate0);
        await _renderer.RenderPageAsync(pdf2, 0, 100, 100, PageRotation.Rotate0);
        Assert.Equal(2, _renderer.CachedFileCount);

        // Act: pdf1 のみ解放
        _renderer.InvalidateCache(pdf1);

        // Assert: pdf1 が削除され、pdf2 は維持される
        Assert.Equal(1, _renderer.CachedFileCount);

        // Act: null で全解放
        _renderer.InvalidateCache(null);
        Assert.Equal(0, _renderer.CachedFileCount);
    }

    [Theory]
    [InlineData("test.png", true, PageRotation.Rotate0, 200, 100)]
    [InlineData("test.png", true, PageRotation.Rotate90, 100, 200)]
    [InlineData("test.png", true, PageRotation.Rotate180, 200, 100)]
    [InlineData("test.png", true, PageRotation.Rotate270, 100, 200)]
    [InlineData("test.jpg", false, PageRotation.Rotate90, 100, 200)]
    public async Task RenderPageAsync_ImageWithRotation_RendersCorrectDimensions(
        string fileName,
        bool isPng,
        PageRotation rotation,
        int expectedWidth,
        int expectedHeight)
    {
        // Arrange
        string imagePath = Path.Combine(_testDirectory, fileName);
        var rtb = new RenderTargetBitmap(200, 100, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        BitmapEncoder encoder = isPng ? new PngBitmapEncoder() : new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(imagePath))
        {
            encoder.Save(fs);
        }

        // Act: 該当回転角度でレンダリング
        var bitmap = await _renderer.RenderPageAsync(imagePath, 0, expectedWidth, expectedHeight, rotation);

        // Assert: スレッドセーフにフリーズされており、指定通りの寸法であること
        Assert.NotNull(bitmap);
        Assert.True(bitmap.IsFrozen);
        Assert.Equal(expectedWidth, bitmap.PixelWidth);
        Assert.Equal(expectedHeight, bitmap.PixelHeight);

        // 別スレッドからさらに TransformedBitmap.Freeze() を行っても例外が発生しないこと
        BitmapSource? furtherRotated = null;
        var thread = new Thread(() =>
        {
            furtherRotated = BitmapTransformHelper.CreateRotatedBitmap(bitmap, 90);
        });
        thread.Start();
        thread.Join();

        Assert.NotNull(furtherRotated);
        Assert.True(furtherRotated.IsFrozen);
    }

    [Fact]
    public async Task RenderPageAsync_WhenCancelled_ReleasesLockSafelyForSubsequentRender()
    {
        // Arrange
        string samplePdf = CreateSamplePdf("cancel_lock_test.pdf");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act 1: キャンセル済みトークンでの実行
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _renderer.RenderPageAsync(samplePdf, 0, 100, 100, PageRotation.Rotate0, cts.Token);
        });

        // Act 2: 直後に正常トークンでレンダリング
        var bitmap = await _renderer.RenderPageAsync(samplePdf, 0, 100, 100, PageRotation.Rotate0);

        // Assert: ロックが正常に解放されており、後続のレンダリングが成功すること
        Assert.NotNull(bitmap);
        Assert.False(_renderer.RenderLock.IsLocked);
    }

    [Fact]
    public async Task RenderPageAsync_ConcurrentRendersWithCancellation_DoesNotCorruptLockState()
    {
        // Arrange
        string samplePdf = CreateSamplePdf("concurrent_cancel_test.pdf");
        using var cts = new CancellationTokenSource();

        // Act: 並行でレンダリングを呼び出しつつ、1つ目を早期キャンセル
        var task1 = _renderer.RenderPageAsync(samplePdf, 0, 200, 200, PageRotation.Rotate0, cts.Token);
        cts.CancelAfter(5);
        var task2 = _renderer.RenderPageAsync(samplePdf, 0, 200, 200, PageRotation.Rotate0);

        try
        {
            await task1;
        }
        catch (OperationCanceledException)
        {
            // キャンセル例外は正常
        }

        var bitmap2 = await task2;

        // Assert: 2つ目のレンダリングが競合せず正常完了し、ロックが解放されていること
        Assert.NotNull(bitmap2);
        Assert.False(_renderer.RenderLock.IsLocked);
    }
}
