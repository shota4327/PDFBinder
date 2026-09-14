using System.Threading;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="EditorInkCanvas"/> とストロークキャッシュ連携機能の単体テスト（Issue #43）
/// </summary>
public class EditorInkCanvasCacheTests
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
    public void PenMode_KeepsCanvasStrokesEmpty_WhenPageItemHasStrokes()
    {
        RunInSta(() =>
        {
            // Arrange
            var page = new PdfPageModel { Width = 500, Height = 800 };
            var stroke = new Stroke(new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(20.0, 20.0) });
            page.InkStrokes.Add(stroke);

            var pageItem = new DetailPageItemViewModel(page);
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                PageItem = pageItem
            };

            // Assert: ペンモード時はInkCanvas内は空になり、ベクター描画負荷がゼロに抑えられる
            Assert.Empty(canvas.Strokes);
        });
    }

    [Fact]
    public void SwitchingToEraser_LoadsStrokesIntoCanvas_AndClearsStrokeCache()
    {
        RunInSta(() =>
        {
            // Arrange
            var page = new PdfPageModel { Width = 500, Height = 800 };
            var stroke = new Stroke(new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(20.0, 20.0) });
            page.InkStrokes.Add(stroke);

            var pageItem = new DetailPageItemViewModel(page);
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                PageItem = pageItem
            };

            Assert.Empty(canvas.Strokes);

            // Act: 消しゴムモードに切り替え
            canvas.ToolMode = EditorToolMode.EraserStroke;

            // Assert: 消しゴム操作用にInkCanvas内に全ストロークが展開され、キャッシュ画像は非表示になる
            Assert.Single(canvas.Strokes);
            Assert.Equal(stroke, canvas.Strokes[0]);
            Assert.Null(pageItem.StrokeCache);
        });
    }

    [Fact]
    public void EraserMode_RemovingStrokeFromCanvas_UpdatesMasterPageStrokes()
    {
        RunInSta(() =>
        {
            // Arrange
            var page = new PdfPageModel { Width = 500, Height = 800 };
            var stroke1 = new Stroke(new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(20.0, 20.0) });
            var stroke2 = new Stroke(new StylusPointCollection { new StylusPoint(30.0, 30.0), new StylusPoint(40.0, 40.0) });
            page.InkStrokes.Add(stroke1);
            page.InkStrokes.Add(stroke2);

            var pageItem = new DetailPageItemViewModel(page);
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.EraserStroke,
                PageItem = pageItem
            };

            Assert.Equal(2, canvas.Strokes.Count);
            Assert.Equal(2, page.InkStrokes.Count);

            // Act: Canvasからstroke1を消去
            canvas.Strokes.Remove(stroke1);

            // Assert: マスターのPage.InkStrokesからもstroke1が同期して消去される
            Assert.Single(page.InkStrokes);
            Assert.Equal(stroke2, page.InkStrokes[0]);
        });
    }

    [Fact]
    public void SwitchingFromEraserBackToPen_ClearsCanvasStrokes()
    {
        RunInSta(() =>
        {
            // Arrange
            var page = new PdfPageModel { Width = 500, Height = 800 };
            var stroke = new Stroke(new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(20.0, 20.0) });
            page.InkStrokes.Add(stroke);

            var pageItem = new DetailPageItemViewModel(page);
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.EraserStroke,
                PageItem = pageItem
            };
            Assert.Single(canvas.Strokes);

            // Act: ペンモードに戻す
            canvas.ToolMode = EditorToolMode.Pen;

            // Assert: Canvasは空になり軽量状態へ戻る
            Assert.Empty(canvas.Strokes);
            Assert.Single(page.InkStrokes);
        });
    }

    [Fact]
    public void StraightLineInPenMode_AddsToMasterStrokes_AndKeepsCanvasEmpty()
    {
        RunInSta(() =>
        {
            // Arrange
            var page = new PdfPageModel { Width = 500, Height = 800 };
            var pageItem = new DetailPageItemViewModel(page);
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.StraightLine,
                PageItem = pageItem
            };

            Assert.Empty(canvas.Strokes);
            Assert.Empty(page.InkStrokes);

            // Act: 直線をコミット（リフレクションまたは内部メソッド呼び出し検証）
            var method = typeof(EditorInkCanvas).GetMethod("CommitStraightLine",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);
            method.Invoke(canvas, new object[] { new Point(0, 0), new Point(100, 100) });

            // Assert: マスターコレクションに直線が追加され、Canvasは空のまま維持される
            Assert.Single(page.InkStrokes);
            Assert.Empty(canvas.Strokes);
        });
    }
}
