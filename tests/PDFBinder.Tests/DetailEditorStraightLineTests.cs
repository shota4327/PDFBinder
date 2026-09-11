using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
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
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();

            // 通常ペン（直線オフ）: インクモード & ペンカーソル
            canvas.ToolMode = EditorToolMode.Pen;
            canvas.IsStraightLine = false;
            Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);
            Assert.Equal(Cursors.Pen, canvas.Cursor);
            Assert.False(canvas.IsStraightLineActive);

            // 通常ペン（直線オン）: Noneモード & 十字カーソル
            canvas.IsStraightLine = true;
            Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);
            Assert.Equal(Cursors.Cross, canvas.Cursor);
            Assert.True(canvas.IsStraightLineActive);

            // 蛍光ペン（直線オン）: Noneモード & 十字カーソル & IsHighlighter=true
            canvas.ToolMode = EditorToolMode.Highlighter;
            Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);
            Assert.Equal(Cursors.Cross, canvas.Cursor);
            Assert.True(canvas.DefaultDrawingAttributes.IsHighlighter);
            Assert.True(canvas.IsStraightLineActive);

            // 蛍光ペン（直線オフ）: インクモード & ペンカーソル
            canvas.IsStraightLine = false;
            Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);
            Assert.Equal(Cursors.Pen, canvas.Cursor);
            Assert.False(canvas.IsStraightLineActive);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
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
}
