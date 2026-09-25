using System.IO;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests.ViewModels;

/// <summary>
/// 画像データの直接編集およびUI制御に関する単体テストクラス (Issue #167)
/// </summary>
public class MainViewModelImageTests
{
    private class MockImageService : IImageService
    {
        public List<string> LoadedPaths { get; } = new();
        public List<string> SavedPaths { get; } = new();
        public List<string> SavedPdfPaths { get; } = new();

        public bool IsSupportedImage(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string ext = Path.GetExtension(filePath);
            return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase);
        }

        public Task<PdfDocumentModel> LoadImageDocumentAsync(string filePath)
        {
            LoadedPaths.Add(filePath);
            var doc = new PdfDocumentModel
            {
                FilePath = filePath,
                DocumentKind = DocumentKind.Image
            };
            doc.AddPage(new PdfPageModel
            {
                SourceFilePath = filePath,
                Width = 400,
                Height = 300,
                OriginalRotation = PageRotation.Rotate0,
                Rotation = PageRotation.Rotate0
            });
            doc.IsModified = false;
            return Task.FromResult(doc);
        }

        public Task SaveImageAsync(PdfDocumentModel doc, string outputPath)
        {
            SavedPaths.Add(outputPath);
            doc.FilePath = outputPath;
            doc.IsModified = false;
            return Task.CompletedTask;
        }

