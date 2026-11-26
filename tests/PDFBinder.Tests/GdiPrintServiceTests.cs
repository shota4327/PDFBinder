using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.App.Services;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// GdiPrintService、PrintSettings.PrintAsImage および PrintPageSource の単体テスト
/// </summary>
public class GdiPrintServiceTests
{
    private static StrokeCollection CreateDummyStrokeCollection()
    {
        var points = new StylusPointCollection(new[]
        {
            new StylusPoint(10, 10),
            new StylusPoint(50, 50)
        });
        return new StrokeCollection(new[] { new Stroke(points) });
    }

    [Fact]
    public void PrintSettings_PrintAsImage_DefaultsFalseAndCopiesResetsProperly()
    {
        var settings = new PrintSettings();
        Assert.False(settings.PrintAsImage);

        settings.PrintAsImage = true;
        var cloned = settings.Clone();
        Assert.True(cloned.PrintAsImage);

        var copied = new PrintSettings();
        copied.CopyFrom(settings);
        Assert.True(copied.PrintAsImage);

        settings.ResetToDefault();
        Assert.False(settings.PrintAsImage);
    }

    [Fact]
    public void DeterminePageRenderKind_SelectsCorrectModeForAllPageTypes()
    {
        var vectorSettings = new PrintSettings { PrintAsImage = false };
        var imageSettings = new PrintSettings { PrintAsImage = true };

        // 白紙（インクなし） -> Blank
        var blankNoInk = new PrintPageSource(null, -1, null, 595, 842, PageRotation.Rotate0, true, false, false, null);
        Assert.Equal(PagePrintRenderKind.Blank, GdiPrintService.DeterminePageRenderKind(blankNoInk, vectorSettings));
        Assert.Equal(PagePrintRenderKind.Blank, GdiPrintService.DeterminePageRenderKind(null, vectorSettings));

        // 白紙（インクあり） -> Raster
        var blankWithInk = new PrintPageSource(null, -1, null, 595, 842, PageRotation.Rotate0, true, false, false, CreateDummyStrokeCollection());
        Assert.Equal(PagePrintRenderKind.Raster, GdiPrintService.DeterminePageRenderKind(blankWithInk, vectorSettings));

        // 通常 PDF（インクなし・PrintAsImage=false） -> PdfVector
        var normalPdf = new PrintPageSource("doc.pdf", 0, null, 595, 842, PageRotation.Rotate0, false, false, false, null);
        Assert.Equal(PagePrintRenderKind.PdfVector, GdiPrintService.DeterminePageRenderKind(normalPdf, vectorSettings));

        // 通常 PDF（インクなし・PrintAsImage=true） -> Raster
        Assert.Equal(PagePrintRenderKind.Raster, GdiPrintService.DeterminePageRenderKind(normalPdf, imageSettings));

        // 手書きインク付き PDF -> Raster
        var inkPdf = new PrintPageSource("doc.pdf", 0, null, 595, 842, PageRotation.Rotate0, false, false, false, CreateDummyStrokeCollection());
        Assert.Equal(PagePrintRenderKind.Raster, GdiPrintService.DeterminePageRenderKind(inkPdf, vectorSettings));

        // 画像ページ -> Raster
        var imagePage = new PrintPageSource("photo.png", 0, null, 800, 600, PageRotation.Rotate0, false, true, false, null);
        Assert.Equal(PagePrintRenderKind.Raster, GdiPrintService.DeterminePageRenderKind(imagePage, vectorSettings));
    }

    [Fact]
    public void ExecutePlacementWithFallback_VectorFailureAutomaticallyFallsBackToRaster()
    {
        // ベクター成功時はラスターを呼ばない
        bool rasterCalled1 = false;
        bool ok1 = GdiPrintService.ExecutePlacementWithFallback(
            PagePrintRenderKind.PdfVector,
            () => true,
            () => rasterCalled1 = true);
        Assert.True(ok1);
        Assert.False(rasterCalled1);

        // ベクター失敗 (false) 時は自動でラスターへフォールバック
        bool rasterCalled2 = false;
        bool ok2 = GdiPrintService.ExecutePlacementWithFallback(
            PagePrintRenderKind.PdfVector,
            () => false,
            () => rasterCalled2 = true);
        Assert.True(ok2);
        Assert.True(rasterCalled2);

        // ベクター例外時も自動でラスターへフォールバック
        bool rasterCalled3 = false;
        bool ok3 = GdiPrintService.ExecutePlacementWithFallback(
            PagePrintRenderKind.PdfVector,
            () => throw new InvalidOperationException("PDFium error"),
            () => rasterCalled3 = true);
        Assert.True(ok3);
        Assert.True(rasterCalled3);
    }

    [Fact]
    public void PrintPageSource_FromPageModel_ClonesStrokesForThreadSafety()
    {
        var page = new PdfPageModel
        {
            SourceFilePath = "sample.pdf",
            OriginalPageIndex = 2,
            Width = 595,
            Height = 842,
            OriginalRotation = PageRotation.Rotate90,
            Rotation = PageRotation.Rotate180,
            InkStrokes = CreateDummyStrokeCollection()
        };

        var snapshot = PrintPageSource.FromPageModel(page);
        page.InkStrokes.Clear();

        Assert.Equal("sample.pdf", snapshot.SourceFilePath);
        Assert.Equal(2, snapshot.OriginalPageIndex);
        Assert.Equal(PageRotation.Rotate90, snapshot.RenderRotation);
        Assert.True(snapshot.HasInk);
        Assert.Single(snapshot.InkStrokes);
    }

    [Fact]
    public void ToPdfiumRotateCode_MapsAllRotationsCorrectly()
    {
        Assert.Equal(0, GdiPrintService.ToPdfiumRotateCode(PageRotation.Rotate0));
        Assert.Equal(1, GdiPrintService.ToPdfiumRotateCode(PageRotation.Rotate90));
        Assert.Equal(2, GdiPrintService.ToPdfiumRotateCode(PageRotation.Rotate180));
        Assert.Equal(3, GdiPrintService.ToPdfiumRotateCode(PageRotation.Rotate270));
    }
}
