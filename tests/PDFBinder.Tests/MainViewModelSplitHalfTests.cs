using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="MainViewModel.SplitPagesHalfCommand"/> の単体テストクラス（Issue #137）
/// </summary>
public class MainViewModelSplitHalfTests
{
    [Fact]
    public async Task SplitPagesHalfCommand_SplitsPagesAndUpdatesDocument()
    {
        // Arrange
        var vm = new MainViewModel();
        var page = new PdfPageModel
        {
            Width = 842,
            Height = 595,
            Rotation = PageRotation.Rotate0
        };
        vm.Document.AddPage(page);
        Assert.Equal(1, vm.Document.PageCount);

        // Act
        await vm.SplitPagesHalfCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, vm.Document.PageCount);
        Assert.Equal(421, vm.Document.Pages[0].Width);
        Assert.Equal(595, vm.Document.Pages[0].Height);
        Assert.Equal(1, vm.Document.Pages[0].PageNumber);

        Assert.Equal(421, vm.Document.Pages[1].Width);
        Assert.Equal(595, vm.Document.Pages[1].Height);
        Assert.Equal(2, vm.Document.Pages[1].PageNumber);
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public async Task SplitPagesHalfCommand_UndoAndRedo_WorksCorrectly()
    {
        // Arrange
        var vm = new MainViewModel();
        var page = new PdfPageModel
        {
            Width = 842,
            Height = 595,
            Rotation = PageRotation.Rotate0
        };
        vm.Document.AddPage(page);

        // Act - Split
        await vm.SplitPagesHalfCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Document.PageCount);
        Assert.True(vm.CanUndo);

        // Act - Undo
        vm.UndoCommand.Execute(null);
        Assert.Equal(1, vm.Document.PageCount);
        Assert.Equal(842, vm.Document.Pages[0].Width);
        Assert.Equal(595, vm.Document.Pages[0].Height);
        Assert.True(vm.CanRedo);

        // Act - Redo
        vm.RedoCommand.Execute(null);
        Assert.Equal(2, vm.Document.PageCount);
        Assert.Equal(421, vm.Document.Pages[0].Width);
        Assert.Equal(421, vm.Document.Pages[1].Width);
    }

    [Fact]
    public async Task SplitPagesHalfCommand_EmptyDocument_DoesNothing()
    {
        // Arrange
        var vm = new MainViewModel();
        Assert.Equal(0, vm.Document.PageCount);

        // Act
        await vm.SplitPagesHalfCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(0, vm.Document.PageCount);
        Assert.False(vm.CanUndo);
    }
}
