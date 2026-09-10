using System.Threading;
using System.Windows.Controls;
using PDFBinder.App.Controls;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="EditorInkCanvas"/> および <see cref="PenOnlyDynamicRenderer"/> のタッチ・スタイラス制御テスト
/// </summary>
public class EditorInkCanvasTouchTests
{
    [Fact]
    public void EditorInkCanvas_UsesPenOnlyDynamicRenderer()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();
            Assert.NotNull(canvas.CurrentDynamicRenderer);
            Assert.IsType<PenOnlyDynamicRenderer>(canvas.CurrentDynamicRenderer);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Fact]
    public void EditorInkCanvas_EditingMode_MatchesToolMode()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();

            // 初期ツールは通常ペン
            Assert.Equal(EditorToolMode.Pen, canvas.ToolMode);
            Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);

            // 蛍光ペン
            canvas.ToolMode = EditorToolMode.Highlighter;
            Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);

            // 全消しゴム
            canvas.ToolMode = EditorToolMode.EraserStroke;
            Assert.Equal(InkCanvasEditingMode.EraseByStroke, canvas.EditingMode);

            // 部分消しゴム
            canvas.ToolMode = EditorToolMode.EraserPoint;
            Assert.Equal(InkCanvasEditingMode.EraseByPoint, canvas.EditingMode);

            // 選択ツール
            canvas.ToolMode = EditorToolMode.Select;
            Assert.Equal(InkCanvasEditingMode.Select, canvas.EditingMode);

            // 手のひら・直線ツール
            canvas.ToolMode = EditorToolMode.Hand;
            Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);

            canvas.ToolMode = EditorToolMode.StraightLine;
            Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }
}
