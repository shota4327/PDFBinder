using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="MainViewModel"/> および <see cref="DetailEditorViewModel"/> の単体テスト
/// </summary>
public class ViewModelsTests
{
    [Fact]
    public void MainViewModel_AddBlankPage_InsertsPageAndEnablesUndo()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act
        vm.AddBlankPageCommand.Execute(null);

        // Assert
        Assert.Equal(1, vm.Document.PageCount);
        Assert.True(vm.Document.Pages[0].IsBlankPage);
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public void MainViewModel_RotateClockwise_RotatesPage()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        vm.Document.Pages[0].IsSelected = true;

        // Act
        vm.RotateClockwiseCommand.Execute(null);

        // Assert
        Assert.Equal(PageRotation.Rotate90, vm.Document.Pages[0].Rotation);

        // Undo
        vm.UndoCommand.Execute(null);
        Assert.Equal(PageRotation.Rotate0, vm.Document.Pages[0].Rotation);
    }

    [Fact]
    public void MainViewModel_DeleteSelectedPages_RemovesPages()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        vm.AddBlankPage();
        Assert.Equal(2, vm.Document.PageCount);

        vm.Document.Pages[0].IsSelected = true;

        // Act
        vm.DeleteSelectedPagesCommand.Execute(null);

        // Assert
        Assert.Equal(1, vm.Document.PageCount);

        // Undo
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Document.PageCount);
    }

    [Fact]
    public void MainViewModel_OpenAndClosePageDetail_TogglesActiveState()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        var page = vm.Document.Pages[0];

        // Act
        vm.OpenPageDetailCommand.Execute(page);

        // Assert
        Assert.True(vm.IsDetailViewActive);
        Assert.NotNull(vm.DetailEditor);
        Assert.Equal(page, vm.DetailEditor.CurrentPage);

        // Close
        vm.ClosePageDetailCommand.Execute(null);
        Assert.False(vm.IsDetailViewActive);
        Assert.Null(vm.DetailEditor);
    }

    [Fact]
    public void DetailEditorViewModel_SelectTool_ConfiguresToolDefaults()
    {
        // Arrange
        var page = new PdfPageModel();
        var renderer = new PdfiumRenderer();
        var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Act: Highlighter
        vm.SelectToolCommand.Execute(EditorToolMode.Highlighter);
        Assert.Equal(EditorToolMode.Highlighter, vm.SelectedTool);
        Assert.Equal(12.0, vm.StrokeThickness);
        Assert.Equal(Colors.Yellow, vm.SelectedColor);

        // Act: Pen
        vm.SelectToolCommand.Execute(EditorToolMode.Pen);
        Assert.Equal(EditorToolMode.Pen, vm.SelectedTool);
        Assert.Equal(2.0, vm.StrokeThickness);
        Assert.Equal(Colors.Black, vm.SelectedColor);
    }

    [Fact]
    public void DetailEditorViewModel_ZoomControls_WorkCorrectly()
    {
        // Arrange
        var page = new PdfPageModel();
        var renderer = new PdfiumRenderer();
        var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Zoom in
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(1.25, vm.Zoom);

        // Zoom out
        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(1.0, vm.Zoom);

        // Reset
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        vm.ZoomResetCommand.Execute(null);
        Assert.Equal(1.0, vm.Zoom);
    }
}
