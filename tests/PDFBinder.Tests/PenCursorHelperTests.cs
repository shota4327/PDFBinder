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

            // ストローク消しゴム時は従来のCrossカーソルであること
            canvas.ToolMode = EditorToolMode.EraserStroke;
            Assert.Equal(Cursors.Cross, canvas.Cursor);
        });
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
