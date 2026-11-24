using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 直線トグル機能およびペン・蛍光ペン連動の単体テスト（Issue #25）
/// </summary>
public class DetailEditorStraightLineTests
{
    private class FakePdfRenderer : IPdfRenderer
    {
        public Task<System.Windows.Media.Imaging.BitmapSource?> RenderPageAsync(
            string? filePath,
            int pageIndex,
            int targetWidth,
            int targetHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);
        }

        public System.Windows.Media.Imaging.BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
        {
            throw new NotImplementedException();
        }

        public System.Windows.Media.Imaging.BitmapSource CompositeStrokes(
            System.Windows.Media.Imaging.BitmapSource baseImage,
            System.Windows.Ink.StrokeCollection strokes,
            double originalPageWidth,
            double originalPageHeight)
        {
            return baseImage;
        }

        public Task<PageInteractiveData> ExtractInteractiveDataAsync(
            string? filePath,
            int pageIndex,
            double displayWidth,
            double displayHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PageInteractiveData.Empty);
        }
    }

    private static DetailEditorViewModel CreateViewModel(EditorToolMode initialTool = EditorToolMode.Pen)
    {
        var page = new PdfPageModel { Width = 595, Height = 842, PageNumber = 1 };
        var vm = new DetailEditorViewModel(page, new FakePdfRenderer(), () => { }, _ => null)
        {
            SelectedTool = initialTool
        };
        return vm;
    }

    [Fact]
    public void IsStraightLine_DefaultIsFalse()
    {
        // Arrange & Act
        using var vm = CreateViewModel();

        // Assert
        Assert.False(vm.IsStraightLine);
    }

    [Theory]
    [InlineData(EditorToolMode.Pen, true)]
    [InlineData(EditorToolMode.Highlighter, true)]
    [InlineData(EditorToolMode.Select, false)]
    [InlineData(EditorToolMode.EraserStroke, false)]
    [InlineData(EditorToolMode.EraserPoint, false)]
    [InlineData(EditorToolMode.Hand, false)]
    public void CanToggleStraightLine_OnlyTrueForPenAndHighlighter(EditorToolMode tool, bool expectedCanToggle)
    {
        // Arrange
        using var vm = CreateViewModel();

        // Act
        vm.SelectedTool = tool;

        // Assert
        Assert.Equal(expectedCanToggle, vm.CanToggleStraightLine);
    }

    [Fact]
    public void SelectedToolChanged_ResetsIsStraightLineToFalse()
    {
        // Arrange: ペンで直線トグルをONにする
        using var vm = CreateViewModel(EditorToolMode.Pen);
        vm.IsStraightLine = true;
        Assert.True(vm.IsStraightLine);

        // Act: 蛍光ペンに切り替える
        vm.SelectedTool = EditorToolMode.Highlighter;

        // Assert: 直線トグルが自動的にオフにリセットされること
        Assert.False(vm.IsStraightLine);

        // Arrange: 再び直線トグルをONにする
        vm.IsStraightLine = true;
        Assert.True(vm.IsStraightLine);

        // Act: 消しゴムに切り替える
        vm.SelectedTool = EditorToolMode.EraserStroke;

        // Assert: 直線トグルがオフになり、かつ有効化不可となること
        Assert.False(vm.IsStraightLine);
        Assert.False(vm.CanToggleStraightLine);
    }

    [Fact]
    public void SelectedToolChanged_RaisesPropertyChangedForCanToggleStraightLine()
    {
        // Arrange
        using var vm = CreateViewModel(EditorToolMode.Pen);
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        // Act
        vm.SelectedTool = EditorToolMode.EraserStroke;

        // Assert
        Assert.Contains(nameof(DetailEditorViewModel.CanToggleStraightLine), changedProperties);
    }

    [Fact]
    public void EditorInkCanvas_IsStraightLine_TogglesEditingModeAndCursor()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var canvas = new EditorInkCanvas();

                // 通常ペン（直線オフ）: インクモード & ペンプレビューカーソル（十字以外）
                canvas.ToolMode = EditorToolMode.Pen;
                canvas.IsStraightLine = false;
                Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);
                Assert.NotNull(canvas.Cursor);
                Assert.NotEqual(Cursors.Cross, canvas.Cursor);
                Assert.False(canvas.IsStraightLineActive);

                // 通常ペン（直線オン）: Noneモード & 定規付きプレビューカーソル（十字以外）
                canvas.IsStraightLine = true;
                Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);
                Assert.NotNull(canvas.Cursor);
                Assert.NotEqual(Cursors.Cross, canvas.Cursor);
                Assert.True(canvas.IsStraightLineActive);

                // 蛍光ペン（直線オン）: Noneモード & 定規付きプレビューカーソル（十字以外） & IsHighlighter=false（Issue #108: 半透明Alpha=120で重なり順を保持）
                canvas.ToolMode = EditorToolMode.Highlighter;
                Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);
                Assert.NotNull(canvas.Cursor);
                Assert.NotEqual(Cursors.Cross, canvas.Cursor);
                Assert.False(canvas.DefaultDrawingAttributes.IsHighlighter);
                Assert.Equal(120, canvas.DefaultDrawingAttributes.Color.A);
                Assert.True(canvas.IsStraightLineActive);

                // 蛍光ペン（直線オフ）: インクモード & ペンプレビューカーソル（十字以外）
                canvas.IsStraightLine = false;
                Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);
                Assert.NotNull(canvas.Cursor);
                Assert.NotEqual(Cursors.Cross, canvas.Cursor);
                Assert.False(canvas.IsStraightLineActive);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
        if (exception != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    [Fact]
    public void EditorInkCanvas_IsStraightLine_InactiveForEraserAndOtherTools()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();

            // 消しゴム選択時に仮にIsStraightLineがtrueになっても消しゴムが優先される
            canvas.ToolMode = EditorToolMode.EraserStroke;
            canvas.IsStraightLine = true;

            Assert.Equal(InkCanvasEditingMode.EraseByStroke, canvas.EditingMode);
            Assert.False(canvas.IsStraightLineActive);

            // 選択ツール
            canvas.ToolMode = EditorToolMode.Select;
            canvas.IsStraightLine = true;

            Assert.Equal(InkCanvasEditingMode.Select, canvas.EditingMode);
            Assert.False(canvas.IsStraightLineActive);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Fact]
    public void EditorInkCanvas_OnPreviewMouseUp_DoesNotThrow_AndCommitsStraightLine()
    {
        RunInSta(() =>
        {
            // Arrange
            var page = new PdfPageModel { Width = 500, Height = 800 };
            var pageItem = new DetailPageItemViewModel(page);
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = true,
                PageItem = pageItem
            };

            // 描画中の状態を設定
            var startPt = new Point(10.0, 20.0);
            var endPt = new Point(100.0, 200.0);
            SetPrivateField(canvas, "_lineStartPoint", (Point?)startPt);
            SetPrivateField(canvas, "_currentLinePoint", (Point?)endPt);
            SetPrivateField(canvas, "_isDrawingLine", true);

            // Act: PreviewMouseUpをシミュレート
            var onMouseUp = typeof(EditorInkCanvas).GetMethod("OnPreviewMouseUp",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(onMouseUp);

            var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseUpEvent
            };

            // 例外が発生せずに正常終了すること（Issue #156: Nullable object must have a value 例外の防止）
            onMouseUp.Invoke(canvas, new object[] { mouseArgs });

            // Assert: マスターコレクションに直線がコミットされていること
            Assert.False(canvas.IsDrawingLineForTesting);
            Assert.Null(canvas.LineStartPointForTesting);
            Assert.Null(canvas.CurrentLinePointForTesting);
            Assert.Single(page.InkStrokes);

            var committedStroke = page.InkStrokes[0];
            Assert.Equal(2, committedStroke.StylusPoints.Count);
            Assert.Equal(10.0, committedStroke.StylusPoints[0].X);
            Assert.Equal(20.0, committedStroke.StylusPoints[0].Y);
            Assert.Equal(100.0, committedStroke.StylusPoints[1].X);
            Assert.Equal(200.0, committedStroke.StylusPoints[1].Y);
        });
    }

    [Fact]
    public void EditorInkCanvas_OnPreviewMouseUp_SingleClick_CommitsDotStroke()
    {
        RunInSta(() =>
        {
            // Arrange: 始点と終点が同一座標（クリックのみ）
            var page = new PdfPageModel { Width = 500, Height = 800 };
            var pageItem = new DetailPageItemViewModel(page);
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = true,
                PageItem = pageItem
            };

            var clickPt = new Point(50.0, 50.0);
            SetPrivateField(canvas, "_lineStartPoint", (Point?)clickPt);
            SetPrivateField(canvas, "_currentLinePoint", (Point?)clickPt);
            SetPrivateField(canvas, "_isDrawingLine", true);

            var onMouseUp = typeof(EditorInkCanvas).GetMethod("OnPreviewMouseUp",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(onMouseUp);

            var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseUpEvent
            };

            // Act
            onMouseUp.Invoke(canvas, new object[] { mouseArgs });

            // Assert: ドットとしてコミットされること
            Assert.Single(page.InkStrokes);
            var stroke = page.InkStrokes[0];
            Assert.Equal(2, stroke.StylusPoints.Count);
            Assert.Equal(50.0, stroke.StylusPoints[0].X);
            Assert.Equal(50.0, stroke.StylusPoints[1].X);
        });
    }

    [Fact]
    public void EditorInkCanvas_OnLostMouseCapture_CancelsDrawingWithoutCommitting()
    {
        RunInSta(() =>
        {
            // Arrange
            var page = new PdfPageModel { Width = 500, Height = 800 };
            var pageItem = new DetailPageItemViewModel(page);
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = true,
                PageItem = pageItem
            };

            SetPrivateField(canvas, "_lineStartPoint", (Point?)new Point(10.0, 20.0));
            SetPrivateField(canvas, "_currentLinePoint", (Point?)new Point(50.0, 60.0));
            SetPrivateField(canvas, "_isDrawingLine", true);

            // Act: キャプチャ喪失をシミュレート
            canvas.ProcessLostMouseCaptureForTesting();

            // Assert: 描画状態が安全にクリアされ、ストロークはコミットされないこと
            Assert.False(canvas.IsDrawingLineForTesting);
            Assert.Null(canvas.LineStartPointForTesting);
            Assert.Null(canvas.CurrentLinePointForTesting);
            Assert.Empty(page.InkStrokes);
        });
    }

    [Fact]
    public void EditorInkCanvas_LinePreviewPen_IsFrozenAndReusedAcrossFrames()
    {
        RunInSta(() =>
        {
            // Arrange
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = true,
                DrawingColor = Colors.Red,
                StrokeThickness = 2.0
            };

            // Act: 同一属性で複数回プレビュー用Penを取得（毎フレームのOnRender呼び出しを模倣）
            var firstPen = canvas.GetLinePreviewPenForTesting();
            var secondPen = canvas.GetLinePreviewPenForTesting();

            // Assert: PenとBrushがともにFreeze済みで、同一インスタンスが再利用されること
            Assert.Same(firstPen, secondPen);
            Assert.Same(firstPen, canvas.CachedLinePreviewPenForTesting);
            Assert.True(firstPen.IsFrozen);
            Assert.IsType<SolidColorBrush>(firstPen.Brush);
            var brush = (SolidColorBrush)firstPen.Brush;
            Assert.True(brush.IsFrozen);
            Assert.Equal(Colors.Red, brush.Color);
            Assert.Equal(2.0, firstPen.Thickness);
            Assert.Equal(PenLineCap.Round, firstPen.StartLineCap);
            Assert.Equal(PenLineCap.Round, firstPen.EndLineCap);
        });
    }

    [Fact]
    public void EditorInkCanvas_LinePreviewPen_UpdatesWhenDrawingAttributesOrToolChange()
    {
        RunInSta(() =>
        {
            // Arrange
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = true,
                DrawingColor = Colors.Black,
                StrokeThickness = 1.0
            };

            var initialPen = canvas.GetLinePreviewPenForTesting();

            // Act 1: 色変更 -> キャッシュが無効化され、新しい色のフリーズ済みPenが生成されること
            canvas.DrawingColor = Colors.Blue;
            Assert.Null(canvas.CachedLinePreviewPenForTesting);
            var bluePen = canvas.GetLinePreviewPenForTesting();
            Assert.NotSame(initialPen, bluePen);
            Assert.True(bluePen.IsFrozen);
            Assert.Equal(Colors.Blue, ((SolidColorBrush)bluePen.Brush).Color);

            // Act 2: 太さ変更 -> 新しい太さのフリーズ済みPenが生成されること
            canvas.StrokeThickness = 4.0;
            Assert.Null(canvas.CachedLinePreviewPenForTesting);
            var thickPen = canvas.GetLinePreviewPenForTesting();
            Assert.NotSame(bluePen, thickPen);
            Assert.True(thickPen.IsFrozen);
            Assert.Equal(4.0, thickPen.Thickness);

            // Act 3: 蛍光ペンへ切り替え -> Alpha=120の半透明フリーズ済みPenが生成されること
            canvas.ToolMode = EditorToolMode.Highlighter;
            var highlighterPen = canvas.GetLinePreviewPenForTesting();
            Assert.NotSame(thickPen, highlighterPen);
            Assert.True(highlighterPen.IsFrozen);
            var highlighterColor = ((SolidColorBrush)highlighterPen.Brush).Color;
            Assert.Equal(120, highlighterColor.A);
            Assert.Equal(Colors.Blue.R, highlighterColor.R);
            Assert.Equal(Colors.Blue.G, highlighterColor.G);
            Assert.Equal(Colors.Blue.B, highlighterColor.B);
        });
    }

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
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private static void SetPrivateField(object obj, string fieldName, object? value)
    {
        var field = obj.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(obj, value);
    }
}
