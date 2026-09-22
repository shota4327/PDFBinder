using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="PdfService.SplitPagesHalfAsync"/> の単体テストクラス（Issue #137）
/// </summary>
public class PdfServiceSplitHalfTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PdfService _service;

    public PdfServiceSplitHalfTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_SplitTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _service = new PdfService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task SplitPagesHalfAsync_EmptyList_ReturnsEmptyList()
    {
        var result = await _service.SplitPagesHalfAsync(new List<PdfPageModel>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task SplitPagesHalfAsync_BlankPage_Landscape_SplitsHorizontally()
    {
        // 横長A3白紙ページ（842 x 595）
        var page = new PdfPageModel
        {
            Width = 842,
            Height = 595,
            Rotation = PageRotation.Rotate0
        };

        var result = await _service.SplitPagesHalfAsync(new[] { page });

        Assert.Equal(2, result.Count);
        // 左右分割 -> 幅421、高さ595（A4縦相当）
        Assert.Equal(421, result[0].Width);
        Assert.Equal(595, result[0].Height);
        Assert.True(result[0].IsBlankPage);
        Assert.Equal(1, result[0].PageNumber);

        Assert.Equal(421, result[1].Width);
        Assert.Equal(595, result[1].Height);
        Assert.True(result[1].IsBlankPage);
        Assert.Equal(2, result[1].PageNumber);
    }

    [Fact]
    public async Task SplitPagesHalfAsync_BlankPage_Portrait_SplitsVertically()
    {
        // 縦長A3白紙ページ（595 x 842）
        var page = new PdfPageModel
        {
            Width = 595,
            Height = 842,
            Rotation = PageRotation.Rotate0
        };

        var result = await _service.SplitPagesHalfAsync(new[] { page });

        Assert.Equal(2, result.Count);
        // 上下分割 -> 幅595、高さ421（A4横相当）
        Assert.Equal(595, result[0].Width);
        Assert.Equal(421, result[0].Height);
        Assert.True(result[0].IsBlankPage);
        Assert.Equal(1, result[0].PageNumber);

        Assert.Equal(595, result[1].Width);
        Assert.Equal(421, result[1].Height);
        Assert.True(result[1].IsBlankPage);
        Assert.Equal(2, result[1].PageNumber);
    }

    [Fact]
    public async Task SplitPagesHalfAsync_PdfFile_Landscape_SplitsHorizontally()
    {
        // 800 x 600 の横長PDFを作成
        string pdfPath = Path.Combine(_testDirectory, "landscape.pdf");
        using (var doc = new PdfDocument())
        {
            var p = doc.AddPage();
            p.Width = XUnit.FromPoint(800);
            p.Height = XUnit.FromPoint(600);
            using var gfx = XGraphics.FromPdfPage(p);
            gfx.DrawRectangle(XBrushes.Red, 50, 50, 100, 100);
            doc.Save(pdfPath);
        }

        var page = new PdfPageModel
        {
            SourceFilePath = pdfPath,
            OriginalPageIndex = 0,
            Width = 800,
            Height = 600,
            Rotation = PageRotation.Rotate0
        };

        var result = await _service.SplitPagesHalfAsync(new[] { page });

        Assert.Equal(2, result.Count);
        Assert.Equal(400, result[0].Width);
        Assert.Equal(600, result[0].Height);
        Assert.False(result[0].IsBlankPage);
        Assert.True(File.Exists(result[0].SourceFilePath));
        Assert.Equal(0, result[0].OriginalPageIndex);

        Assert.Equal(400, result[1].Width);
        Assert.Equal(600, result[1].Height);
        Assert.False(result[1].IsBlankPage);
        Assert.Equal(1, result[1].OriginalPageIndex);
    }

    [Fact]
    public async Task SplitPagesHalfAsync_WithStrokes_DistributesStrokesToBothPages()
    {
        // 横長白紙ページ（800 x 600）に境界(X=400)を跨ぐストローク
        var page = new PdfPageModel
        {
            Width = 800,
            Height = 600,
            Rotation = PageRotation.Rotate0
        };
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(200, 100), new StylusPoint(600, 100) });
        page.InkStrokes.Add(stroke);

        var result = await _service.SplitPagesHalfAsync(new[] { page });

        Assert.Equal(2, result.Count);
        Assert.Single(result[0].InkStrokes);
        Assert.Single(result[1].InkStrokes);

        // 左ページ: (200, 100) -> (400, 100)
        Assert.Equal(200, result[0].InkStrokes[0].StylusPoints[0].X);
        Assert.Equal(400, result[0].InkStrokes[0].StylusPoints[1].X);

        // 右ページ: (400-400, 100) -> (600-400, 100) => (0, 100) -> (200, 100)
        Assert.Equal(0, result[1].InkStrokes[0].StylusPoints[0].X);
        Assert.Equal(200, result[1].InkStrokes[0].StylusPoints[1].X);
    }

    [Fact]
    public async Task SplitPagesHalfAsync_WithRotation_LandscapeDisplay_SplitsHorizontally()
    {
        // 元は 600 x 800（縦長）だが Rotate90 で画面上は 800 x 600（横長）
        string pdfPath = Path.Combine(_testDirectory, "rot.pdf");
        using (var doc = new PdfDocument())
        {
            var p = doc.AddPage();
            p.Width = XUnit.FromPoint(600);
            p.Height = XUnit.FromPoint(800);
            doc.Save(pdfPath);
        }

        var page = new PdfPageModel
        {
            SourceFilePath = pdfPath,
            OriginalPageIndex = 0,
            Width = 600,
            Height = 800,
            Rotation = PageRotation.Rotate90
        };

        var result = await _service.SplitPagesHalfAsync(new[] { page });

        Assert.Equal(2, result.Count);
        // 画面上横長（DisplayWidth=800, DisplayHeight=600）なので左右分割
        Assert.Equal(400, result[0].DisplayWidth);
        Assert.Equal(600, result[0].DisplayHeight);
        Assert.Equal(PageRotation.Rotate90, result[0].Rotation);

        Assert.Equal(400, result[1].DisplayWidth);
        Assert.Equal(600, result[1].DisplayHeight);
        Assert.Equal(PageRotation.Rotate90, result[1].Rotation);
    }
}
