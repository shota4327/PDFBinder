using System.IO;
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
}
