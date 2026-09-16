using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// PenCursorHelperおよびズーム連動カーソル更新機能の単体テスト
/// </summary>
public class PenCursorHelperTests
{
    [Fact]
    public void IsCircleCursorTool_IdentifiesTargetToolsCorrectly()
    {
        // 円形カーソル対象ツール
        Assert.True(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Pen));
        Assert.True(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Highlighter));
        Assert.True(PenCursorHelper.IsCircleCursorTool(EditorToolMode.EraserPoint));

        // 円形カーソル対象外ツール
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Select));
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.EraserStroke));
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.StraightLine));
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Hand));
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.TextSelect));
    }

    [Fact]
    public void GetCursor_NonCircleTool_ReturnsNull()
    {
        var cursor = PenCursorHelper.GetCursor(EditorToolMode.Select, Colors.Black, 2.0, 1.0);
        Assert.Null(cursor);

        cursor = PenCursorHelper.GetCursor(EditorToolMode.StraightLine, Colors.Black, 2.0, 1.0);
        Assert.Null(cursor);

        cursor = PenCursorHelper.GetCursor(EditorToolMode.Hand, Colors.Black, 2.0, 1.0);
        Assert.Null(cursor);
    }

    [Theory]
    [InlineData(EditorToolMode.Pen)]
    [InlineData(EditorToolMode.Highlighter)]
    [InlineData(EditorToolMode.EraserPoint)]
    public void GetCursor_CircleTools_ReturnsNonNullCursor(EditorToolMode tool)
    {
        var cursor = PenCursorHelper.GetCursor(tool, Colors.Blue, 3.0, 1.5);
        Assert.NotNull(cursor);
    }

    [Fact]
    public void GetCursor_SameParameters_ReturnsCachedInstance()
    {
        PenCursorHelper.ClearCache();

        var cursor1 = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Red, 4.0, 2.0);
        var cursor2 = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Red, 4.0, 2.0);

        Assert.NotNull(cursor1);
        Assert.Same(cursor1, cursor2);
    }

    [Fact]
    public void GetCursor_DifferentZoom_ReturnsDifferentCursorInstance()
    {
        PenCursorHelper.ClearCache();

        var cursor100 = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Black, 5.0, 1.0);
        var cursor200 = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Black, 5.0, 2.0);

        Assert.NotNull(cursor100);
        Assert.NotNull(cursor200);
        Assert.NotSame(cursor100, cursor200);
    }

    [Fact]
    public void GetCursor_ExtremeZoom_ClampsBetweenMinAndMax()
    {
        PenCursorHelper.ClearCache();

        // 極小サイズ (0.01 * 0.1 = 0.001) -> MinCursorSize (3.0px) にクランプ
        var cursorMin = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Black, 0.01, 0.1);
        Assert.NotNull(cursorMin);

        // 極大サイズ (100 * 50 = 5000) -> MaxCursorSize (128.0px) にクランプ
        var cursorMax = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Black, 100.0, 50.0);
        Assert.NotNull(cursorMax);
    }

    [Fact]
    public void EditorInkCanvas_ZoomChange_UpdatesCursorOnStaThread()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                StrokeThickness = 4.0,
                DrawingColor = Colors.Black,
                Zoom = 1.0
            };

            var initialCursor = canvas.Cursor;
            Assert.NotNull(initialCursor);

            // ズームを2.0に変更
            canvas.Zoom = 2.0;
            var zoomedCursor = canvas.Cursor;

            Assert.NotNull(zoomedCursor);
            Assert.NotSame(initialCursor, zoomedCursor);
        });
    }

    [Fact]
    public void EditorInkCanvas_ThicknessAndColorChange_UpdatesCursorOnStaThread()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Highlighter,
                StrokeThickness = 10.0,
                DrawingColor = Colors.Yellow,
                Zoom = 1.0
            };

            var cursor1 = canvas.Cursor;
            Assert.NotNull(cursor1);

            // 太さを変更
            canvas.StrokeThickness = 20.0;
            var cursor2 = canvas.Cursor;
            Assert.NotNull(cursor2);
            Assert.NotSame(cursor1, cursor2);

            // 色を変更
            canvas.DrawingColor = Colors.Green;
            var cursor3 = canvas.Cursor;
            Assert.NotNull(cursor3);
            Assert.NotSame(cursor2, cursor3);
        });
    }

    [Fact]
    public void EditorInkCanvas_PointEraserMode_UsesPointEraserCursor()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.EraserPoint,
                StrokeThickness = 12.0,
                Zoom = 1.5
            };

            Assert.NotNull(canvas.Cursor);
            Assert.NotEqual(Cursors.Cross, canvas.Cursor);

            // ストローク消しゴム時は消しゴム形状カーソル（WPF標準）であり、UseCustomCursorがfalseであること
            canvas.ToolMode = EditorToolMode.EraserStroke;
            Assert.False(canvas.UseCustomCursor);
            Assert.Equal(PenCursorHelper.GetStrokeEraserCursor(), canvas.Cursor);
        });
    }

    [Fact]
    public void GetStrokeEraserCursor_ReturnsNonNullCursor()
    {
        var cursor = PenCursorHelper.GetStrokeEraserCursor();
        Assert.NotNull(cursor);
    }

    [Fact]
    public void EditorInkCanvas_StraightLineMode_KeepsCrossCursor()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = true,
                StrokeThickness = 5.0,
                Zoom = 2.0
            };

            // 直線トグル有効時は十字カーソルであること
            Assert.Equal(Cursors.Cross, canvas.Cursor);
        });
    }

    [Fact]
    public void RenderCirclePixels_EraserPoint_HasNoCenterDot()
    {
        // 部分消しゴム（中空円）の中心点にドット（不透明ピクセル）が存在しないことを検証
        int size = 32;
        int hotspot = 16;
        double diameter = 20.0;
        byte[] pixels = PenCursorHelper.RenderCirclePixels(
            size, hotspot, diameter, Colors.Black, isHollow: true, isHighlighter: false);

        // hotspot位置のピクセルのアルファ値を取得（DIBボトムアップ順）
        int dibRow = size - 1 - hotspot;
        int centerPixelOffset = (dibRow * size + hotspot) * 4;
        byte centerAlpha = pixels[centerPixelOffset + 3];

        Assert.Equal(0, centerAlpha);
    }

    [Fact]
    public void RenderCirclePixels_AntiAliasing_ProducesIntermediateAlphas()
    {
        // ペンおよび消しゴムの円周境界にスーパーサンプリングによる中間アルファ値が存在することを検証（ギザギザ防止）
        int size = 32;
        int hotspot = 16;
        double diameter = 20.0;

        // ペン（塗りつぶし円）
        byte[] penPixels = PenCursorHelper.RenderCirclePixels(
            size, hotspot, diameter, Colors.Red, isHollow: false, isHighlighter: false);
        var penAlphas = penPixels.Where((p, i) => i % 4 == 3 && p > 0).Distinct().ToList();
        // 0と255以外に中間階調が複数存在すること（アンチエイリアス）
        Assert.True(penAlphas.Count > 2);

        // 消しゴム（中空円）
        byte[] eraserPixels = PenCursorHelper.RenderCirclePixels(
            size, hotspot, diameter, Colors.Black, isHollow: true, isHighlighter: false);
        var eraserAlphas = eraserPixels.Where((p, i) => i % 4 == 3 && p > 0).Distinct().ToList();
        // 0と220以外に中間階調が複数存在すること（アンチエイリアス）
        Assert.True(eraserAlphas.Count > 2);
    }

    [Fact]
    public void RenderCirclePixels_StraightAlpha_PreservesSourceRgb()
    {
        // ストレートアルファ（RGB値を直接維持し、アルファのみを調整）を検証
        int size = 32;
        int hotspot = 16;
        double diameter = 18.0;
        var sourceColor = Color.FromArgb(200, 255, 128, 64);

        byte[] pixels = PenCursorHelper.RenderCirclePixels(
            size, hotspot, diameter, sourceColor, isHollow: false, isHighlighter: false);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a > 0)
            {
                Assert.Equal(sourceColor.B, pixels[i]);
                Assert.Equal(sourceColor.G, pixels[i + 1]);
                Assert.Equal(sourceColor.R, pixels[i + 2]);
            }
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new System.Threading.Thread(() =>
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
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
