using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 手書きストロークの再編集可能保存・復元に関する単体テスト
/// </summary>
public class ReEditableInkAnnotationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PdfService _service;
    private readonly PdfiumRenderer _renderer;

    public ReEditableInkAnnotationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_InkTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _service = new PdfService();
        _renderer = new PdfiumRenderer();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task SaveAndReload_PreservesInkStrokesWithAllAttributes()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        var page = _service.CreateBlankPage(500, 700);

        // 1本目: ペン (青、太さ2.0)
        var points1 = new StylusPointCollection
        {
            new StylusPoint(10, 20),
            new StylusPoint(50, 80),
            new StylusPoint(100, 150)
        };
        var stroke1 = new Stroke(points1);
        stroke1.DrawingAttributes.Color = Colors.Blue;
        stroke1.DrawingAttributes.Width = 2.0;
        stroke1.DrawingAttributes.IsHighlighter = false;
        page.InkStrokes.Add(stroke1);

        // 2本目: 蛍光ペン (黄、太さ12.0)
        var points2 = new StylusPointCollection
        {
            new StylusPoint(200, 100),
            new StylusPoint(350, 100)
        };
        var stroke2 = new Stroke(points2);
        stroke2.DrawingAttributes.Color = Colors.Yellow;
        stroke2.DrawingAttributes.Width = 12.0;
        stroke2.DrawingAttributes.IsHighlighter = true;
        page.InkStrokes.Add(stroke2);

        doc.AddPage(page);
        string outputPath = Path.Combine(_testDirectory, "re_editable_sample.pdf");

        // Act
        await _service.SaveDocumentAsync(doc, outputPath);

        // Assert
        Assert.True(File.Exists(outputPath));
        var reloaded = await _service.LoadDocumentAsync(outputPath);
        Assert.Equal(1, reloaded.PageCount);

        var reloadedPage = reloaded.Pages[0];
        Assert.Equal(2, reloadedPage.InkStrokes.Count);

        // ストローク1の検証
        var rStroke1 = reloadedPage.InkStrokes[0];
        Assert.Equal(3, rStroke1.StylusPoints.Count);
        Assert.Equal(Colors.Blue, rStroke1.DrawingAttributes.Color);
        Assert.Equal(2.0, rStroke1.DrawingAttributes.Width, 1);
        Assert.False(rStroke1.DrawingAttributes.IsHighlighter);

        // ストローク2の検証
        var rStroke2 = reloadedPage.InkStrokes[1];
        Assert.Equal(2, rStroke2.StylusPoints.Count);
        Assert.Equal(Colors.Yellow, rStroke2.DrawingAttributes.Color);
        Assert.Equal(12.0, rStroke2.DrawingAttributes.Width, 1);
        Assert.True(rStroke2.DrawingAttributes.IsHighlighter);
    }

    [Fact]
    public async Task EraseStrokesAndResave_UpdatesInkStrokesAndPdfFile()
    {
        // Arrange: 2本のストロークを保存
        var doc = new PdfDocumentModel();
        var page = _service.CreateBlankPage(500, 700);

        var stroke1 = new Stroke(new StylusPointCollection { new StylusPoint(10, 10), new StylusPoint(50, 50) });
        var stroke2 = new Stroke(new StylusPointCollection { new StylusPoint(100, 100), new StylusPoint(200, 200) });
        page.InkStrokes.Add(stroke1);
        page.InkStrokes.Add(stroke2);
        doc.AddPage(page);

        string outputPath = Path.Combine(_testDirectory, "erase_test.pdf");
        await _service.SaveDocumentAsync(doc, outputPath);

        // Act 1: 再読込して1本消去（消しゴム相当）
        var loaded = await _service.LoadDocumentAsync(outputPath);
        Assert.Equal(2, loaded.Pages[0].InkStrokes.Count);
        loaded.Pages[0].InkStrokes.RemoveAt(0);
        await _service.SaveDocumentAsync(loaded, outputPath);

        // Assert 1: 1本のみ残っていること
        var reloaded1 = await _service.LoadDocumentAsync(outputPath);
        Assert.Single(reloaded1.Pages[0].InkStrokes);
        Assert.Equal(100, reloaded1.Pages[0].InkStrokes[0].StylusPoints[0].X, 1);

        // Act 2: 全て消去して上書き保存
        reloaded1.Pages[0].InkStrokes.Clear();
        await _service.SaveDocumentAsync(reloaded1, outputPath);

        // Assert 2: 0本になり注釈も削除されていること
        var reloaded2 = await _service.LoadDocumentAsync(outputPath);
        Assert.Empty(reloaded2.Pages[0].InkStrokes);
    }

    [Fact]
    public async Task ThirdPartyAnnotation_PreservedWhenSavingAndReloadingBinderInk()
    {
        // Arrange: 外部PDF（リンク注釈付き）を作成
        string pdfPath = Path.Combine(_testDirectory, "annot_coexist.pdf");
        using (var rawDoc = new PdfDocument())
        {
            var rawPage = rawDoc.AddPage();
            rawPage.Width = XUnit.FromPoint(400);
            rawPage.Height = XUnit.FromPoint(600);
            // リンク注釈を追加
            var linkAnnot = rawPage.AddWebLink(
                new PdfRectangle(new XRect(10, 10, 100, 30)),
                "https://example.com");
            rawDoc.Save(pdfPath);
        }

        // Act: 読み込んで手書きストロークを追加して保存
        var doc = await _service.LoadDocumentAsync(pdfPath);
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(20, 20), new StylusPoint(60, 60) });
        doc.Pages[0].InkStrokes.Add(stroke);

        string savedPath = Path.Combine(_testDirectory, "annot_coexist_saved.pdf");
        await _service.SaveDocumentAsync(doc, savedPath);

        // Assert: 再読込後、ストロークが復元され、リンク注釈も維持されていること
        var reloaded = await _service.LoadDocumentAsync(savedPath);
        Assert.Single(reloaded.Pages[0].InkStrokes);

        var interactiveData = await _renderer.ExtractInteractiveDataAsync(
            savedPath, 0, 400, 600, PageRotation.Rotate0);
        Assert.Single(interactiveData.Links);
        Assert.Equal("https://example.com", interactiveData.Links[0].Uri);
    }

    [Fact]
    public async Task RenderPageAsync_ExcludesBinderInkFromRasterizedBackground()
    {
        // Arrange: 白紙ページに太い黒手書きストロークを保存
        var doc = new PdfDocumentModel();
        var page = _service.CreateBlankPage(200, 200);
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(0, 0), new StylusPoint(200, 200) });
        stroke.DrawingAttributes.Color = Colors.Black;
        stroke.DrawingAttributes.Width = 30;
        page.InkStrokes.Add(stroke);
        doc.AddPage(page);

        string pdfPath = Path.Combine(_testDirectory, "bg_isolate_test.pdf");
        await _service.SaveDocumentAsync(doc, pdfPath);

        // Act: PdfiumRenderer で背景をレンダリング
        var bitmap = await _renderer.RenderPageAsync(pdfPath, 0, 200, 200, PageRotation.Rotate0);

        // Assert: レンダリングされた画像は白紙（手書き線が焼き込まれていないこと）
        Assert.NotNull(bitmap);
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        // 手書きストローク（黒）が除外され、背景全体が純白（すべてのバイトが255）であること
        Assert.All(pixels, b => Assert.Equal(255, b));
    }
}
