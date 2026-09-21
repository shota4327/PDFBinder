using System.IO;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// PDFSharpでのXPdfForm描画・クリッピング動作検証テスト
/// </summary>
public class PdfSharpSplitExperimentTests : IDisposable
{
    private readonly string _testDirectory;

    public PdfSharpSplitExperimentTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_Exp_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public void TestXPdfForm_CanRenderHalfPage()
    {
        // 800 x 600 の横長PDFを作成（A3横の縮小版）
        string srcPath = Path.Combine(_testDirectory, "wide.pdf");
        using (var doc = new PdfDocument())
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(800);
            page.Height = XUnit.FromPoint(600);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.Red, 50, 50, 200, 200);
            gfx.DrawRectangle(XBrushes.Blue, 450, 50, 200, 200);
            doc.Save(srcPath);
        }

        // 左右2つ（400 x 600）に分割したPDFを作成
        string outPath = Path.Combine(_testDirectory, "split.pdf");
        using (var outDoc = new PdfDocument())
        {
            using var form = XPdfForm.FromFile(srcPath);
            form.PageNumber = 1;

            // 左ページ
            var leftPage = outDoc.AddPage();
            leftPage.Width = XUnit.FromPoint(400);
            leftPage.Height = XUnit.FromPoint(600);
            using (var gfx = XGraphics.FromPdfPage(leftPage))
            {
                gfx.DrawImage(form, 0, 0, 800, 600);
            }

            // 右ページ
            var rightPage = outDoc.AddPage();
            rightPage.Width = XUnit.FromPoint(400);
            rightPage.Height = XUnit.FromPoint(600);
            using (var gfx = XGraphics.FromPdfPage(rightPage))
            {
                gfx.DrawImage(form, -400, 0, 800, 600);
            }

            outDoc.Save(outPath);
        }

        Assert.True(File.Exists(outPath));
        using var verifyDoc = PdfSharp.Pdf.IO.PdfReader.Open(outPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, verifyDoc.PageCount);
        Assert.Equal(400, verifyDoc.Pages[0].Width.Point);
        Assert.Equal(600, verifyDoc.Pages[0].Height.Point);
        Assert.Equal(400, verifyDoc.Pages[1].Width.Point);
        Assert.Equal(600, verifyDoc.Pages[1].Height.Point);
    }

    [Theory]
    [InlineData(PageRotation.Rotate0)]
    [InlineData(PageRotation.Rotate90)]
    [InlineData(PageRotation.Rotate180)]
    [InlineData(PageRotation.Rotate270)]
    public void TestXPdfForm_RotatedPage_RendersCorrectly(PageRotation rotation)
    {
        // 800 x 600 の横長PDFを作成
        string srcPath = Path.Combine(_testDirectory, $"src_{rotation}.pdf");
        using (var doc = new PdfDocument())
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(800);
            page.Height = XUnit.FromPoint(600);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.Green, 50, 50, 200, 200);
            doc.Save(srcPath);
        }

        double width = 800;
        double height = 600;
        double dispWidth = rotation is PageRotation.Rotate90 or PageRotation.Rotate270 ? height : width;
        double dispHeight = rotation is PageRotation.Rotate90 or PageRotation.Rotate270 ? width : height;

        bool isLandscape = dispWidth >= dispHeight;
        double splitWidth = isLandscape ? dispWidth / 2 : dispWidth;
        double splitHeight = isLandscape ? dispHeight : dispHeight / 2;

        string outPath = Path.Combine(_testDirectory, $"out_{rotation}.pdf");
        using (var outDoc = new PdfDocument())
        {
            using var form = XPdfForm.FromFile(srcPath);
            form.PageNumber = 1;

            for (int part = 0; part < 2; part++)
            {
                double offsetX = isLandscape ? part * splitWidth : 0;
                double offsetY = isLandscape ? 0 : part * splitHeight;

                var newPage = outDoc.AddPage();
                newPage.Width = XUnit.FromPoint(splitWidth);
                newPage.Height = XUnit.FromPoint(splitHeight);

                using var gfx = XGraphics.FromPdfPage(newPage);
                gfx.TranslateTransform(-offsetX, -offsetY);

                switch (rotation)
                {
                    case PageRotation.Rotate0:
                        break;
                    case PageRotation.Rotate90:
                        gfx.TranslateTransform(height, 0);
                        gfx.RotateAtTransform(90, new XPoint(0, 0));
                        break;
                    case PageRotation.Rotate180:
                        gfx.TranslateTransform(width, height);
                        gfx.RotateAtTransform(180, new XPoint(0, 0));
                        break;
                    case PageRotation.Rotate270:
                        gfx.TranslateTransform(0, width);
                        gfx.RotateAtTransform(270, new XPoint(0, 0));
                        break;
                }

                gfx.DrawImage(form, 0, 0, width, height);
            }

            outDoc.Save(outPath);
        }

        using var verifyDoc = PdfSharp.Pdf.IO.PdfReader.Open(outPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, verifyDoc.PageCount);
        Assert.Equal(splitWidth, verifyDoc.Pages[0].Width.Point);
        Assert.Equal(splitHeight, verifyDoc.Pages[0].Height.Point);
        Assert.Equal(splitWidth, verifyDoc.Pages[1].Width.Point);
        Assert.Equal(splitHeight, verifyDoc.Pages[1].Height.Point);
    }
}
