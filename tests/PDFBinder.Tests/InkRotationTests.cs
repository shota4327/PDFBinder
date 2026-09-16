using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.Core.Helpers;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// ページ回転時の手書きインク追従回転に関する単体テスト
/// </summary>
public class InkRotationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PdfService _pdfService;

    public InkRotationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_InkRotTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _pdfService = new PdfService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Theory]
    [InlineData(PageRotation.Rotate0, 10, 20, 100, 200, 10, 20)]
    [InlineData(PageRotation.Rotate90, 10, 20, 100, 200, 180, 10)] // H - y = 200 - 20 = 180, x = 10
    [InlineData(PageRotation.Rotate180, 10, 20, 100, 200, 90, 180)] // W - x = 100 - 10 = 90, H - y = 200 - 20 = 180
    [InlineData(PageRotation.Rotate270, 10, 20, 100, 200, 20, 90)] // y = 20, W - x = 100 - 10 = 90
    public void TransformPoint_CalculatesCorrectCoordinates(
        PageRotation delta,
        double origX,
        double origY,
        double width,
        double height,
        double expectedX,
        double expectedY)
    {
        // Act
        var (newX, newY) = InkTransformHelper.TransformPoint(origX, origY, delta, width, height);

        // Assert
        Assert.Equal(expectedX, newX, 4);
        Assert.Equal(expectedY, newY, 4);
    }

    [Fact]
    public void RotateStrokes_FourConsecutiveClockwiseRotations_RestoresOriginalCoordinates()
    {
        // Arrange: 縦向きページ (幅 400, 高さ 600)
        double width = 400;
        double height = 600;
        var points = new StylusPointCollection
        {
            new StylusPoint(50, 80, 0.75f),
            new StylusPoint(120, 300, 0.5f)
        };
        var stroke = new Stroke(points);
        stroke.DrawingAttributes.Width = 3.0;
        stroke.DrawingAttributes.Height = 7.0;
        var strokes = new StrokeCollection { stroke };

        // Act & Assert 1: 1回目の時計回り90度 (新幅 600, 新高 400)
        InkTransformHelper.RotateStrokes(strokes, PageRotation.Rotate90, width, height);
        Assert.Equal(600 - 80, strokes[0].StylusPoints[0].X, 4);
        Assert.Equal(50, strokes[0].StylusPoints[0].Y, 4);
        Assert.Equal(0.75f, strokes[0].StylusPoints[0].PressureFactor);
        Assert.Equal(7.0, strokes[0].DrawingAttributes.Width);
        Assert.Equal(3.0, strokes[0].DrawingAttributes.Height);

        // Act & Assert 2: 2回目の時計回り90度 (新幅 400, 新高 600)
        InkTransformHelper.RotateStrokes(strokes, PageRotation.Rotate90, height, width);
        Assert.Equal(400 - 50, strokes[0].StylusPoints[0].X, 4);
        Assert.Equal(600 - 80, strokes[0].StylusPoints[0].Y, 4);

        // Act & Assert 3: 3回目の時計回り90度 (新幅 600, 新高 400)
        InkTransformHelper.RotateStrokes(strokes, PageRotation.Rotate90, width, height);
        Assert.Equal(80, strokes[0].StylusPoints[0].X, 4);
        Assert.Equal(400 - 50, strokes[0].StylusPoints[0].Y, 4);

        // Act & Assert 4: 4回目の時計回り90度 (元に戻る)
        InkTransformHelper.RotateStrokes(strokes, PageRotation.Rotate90, height, width);
        Assert.Equal(50, strokes[0].StylusPoints[0].X, 4);
        Assert.Equal(80, strokes[0].StylusPoints[0].Y, 4);
        Assert.Equal(0.75f, strokes[0].StylusPoints[0].PressureFactor);
        Assert.Equal(3.0, strokes[0].DrawingAttributes.Width);
        Assert.Equal(7.0, strokes[0].DrawingAttributes.Height);
    }

    [Fact]
    public void PdfPageModel_RotateClockwise_RotatesInkAndUpdatesRotation()
    {
        // Arrange: 幅500, 高800のページ
        var page = new PdfPageModel
        {
            Width = 500,
            Height = 800,
            Rotation = PageRotation.Rotate0
        };
        var stroke = new Stroke(new StylusPointCollection
        {
            new StylusPoint(100, 200),
            new StylusPoint(150, 250)
        });
        page.InkStrokes.Add(stroke);
        page.IsModified = false;
        page.IsThumbnailDirty = false;

        // Act: 時計回りに回転
        page.RotateClockwise();

        // Assert: 90度回転され、インクも回転
        Assert.Equal(PageRotation.Rotate90, page.Rotation);
        Assert.True(page.IsModified);
        Assert.True(page.IsThumbnailDirty);
        Assert.Equal(800, page.DisplayWidth);
        Assert.Equal(500, page.DisplayHeight);

        // 元(100, 200) -> 90度回転後: (800 - 200, 100) = (600, 100)
        Assert.Equal(600, page.InkStrokes[0].StylusPoints[0].X, 4);
        Assert.Equal(100, page.InkStrokes[0].StylusPoints[0].Y, 4);
    }

    [Fact]
    public void RotatePageCommand_ExecuteAndUndo_RotatesAndRestoresInk()
    {
        // Arrange
        var undoRedoService = new UndoRedoService();
        var page = new PdfPageModel
        {
            Width = 600,
            Height = 900,
            Rotation = PageRotation.Rotate0
        };
        var stroke = new Stroke(new StylusPointCollection
        {
            new StylusPoint(100, 150)
        });
        page.InkStrokes.Add(stroke);

        var command = new RotatePageCommand(page, PageRotation.Rotate0, PageRotation.Rotate90);

        // Act 1: コマンド実行 (Execute)
        undoRedoService.Execute(command);

        // Assert 1: 回転反映
        Assert.Equal(PageRotation.Rotate90, page.Rotation);
        // 元(100, 150) -> (900 - 150, 100) = (750, 100)
        Assert.Equal(750, page.InkStrokes[0].StylusPoints[0].X, 4);
        Assert.Equal(100, page.InkStrokes[0].StylusPoints[0].Y, 4);

        // Act 2: 元に戻す (Undo)
        undoRedoService.Undo();

        // Assert 2: 逆回転で元の位置・向きに完全復帰
        Assert.Equal(PageRotation.Rotate0, page.Rotation);
        Assert.Equal(100, page.InkStrokes[0].StylusPoints[0].X, 4);
        Assert.Equal(150, page.InkStrokes[0].StylusPoints[0].Y, 4);

        // Act 3: やり直し (Redo)
        undoRedoService.Redo();

        // Assert 3: 再度回転反映
        Assert.Equal(PageRotation.Rotate90, page.Rotation);
        Assert.Equal(750, page.InkStrokes[0].StylusPoints[0].X, 4);
        Assert.Equal(100, page.InkStrokes[0].StylusPoints[0].Y, 4);
    }

    [Fact]
    public async Task RotatePageAndSave_ReloadsPreservedInkAtRotatedPosition()
    {
        // Arrange: 白紙ページを作成しインクを描画
        var doc = new PdfDocumentModel();
        var page = _pdfService.CreateBlankPage(500, 700);

        // (50, 100) 付近にストロークを描画
        var stroke = new Stroke(new StylusPointCollection
        {
            new StylusPoint(50, 100),
            new StylusPoint(150, 200)
        });
        stroke.DrawingAttributes.Color = Colors.Green;
        page.InkStrokes.Add(stroke);
        doc.AddPage(page);

        // Act 1: ページを時計回りに90度回転
        page.RotateClockwise();
        Assert.Equal(PageRotation.Rotate90, page.Rotation);
        // 回転後の座標: (700 - 100, 50) = (600, 50)
        Assert.Equal(600, page.InkStrokes[0].StylusPoints[0].X, 4);
        Assert.Equal(50, page.InkStrokes[0].StylusPoints[0].Y, 4);

        // Act 2: PDF保存
        string outputPath = Path.Combine(_testDirectory, "rotated_ink_test.pdf");
        await _pdfService.SaveDocumentAsync(doc, outputPath);

        // Act 3: 保存したPDFを再読込
        var reloaded = await _pdfService.LoadDocumentAsync(outputPath);
        Assert.Single(reloaded.Pages);
        var reloadedPage = reloaded.Pages[0];

        // Assert 3: 再読込後も回転角度およびインク座標が一致
        Assert.Equal(PageRotation.Rotate90, reloadedPage.Rotation);
        Assert.Single(reloadedPage.InkStrokes);
        Assert.Equal(600, reloadedPage.InkStrokes[0].StylusPoints[0].X, 1);
        Assert.Equal(50, reloadedPage.InkStrokes[0].StylusPoints[0].Y, 1);
        Assert.Equal(Colors.Green, reloadedPage.InkStrokes[0].DrawingAttributes.Color);
    }
}
