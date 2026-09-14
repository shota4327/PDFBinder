using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="StrokeCacheService"/> の単体テストクラス（Issue #43）
/// </summary>
public class StrokeCacheServiceTests
{
    private readonly StrokeCacheService _service = new();

    [Fact]
    public void RenderStrokeCache_ReturnsNull_WhenStrokesNullOrEmpty()
    {
        // Act & Assert
        Assert.Null(_service.RenderStrokeCache(null, 595, 842, 600, 800));
        Assert.Null(_service.RenderStrokeCache(new StrokeCollection(), 595, 842, 600, 800));
    }

    [Theory]
    [InlineData(0, 842, 600, 800)]
    [InlineData(595, 0, 600, 800)]
    [InlineData(595, 842, 0, 800)]
    [InlineData(595, 842, 600, 0)]
    public void RenderStrokeCache_ReturnsNull_WhenDimensionsInvalid(double pw, double ph, int rw, int rh)
    {
        // Arrange
        var strokes = CreateSampleStrokes();

        // Act
        var result = _service.RenderStrokeCache(strokes, pw, ph, rw, rh);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void RenderStrokeCache_ReturnsValidFrozenBitmap_WhenStrokesExist()
    {
        // Arrange
        var strokes = CreateSampleStrokes();

        // Act
        var result = _service.RenderStrokeCache(strokes, 500, 800, 1000, 1600);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1000, result.PixelWidth);
        Assert.Equal(1600, result.PixelHeight);
        Assert.True(result.IsFrozen);
    }

    [Fact]
    public void RenderStrokeCache_SupportsHighlighterStrokes()
    {
        // Arrange
        var strokes = new StrokeCollection();
        var points = new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(100.0, 100.0) };
        var attr = new DrawingAttributes
        {
            Color = Colors.Yellow,
            Width = 12,
            Height = 12,
            IsHighlighter = true
        };
        strokes.Add(new Stroke(points, attr));

        // Act
        var result = _service.RenderStrokeCache(strokes, 400, 600, 800, 1200);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(800, result.PixelWidth);
        Assert.Equal(1200, result.PixelHeight);
        Assert.True(result.IsFrozen);
    }

    private static StrokeCollection CreateSampleStrokes()
    {
        var strokes = new StrokeCollection();
        var points1 = new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(50.0, 50.0), new StylusPoint(100.0, 80.0) };
        strokes.Add(new Stroke(points1, new DrawingAttributes { Color = Colors.Black, Width = 2 }));

        var points2 = new StylusPointCollection { new StylusPoint(20.0, 30.0), new StylusPoint(60.0, 70.0) };
        strokes.Add(new Stroke(points2, new DrawingAttributes { Color = Colors.Red, Width = 4 }));

        return strokes;
    }
}
