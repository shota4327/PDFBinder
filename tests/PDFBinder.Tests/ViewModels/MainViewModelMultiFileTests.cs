using System.IO;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests.ViewModels;

/// <summary>
/// 複数ファイルのオープン・切り替え・個別終了に関する単体テストクラス (Issue #51)
/// </summary>
public class MainViewModelMultiFileTests
{
    private class MockMultiPdfService : IPdfService
    {
        public List<string> OpenedPaths { get; } = new();
        public List<string> SavedPaths { get; } = new();

        public Task<PdfDocumentModel> LoadDocumentAsync(string filePath)
        {
            OpenedPaths.Add(filePath);
            var doc = new PdfDocumentModel
            {
                FilePath = filePath
            };
            doc.AddPage(new PdfPageModel { SourceFilePath = filePath, Width = 500, Height = 700 });
            doc.IsModified = false;
            return Task.FromResult(doc);
        }

        public Task AppendDocumentAsync(PdfDocumentModel targetDocument, string sourceFilePath, int insertIndex = -1)
        {
            targetDocument.AddPage(new PdfPageModel { SourceFilePath = sourceFilePath });
            return Task.CompletedTask;
        }

        public Task SaveDocumentAsync(PdfDocumentModel document, string targetFilePath)
        {
            SavedPaths.Add(targetFilePath);
            document.FilePath = targetFilePath;
            document.IsModified = false;
            return Task.CompletedTask;
        }

        public Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputFilePath) => Task.CompletedTask;
        public Task<int> SplitAllPagesAsync(PdfDocumentModel document, string outputDirectory, string baseFileName) => Task.FromResult(document.PageCount);