        public Task SaveImageAsPdfAsync(PdfDocumentModel doc, string outputPath)
        {
            SavedPdfPaths.Add(outputPath);
            doc.FilePath = outputPath;
            doc.IsModified = false;
            return Task.CompletedTask;
        }
    }

    private class MockPdfService : IPdfService
    {
        public List<string> LoadedPaths { get; } = new();

        public Task<PdfDocumentModel> LoadDocumentAsync(string filePath)
        {
            LoadedPaths.Add(filePath);
            var doc = new PdfDocumentModel { FilePath = filePath };
            doc.AddPage(new PdfPageModel { SourceFilePath = filePath, Width = 595, Height = 842 });
            doc.AddPage(new PdfPageModel { SourceFilePath = filePath, Width = 595, Height = 842 });
            doc.IsModified = false;
            return Task.FromResult(doc);
        }

        public Task AppendDocumentAsync(PdfDocumentModel targetDocument, string sourceFilePath, int insertIndex = -1) => Task.CompletedTask;
        public Task SaveDocumentAsync(PdfDocumentModel document, string targetFilePath) => Task.CompletedTask;
        public Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputFilePath) => Task.CompletedTask;
        public Task<int> SplitAllPagesAsync(PdfDocumentModel document, string outputDirectory, string baseFileName) => Task.FromResult(1);
        public Task<List<PdfPageModel>> SplitPagesHalfAsync(IEnumerable<PdfPageModel> pages, CancellationToken cancellationToken = default) => Task.FromResult(new List<PdfPageModel>());
        public PdfPageModel CreateBlankPage(double width = 595.28, double height = 841.89) => new() { Width = width, Height = height };
    }

    [Fact]
    public async Task OpenSingleDocumentAsync_WithPng_OpensImageSessionAndForcesDetailView()
    {
        var mockImageService = new MockImageService();
        var mockPdfService = new MockPdfService();
        var vm = new MainViewModel(pdfService: mockPdfService, imageService: mockImageService);

        string testPng = @"C:\images\sample.png";
        await vm.OpenSingleDocumentAsync(testPng);

        Assert.Single(vm.Documents);
        Assert.NotNull(vm.ActiveSession);
        Assert.True(vm.ActiveSession.IsImage);
        Assert.True(vm.IsImageDocumentActive);
        Assert.True(vm.IsDetailViewActive);
        Assert.Contains(testPng, mockImageService.LoadedPaths);
        Assert.Empty(mockPdfService.LoadedPaths);
    }

    [Fact]
    public async Task ImageDocumentActive_DisablesIncompatibleFeatures()
    {
        var mockImageService = new MockImageService();
        var vm = new MainViewModel(imageService: mockImageService);

        await vm.OpenSingleDocumentAsync(@"C:\images\sample.jpg");

        // 画像編集では使用不可なコマンド・機能の無効化を検証
        Assert.False(vm.CanAppendDocument);
        Assert.False(vm.AppendDocumentCommand.CanExecute(null));

        Assert.False(vm.CanAddBlankPage);
        Assert.False(vm.AddBlankPageCommand.CanExecute(null));

        Assert.False(vm.CanExportSelectedPages);
        Assert.False(vm.ExportSelectedPagesCommand.CanExecute(null));

        Assert.False(vm.CanSplitAllPages);
        Assert.False(vm.SplitAllPagesCommand.CanExecute(null));

        Assert.False(vm.CanSplitPagesHalf);
        Assert.False(vm.SplitPagesHalfCommand.CanExecute(null));

        Assert.False(vm.CanDeleteSelectedPages);
        Assert.False(vm.DeleteSelectedPagesCommand.CanExecute(null));

        Assert.False(vm.CanToggleViewMode);
        Assert.False(vm.CanToggleContinuousScroll);
        Assert.False(vm.CanClosePageDetail);
        Assert.False(vm.ClosePageDetailCommand.CanExecute(null));

        Assert.False(vm.CanGoToPreviousPage);
        Assert.False(vm.CanGoToNextPage);
        Assert.False(vm.CanNavigatePages);
    }

    [Fact]
    public async Task ImageDocumentActive_SuppressesGridViewSwitch()
    {
        var mockImageService = new MockImageService();
        var vm = new MainViewModel(imageService: mockImageService);

        await vm.OpenSingleDocumentAsync(@"C:\images\photo.png");

        Assert.True(vm.IsDetailViewActive);

        // 外部やショートカット等から false に変更を試みても true に保持されることを検証
        vm.IsDetailViewActive = false;
        Assert.True(vm.IsDetailViewActive);

        vm.ClosePageDetail();
        Assert.True(vm.IsDetailViewActive);
    }

    [Fact]
    public async Task HandleFileDropAsync_MultipleImages_OpensAsSeparateSessions()
    {
        var mockImageService = new MockImageService();
        var vm = new MainViewModel(imageService: mockImageService);

        var dropFiles = new[]
        {
            @"C:\images\pic1.jpg",
            @"C:\images\pic2.png",
            @"C:\images\pic3.jpeg"
        };

        await vm.HandleFileDropAsync(dropFiles);

        Assert.Equal(3, vm.Documents.Count);
        Assert.All(vm.Documents, d => Assert.True(d.IsImage));
        Assert.Equal(@"C:\images\pic3.jpeg", vm.ActiveSession?.Document.FilePath);
    }

    [Fact]
    public async Task InsertPdfFilesAsync_WithImages_OpensImagesAsSeparateDocumentsInsteadOfInserting()
    {
        var mockImageService = new MockImageService();
        var mockPdfService = new MockPdfService();
        var vm = new MainViewModel(pdfService: mockPdfService, imageService: mockImageService);

        // 最初にPDFを開く
        await vm.OpenSingleDocumentAsync(@"C:\docs\initial.pdf");
        Assert.Single(vm.Documents);
        int initialPageCount = vm.Document.PageCount;
        Assert.Equal(2, initialPageCount);

        // グリッドやビューへのドロップとして画像とPDFを同時挿入
        var dropFiles = new[]
        {
            @"C:\images\dropped.png",
            @"C:\docs\inserted.pdf"
        };

        await vm.InsertPdfFilesAsync(dropFiles, 1);

        // 画像は新規セッションとして追加され、PDFのページへは直接挿入されないことを検証
        Assert.Equal(2, vm.Documents.Count);
        Assert.Contains(vm.Documents, d => d.IsImage && d.Document.FilePath == @"C:\images\dropped.png");
        Assert.Equal(4, vm.Documents[0].Document.PageCount);
    }

    [Fact]
    public async Task SaveDocumentSessionAsync_WithImageDocument_CallsImageServiceSave()
    {
        var mockImageService = new MockImageService();
        var vm = new MainViewModel(imageService: mockImageService);

        string imagePath = @"C:\images\edit_target.png";
        await vm.OpenSingleDocumentAsync(imagePath);

        vm.Document.Pages[0].RotateClockwise();
        Assert.True(vm.Document.IsModified);

        bool saved = await vm.SaveDocumentSessionAsync();

        Assert.True(saved);
        Assert.Contains(imagePath, mockImageService.SavedPaths);
        Assert.False(vm.Document.IsModified);
    }
}
