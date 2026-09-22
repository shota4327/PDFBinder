using System.IO;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 回転メタデータ（Rotate90/270）が付与された横向きPDF等の分割動作テストクラス（Issue #137 追加改修-2）
/// </summary>
public class SplitRotatedPdfTests : IDisposable
{
    private readonly string _dir;
    private readonly PdfService _service = new();
    private readonly PdfiumRenderer _renderer = new();

    public SplitRotatedPdfTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PDFBinder_SplitRot_{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Theory]
    [InlineData(PageRotation.Rotate0)]
    [InlineData(PageRotation.Rotate90)]
    [InlineData(PageRotation.Rotate180)]
    [InlineData(PageRotation.Rotate270)]
    public async Task SplitPagesHalfAsync_AllRotations_ProducesExpectedPages(PageRotation rotation)
    {
        // 800 x 600 (Rotate0/180) または 600 x 800 (Rotate90/270)
        bool isNativeLandscape = rotation is PageRotation.Rotate0 or PageRotation.Rotate180;
        double w = isNativeLandscape ? 800 : 600;
        double h = isNativeLandscape ? 600 : 800;

        string pdfPath = Path.Combine(_dir, $"test_rot_{(int)rotation}.pdf");
        using (var doc = new PdfDocument())
        {
            var p = doc.AddPage();
            p.Width = XUnit.FromPoint(w);
            p.Height = XUnit.FromPoint(h);
            p.Rotate = (int)rotation;
            using var gfx = XGraphics.FromPdfPage(p);
            gfx.DrawRectangle(XBrushes.Red, 50, 50, 100, 100);
            doc.Save(pdfPath);
        }

        var docModel = await _service.LoadDocumentAsync(pdfPath);
        var page = docModel.Pages[0];
        Assert.Equal(800, page.DisplayWidth);
        Assert.Equal(600, page.DisplayHeight);

        var splitPages = await _service.SplitPagesHalfAsync(new[] { page });
        Assert.Equal(2, splitPages.Count);

        // 画面上横長（800x600）を左右に2分割 -> 各ページの表示寸法は 400x600
        Assert.Equal(400, splitPages[0].DisplayWidth);
        Assert.Equal(600, splitPages[0].DisplayHeight);
        Assert.Equal(400, splitPages[1].DisplayWidth);
        Assert.Equal(600, splitPages[1].DisplayHeight);

        // PDFium で正常にレンダリングできること
        var bmp0 = await _renderer.RenderPageAsync(
            splitPages[0].SourceFilePath,
            splitPages[0].OriginalPageIndex,
            (int)splitPages[0].DisplayWidth,
            (int)splitPages[0].DisplayHeight,
            splitPages[0].RenderRotation);
        Assert.NotNull(bmp0);

        var bmp1 = await _renderer.RenderPageAsync(
            splitPages[1].SourceFilePath,
            splitPages[1].OriginalPageIndex,
            (int)splitPages[1].DisplayWidth,
            (int)splitPages[1].DisplayHeight,
            splitPages[1].RenderRotation);
        Assert.NotNull(bmp1);
    }
}
