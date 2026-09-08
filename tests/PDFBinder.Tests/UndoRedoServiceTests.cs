using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="UndoRedoService"/> の単体テストクラス
/// </summary>
public class UndoRedoServiceTests
{
    [Fact]
    public void ExecuteAndUndo_RotateCommand_RestoresOriginalRotation()
    {
        // Arrange
        var service = new UndoRedoService();
        var page = new PdfPageModel { Rotation = PageRotation.Rotate0 };
        var cmd = new RotatePageCommand(page, PageRotation.Rotate0, PageRotation.Rotate90);

        // Act
        service.Execute(cmd);
        Assert.Equal(PageRotation.Rotate90, page.Rotation);
        Assert.True(service.CanUndo);
        Assert.False(service.CanRedo);

        service.Undo();

        // Assert
        Assert.Equal(PageRotation.Rotate0, page.Rotation);
        Assert.False(service.CanUndo);
        Assert.True(service.CanRedo);

        service.Redo();
        Assert.Equal(PageRotation.Rotate90, page.Rotation);
    }

    [Fact]
    public void ExecuteAndUndo_MovePageCommand_RestoresOriginalOrder()
    {
        // Arrange
        var service = new UndoRedoService();
        var doc = new PdfDocumentModel();
        var p1 = new PdfPageModel { OriginalPageIndex = 0 };
        var p2 = new PdfPageModel { OriginalPageIndex = 1 };
        doc.AddPage(p1);
        doc.AddPage(p2);

        var cmd = new MovePageCommand(doc, 0, 1);

        // Act
        service.Execute(cmd);
        Assert.Equal(p2, doc.Pages[0]);
        Assert.Equal(p1, doc.Pages[1]);

        service.Undo();

        // Assert
        Assert.Equal(p1, doc.Pages[0]);
        Assert.Equal(p2, doc.Pages[1]);
    }

    [Fact]
    public void ExecuteAndUndo_RemovePageCommand_RestoresRemovedPage()
    {
        // Arrange
        var service = new UndoRedoService();
        var doc = new PdfDocumentModel();
        var p1 = new PdfPageModel();
        var p2 = new PdfPageModel();
        doc.AddPage(p1);
        doc.AddPage(p2);

        var cmd = new RemovePageCommand(doc, p1, 0);

        // Act
        service.Execute(cmd);
        Assert.Single(doc.Pages);
        Assert.Equal(p2, doc.Pages[0]);

        service.Undo();

        // Assert
        Assert.Equal(2, doc.Pages.Count);
        Assert.Equal(p1, doc.Pages[0]);
    }
}
