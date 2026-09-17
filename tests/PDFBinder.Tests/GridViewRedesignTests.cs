using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// Issue #76 グリッドビューの並び替え・複数ページ移動・外部PDF挿入の単体テスト
/// </summary>
public class GridViewRedesignTests
{
    [Fact]
    public void ReorderPagesCommand_MultiplePages_ReordersAndRestores()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        var pages = Enumerable.Range(0, 5).Select(i => new PdfPageModel { OriginalPageIndex = i }).ToList();
        foreach (var p in pages) doc.AddPage(p);

        // [0, 1, 2, 3, 4] -> pages[1] と pages[3] を先頭に移動 -> [1, 3, 0, 2, 4]
        var targetOrder = new List<PdfPageModel> { pages[1], pages[3], pages[0], pages[2], pages[4] };
        var cmd = new ReorderPagesCommand(doc, pages, targetOrder);

        // Act
        cmd.Execute();

        // Assert
        Assert.Equal(pages[1], doc.Pages[0]);
        Assert.Equal(pages[3], doc.Pages[1]);
        Assert.Equal(pages[0], doc.Pages[2]);
        Assert.Equal(pages[2], doc.Pages[3]);
        Assert.Equal(pages[4], doc.Pages[4]);

        // Undo
        cmd.Undo();
        Assert.Equal(pages[0], doc.Pages[0]);
        Assert.Equal(pages[1], doc.Pages[1]);
        Assert.Equal(pages[2], doc.Pages[2]);
        Assert.Equal(pages[3], doc.Pages[3]);
        Assert.Equal(pages[4], doc.Pages[4]);
    }

    [Fact]
    public void InsertPagesCommand_MultiplePages_InsertsAndRemoves()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        var p0 = new PdfPageModel { OriginalPageIndex = 0 };
        var p1 = new PdfPageModel { OriginalPageIndex = 1 };
        doc.AddPage(p0);
        doc.AddPage(p1);

        var newPages = new List<PdfPageModel>
        {
            new() { OriginalPageIndex = 10 },
            new() { OriginalPageIndex = 11 }
        };

        var cmd = new InsertPagesCommand(doc, newPages, 1);

        // Act
        cmd.Execute();

        // Assert
        Assert.Equal(4, doc.PageCount);
        Assert.Equal(p0, doc.Pages[0]);
        Assert.Equal(newPages[0], doc.Pages[1]);
        Assert.Equal(newPages[1], doc.Pages[2]);
        Assert.Equal(p1, doc.Pages[3]);

        // Undo
        cmd.Undo();
        Assert.Equal(2, doc.PageCount);
        Assert.Equal(p0, doc.Pages[0]);
        Assert.Equal(p1, doc.Pages[1]);
    }

    [Fact]
    public void MainViewModel_MovePages_ReordersSelectedPagesWithUndo()
    {
        // Arrange
        var vm = new MainViewModel();
        for (int i = 0; i < 5; i++)
        {
            vm.AddBlankPage();
        }

        var p0 = vm.Document.Pages[0];
        var p1 = vm.Document.Pages[1];
        var p2 = vm.Document.Pages[2];
        var p3 = vm.Document.Pages[3];
        var p4 = vm.Document.Pages[4];

        // p1 と p3 を選択
        p1.IsSelected = true;
        p3.IsSelected = true;

        // Act: 先頭（index 0）へ一括移動
        vm.MovePages(new[] { p1, p3 }, 0);

        // Assert: [p1, p3, p0, p2, p4]
        Assert.Equal(p1, vm.Document.Pages[0]);
        Assert.Equal(p3, vm.Document.Pages[1]);
        Assert.Equal(p0, vm.Document.Pages[2]);
        Assert.Equal(p2, vm.Document.Pages[3]);
        Assert.Equal(p4, vm.Document.Pages[4]);
        Assert.Equal(1, vm.Document.Pages[0].PageNumber);
        Assert.Equal(2, vm.Document.Pages[1].PageNumber);
        Assert.Equal(3, vm.Document.Pages[2].PageNumber);

        // Undo
        vm.UndoCommand.Execute(null);
        Assert.Equal(p0, vm.Document.Pages[0]);
        Assert.Equal(p1, vm.Document.Pages[1]);
        Assert.Equal(p2, vm.Document.Pages[2]);
        Assert.Equal(p3, vm.Document.Pages[3]);
        Assert.Equal(p4, vm.Document.Pages[4]);
    }

    [Fact]
    public void MainViewModel_MovePages_ToMiddleIndex_MaintainsCorrectOrder()
    {
        // Arrange
        var vm = new MainViewModel();
        for (int i = 0; i < 5; i++)
        {
            vm.AddBlankPage();
        }

        var p0 = vm.Document.Pages[0];
        var p1 = vm.Document.Pages[1];
        var p2 = vm.Document.Pages[2];
        var p3 = vm.Document.Pages[3];
        var p4 = vm.Document.Pages[4];

        // p0 を p2 の直後（index 3）へ移動
        vm.MovePages(new[] { p0 }, 3);

        // Assert: [p1, p2, p0, p3, p4]
        Assert.Equal(p1, vm.Document.Pages[0]);
        Assert.Equal(p2, vm.Document.Pages[1]);
        Assert.Equal(p0, vm.Document.Pages[2]);
        Assert.Equal(p3, vm.Document.Pages[3]);
        Assert.Equal(p4, vm.Document.Pages[4]);
    }

    [Fact]
    public void MainViewModel_MovePages_EmptyOrSameOrder_DoesNotCreateUndo()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        vm.AddBlankPage();
        var initialCanUndo = vm.CanUndo;

        // Act
        vm.MovePages(Array.Empty<PdfPageModel>(), 0);
        vm.MovePages(new[] { vm.Document.Pages[0] }, 0);

        // Assert
        Assert.Equal(initialCanUndo, vm.CanUndo);
    }

    private class FakePdfService : IPdfService
    {
        public Task<PdfDocumentModel> LoadDocumentAsync(string filePath)
        {
            var doc = new PdfDocumentModel { FilePath = filePath };
            doc.AddPage(new PdfPageModel { SourceFilePath = filePath, OriginalPageIndex = 0 });
            doc.AddPage(new PdfPageModel { SourceFilePath = filePath, OriginalPageIndex = 1 });
            return Task.FromResult(doc);
        }

        public Task SaveDocumentAsync(PdfDocumentModel doc, string outputPath) => Task.CompletedTask;
        public Task AppendDocumentAsync(PdfDocumentModel targetDoc, string filePath, int insertIndex = -1) => Task.CompletedTask;
        public PdfPageModel CreateBlankPage(double width = 595.28, double height = 841.89) => new();
        public Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputPath) => Task.CompletedTask;
        public Task<int> SplitAllPagesAsync(PdfDocumentModel doc, string outputDirectory, string baseFileName) => Task.FromResult(0);
    }


    [Fact]
    public async Task MainViewModel_InsertPdfFilesAsync_InsertsPagesAtTargetIndex()
    {
        // Arrange
        var fakePdfService = new FakePdfService();
        var vm = new MainViewModel(pdfService: fakePdfService);

        vm.AddBlankPage(); // index 0
        vm.AddBlankPage(); // index 1
        var originalP0 = vm.Document.Pages[0];
        var originalP1 = vm.Document.Pages[1];

        // Act: index 1 に外部PDF（2ページ）を挿入
        await vm.InsertPdfFilesAsync(new[] { "test.pdf" }, 1);

        // Assert
        Assert.Equal(4, vm.Document.PageCount);
        Assert.Equal(originalP0, vm.Document.Pages[0]);
        Assert.Equal("test.pdf", vm.Document.Pages[1].SourceFilePath);
        Assert.Equal("test.pdf", vm.Document.Pages[2].SourceFilePath);
        Assert.Equal(originalP1, vm.Document.Pages[3]);

        // Undo
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Document.PageCount);
        Assert.Equal(originalP0, vm.Document.Pages[0]);
        Assert.Equal(originalP1, vm.Document.Pages[1]);
    }
}
