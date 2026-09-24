using System.Threading;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.Controls;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 手書きストロークの重なり順（Z-Order）保持および蛍光ペン重ね塗り濃色化の単体テスト（Issue #108）
/// </summary>
public class StrokeOrderAndOverlapTests
{
    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);

        Assert.True(finished, "STA thread timed out");
        if (exception != null)
        {
            throw exception;
        }
    }

    [Fact]
    public void HighlighterTool_ConfiguresSemiTransparentDrawingAttributes_AndNotIsHighlighter()
    {
        RunInSta(() =>
        {
            // Arrange
            var canvas = new EditorInkCanvas
            {
                DrawingColor = Color.FromRgb(0xEA, 0xB3, 0x08), // 黄色プリセット
                StrokeThickness = 12.0
            };

            // Act
            canvas.ToolMode = EditorToolMode.Highlighter;

            // Assert: IsHighlighter = false かつ Alpha = 120（約47%）が設定される
            Assert.False(canvas.DefaultDrawingAttributes.IsHighlighter);
            Assert.Equal(120, canvas.DefaultDrawingAttributes.Color.A);
            Assert.Equal(0xEA, canvas.DefaultDrawingAttributes.Color.R);
            Assert.Equal(0xB3, canvas.DefaultDrawingAttributes.Color.G);
            Assert.Equal(0x08, canvas.DefaultDrawingAttributes.Color.B);
        });
    }

    [Fact]
    public void SwitchingBackToPen_RestoresOpaqueColor()
    {
        RunInSta(() =>
        {
            // Arrange
            var canvas = new EditorInkCanvas
            {
                DrawingColor = Colors.Red,
                StrokeThickness = 2.0
            };

            // Act: 蛍光ペンに変更後、再びペンに変更
            canvas.ToolMode = EditorToolMode.Highlighter;
            Assert.Equal(120, canvas.DefaultDrawingAttributes.Color.A);

            canvas.ToolMode = EditorToolMode.Pen;

            // Assert: ペン時は不透明（Alpha = 255）に戻る
            Assert.False(canvas.DefaultDrawingAttributes.IsHighlighter);
            Assert.Equal(255, canvas.DefaultDrawingAttributes.Color.A);
            Assert.Equal(Colors.Red.R, canvas.DefaultDrawingAttributes.Color.R);
        });
    }

    [Fact]
    public void PaletteColorChange_InHighlighterMode_PreservesAlpha120()
    {
        RunInSta(() =>
        {
            // Arrange
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Highlighter
            };

            // Act: 蛍光ペンモード中にパレット色を変更（不透明な青色が渡された場合）
            canvas.DrawingColor = Color.FromRgb(59, 130, 246);

            // Assert: アルファ値 120 が維持されつつ RGB が更新される
            Assert.False(canvas.DefaultDrawingAttributes.IsHighlighter);
            Assert.Equal(120, canvas.DefaultDrawingAttributes.Color.A);
            Assert.Equal(59, canvas.DefaultDrawingAttributes.Color.R);
            Assert.Equal(130, canvas.DefaultDrawingAttributes.Color.G);
            Assert.Equal(246, canvas.DefaultDrawingAttributes.Color.B);
        });
    }

    [Fact]
    public void OverlappingHighlighter_DarkensOpacity()
    {
        // Arrange: 1本の蛍光ペンストロークと、2本重ねた蛍光ペンストロークを準備
        var service = new StrokeCacheService();
        var attrSingle = new DrawingAttributes
        {
            Color = Color.FromArgb(120, 255, 255, 0),
            Width = 20,
            Height = 20,
            IsHighlighter = false
        };

        var points1 = new StylusPointCollection { new StylusPoint(50, 50), new StylusPoint(50, 60) };
        var strokesSingle = new StrokeCollection { new Stroke(points1, attrSingle) };

        var strokesDouble = new StrokeCollection
        {
            new Stroke(points1, attrSingle.Clone()),
            new Stroke(points1, attrSingle.Clone())
        };

        // Act: それぞれを透過ビットマップへレンダリング
        var bmpSingle = service.RenderStrokeCache(strokesSingle, 100, 100, 100, 100);
        var bmpDouble = service.RenderStrokeCache(strokesDouble, 100, 100, 100, 100);

        Assert.NotNull(bmpSingle);
        Assert.NotNull(bmpDouble);

        // 重心位置（50, 55）のピクセル値を取得
        byte[] pixelSingle = new byte[4];
        byte[] pixelDouble = new byte[4];
        bmpSingle.CopyPixels(new Int32Rect(50, 55, 1, 1), pixelSingle, 4, 0);
        bmpDouble.CopyPixels(new Int32Rect(50, 55, 1, 1), pixelDouble, 4, 0);

        byte alphaSingle = pixelSingle[3];
        byte alphaDouble = pixelDouble[3];

        // Assert: 2回重ねたストロークのアルファ値（不透明度）が1回のみの場合より濃くなっていること
        Assert.True(alphaDouble > alphaSingle, $"重ね塗り後のアルファ値 ({alphaDouble}) は単一描画時 ({alphaSingle}) より大きくなるべきです。");
    }

    [Fact]
    public void PenThenHighlighter_HighlighterRenderedOnTopOfPen()
    {
        // Arrange: ペン（黒・不透明）を先に引き、その上に蛍光ペン（黄色・半透明）を引く
        var service = new StrokeCacheService();
        var penAttr = new DrawingAttributes
        {
            Color = Colors.Black,
            Width = 20,
            Height = 20,
            IsHighlighter = false
        };
        var highlighterAttr = new DrawingAttributes
        {
            Color = Color.FromArgb(120, 255, 255, 0),
            Width = 20,
            Height = 20,
            IsHighlighter = false
        };

        var points = new StylusPointCollection { new StylusPoint(50, 50), new StylusPoint(50, 60) };
        var strokes = new StrokeCollection
        {
            new Stroke(points, penAttr),
            new Stroke(points, highlighterAttr)
        };

        // Act: レンダリング
        var bmp = service.RenderStrokeCache(strokes, 100, 100, 100, 100);
        Assert.NotNull(bmp);

        byte[] pixel = new byte[4]; // Bgra32: [0]=B, [1]=G, [2]=R, [3]=A
        bmp.CopyPixels(new Int32Rect(50, 55, 1, 1), pixel, 4, 0);

        // Assert: 黒の上に黄色が重なっているため、RedおよびGreen成分が0より大きくなっていること（黒に埋もれていない）
        Assert.True(pixel[1] > 0, "ペンの上に蛍光ペンが重なっているため、Green成分が反映されていること");
        Assert.True(pixel[2] > 0, "ペンの上に蛍光ペンが重なっているため、Red成分が反映されていること");
    }

    [Fact]
    public void LegacyHighlighterStroke_BackwardCompatibility_RendersSuccessfully()
    {
        // Arrange: 過去バージョンで保存された IsHighlighter = true のストロークと新方式ストロークを混在
        var service = new StrokeCacheService();
        var legacyAttr = new DrawingAttributes
        {
            Color = Colors.Yellow,
            Width = 12,
            Height = 12,
            IsHighlighter = true
        };
        var newAttr = new DrawingAttributes
        {
            Color = Color.FromArgb(120, 0, 255, 0),
            Width = 12,
            Height = 12,
            IsHighlighter = false
        };

        var points1 = new StylusPointCollection { new StylusPoint(20, 20), new StylusPoint(40, 40) };
        var points2 = new StylusPointCollection { new StylusPoint(30, 30), new StylusPoint(50, 50) };
        var strokes = new StrokeCollection
        {
            new Stroke(points1, legacyAttr),
            new Stroke(points2, newAttr)
        };

        // Act
        var bmp = service.RenderStrokeCache(strokes, 100, 100, 100, 100);

        // Assert: 例外なく正常にレンダリングされること
        Assert.NotNull(bmp);
        Assert.True(bmp.PixelWidth == 100);
        Assert.True(bmp.PixelHeight == 100);
    }
}