        public PdfPageModel CreateBlankPage(double width, double height)
        {
            return new PdfPageModel
            {
                Width = width,
                Height = height
            };
        }
    }

    [Fact]
    public async Task OpenDocument_WhenMultipleFiles_CreatesSeparateSessionsAndIncreasesDropdownCount()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        // Act
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");

        // Assert
        Assert.Equal(2, vm.Documents.Count);
        Assert.NotNull(vm.ActiveSession);
        Assert.Equal("DocB.pdf", vm.ActiveSession.Document.FileName);
        Assert.True(vm.HasOpenDocuments);
        Assert.Equal("DocB.pdf", vm.DisplayFileName);
    }

    [Fact]
    public async Task OpenDocument_WhenDuplicateFilePath_ActivatesExistingSessionWithoutDuplicating()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        Assert.Equal(2, vm.Documents.Count);
        Assert.Equal("DocB.pdf", vm.ActiveSession?.Document.FileName);

        // Act - DocA を再度開く
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");

        // Assert - セッション数は増えず、DocAがアクティブになる
        Assert.Equal(2, vm.Documents.Count);
        Assert.Equal("DocA.pdf", vm.ActiveSession?.Document.FileName);
    }

    [Fact]
    public async Task SwitchDocument_ChangesActiveSessionAndRestoresState()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;
        sessionA.IsDetailViewActive = true;
        sessionA.ZoomFactor = 1.5;

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        var sessionB = vm.ActiveSession!;
        sessionB.IsDetailViewActive = false;

        // Act
        vm.SwitchDocument(sessionA);

        // Assert
        Assert.Equal(sessionA, vm.ActiveSession);
        Assert.True(vm.IsDetailViewActive);
        Assert.Equal("DocA.pdf", vm.DisplayFileName);
    }

    [Fact]
    public async Task IndependentUndoRedo_EditsInOneDocument_DoNotPolluteOtherDocumentHistory()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        var sessionB = vm.ActiveSession!;

        // Act 1: DocB で白紙追加（1操作実行）
        vm.AddBlankPage();
        Assert.True(vm.CanUndo);

        // Act 2: DocA に切り替え
        vm.SwitchDocument(sessionA);

        // Assert: DocA は何も操作していないため CanUndo が false
        Assert.False(vm.CanUndo);

        // Act 3: 再び DocB に切り替え
        vm.SwitchDocument(sessionB);

        // Assert: DocB は CanUndo が true のまま
        Assert.True(vm.CanUndo);

        // Act 4: 元に戻す実行
        vm.Undo();
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);
    }

    [Fact]
    public void AddBlankPage_WhenNoDocumentOpen_CreatesUntitledPdfAndSetsActive()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);
        Assert.Empty(vm.Documents);
        Assert.Null(vm.ActiveSession);
        Assert.Equal(string.Empty, vm.DisplayFileName);

        // Act
        vm.AddBlankPage();

        // Assert
        Assert.Single(vm.Documents);
        Assert.NotNull(vm.ActiveSession);
        Assert.Equal("名称未設定.pdf", vm.ActiveSession.Document.FileName);
        Assert.Equal("名称未設定.pdf", vm.DisplayFileName);
        Assert.Equal(1, vm.ActiveSession.Document.PageCount);
        Assert.True(vm.HasOpenDocuments);
    }

    [Fact]
    public async Task CloseDocument_WhenNotModified_RemovesSessionAndActivatesAdjacent()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocC.pdf");

        var sessionB = vm.Documents[1];
        vm.SwitchDocument(sessionB);
        Assert.Equal("DocB.pdf", vm.ActiveSession?.Document.FileName);

        // Act: アクティブな DocB を閉じる
        bool closed = await vm.CloseDocumentAsync(sessionB);

        // Assert
        Assert.True(closed);
        Assert.Equal(2, vm.Documents.Count);
        // 隣接するセッション（インデックス1にある DocC）がアクティブ化
        Assert.Equal("DocC.pdf", vm.ActiveSession?.Document.FileName);
    }

    [Fact]
    public async Task CloseDocument_WhenModified_PromptsSave_Cancel_AbortsClosing()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;
        sessionA.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Cancel;

        // Act
        bool closed = await vm.CloseDocumentAsync(sessionA);

        // Assert
        Assert.False(closed);
        Assert.Single(vm.Documents);
        Assert.Equal(sessionA, vm.ActiveSession);
    }

    [Fact]
    public async Task CloseDocument_WhenModified_PromptsSave_Save_SavesAndCloses()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;
        sessionA.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Save;

        // Act
        bool closed = await vm.CloseDocumentAsync(sessionA);

        // Assert
        Assert.True(closed);
        Assert.Empty(vm.Documents);
        Assert.Null(vm.ActiveSession);
        Assert.Single(service.SavedPaths);
        Assert.Equal(@"C:\Folder\DocA.pdf", service.SavedPaths[0]);
    }

    [Fact]
    public async Task CloseDocument_WhenAllDocumentsClosed_TransitionsToEmptyState()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        Assert.Single(vm.Documents);

        // Act
        bool closed = await vm.CloseDocumentAsync(vm.ActiveSession);

        // Assert
        Assert.True(closed);
        Assert.Empty(vm.Documents);
        Assert.Null(vm.ActiveSession);
        Assert.False(vm.HasOpenDocuments);
        Assert.Equal(string.Empty, vm.DisplayFileName);
        Assert.Equal(0, vm.Document.PageCount);
    }

    [Fact]
    public async Task ConfirmSaveAllAsync_WhenMultipleModifiedDocuments_PromptsSequentially()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        vm.ActiveSession!.Document.IsModified = true;

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        vm.ActiveSession!.Document.IsModified = true;

        var promptedFiles = new List<string>();
        vm.ConfirmSavePrompt = fileName =>
        {
            promptedFiles.Add(fileName);
            return SaveConfirmationResult.Save;
        };

        // Act
        bool canClose = await vm.ConfirmSaveAllAsync();

        // Assert
        Assert.True(canClose);
        Assert.Equal(2, promptedFiles.Count);
        Assert.Contains("DocA.pdf", promptedFiles);
        Assert.Contains("DocB.pdf", promptedFiles);
        Assert.Equal(2, service.SavedPaths.Count);
    }

    [Fact]
    public async Task ConfirmSaveAllAsync_WhenUserCancels_AbortsAndReturnsFalse()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        vm.ActiveSession!.Document.IsModified = true;

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        vm.ActiveSession!.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Cancel;

        // Act
        bool canClose = await vm.ConfirmSaveAllAsync();

        // Assert
        Assert.False(canClose);
        Assert.Empty(service.SavedPaths);
    }

    [Fact]
    public async Task DisplayTitle_WhenModified_DisplaysAsterisk()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var session = vm.ActiveSession!;

        // Assert - 未変更時
        Assert.Equal("DocA.pdf", session.DisplayTitle);

        // Act - 変更あり
        session.Document.IsModified = true;

        // Assert - 変更後
        Assert.Equal("DocA.pdf *", session.DisplayTitle);
    }
}
