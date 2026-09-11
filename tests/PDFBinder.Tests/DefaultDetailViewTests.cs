using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// Issue #23 詳細ビュー基本・表示タブ新設・縦連続表示・オンデマンドサムネイルの単体テスト
/// </summary>
public class DefaultDetailViewTests
{
    [Fact]
    public void MainViewModel_DefaultView_IsDetailViewActive()
    {
        // Arrange & Act
        var vm = new MainViewModel();

        // Assert: デフォルトで詳細ビューがアクティブであること
        Assert.True(vm.IsDetailViewActive);
        Assert.NotNull(vm.DetailEditor);
        Assert.Equal(0, vm.SelectedRibbonTabIndex); // 初期タブは「PDF編集」
    }

    [Fact]
    public void MainViewModel_UnifiedZoom_DetailView_OperatesDetailZoom()
    {
        // Arrange
        var vm = new MainViewModel();
        Assert.True(vm.IsDetailViewActive);
        Assert.NotNull(vm.DetailEditor);

        // 初期倍率は100%
        Assert.Equal("100%", vm.CurrentZoomText);
        Assert.True(vm.CanZoomIn);
        Assert.True(vm.CanZoomOut);

        // 拡大 (1.0 -> 1.25)
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(1.25, vm.DetailEditor.Zoom);
        Assert.Equal("125%", vm.CurrentZoomText);

        // 縮小 (1.25 -> 1.0)
        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(1.0, vm.DetailEditor.Zoom);
        Assert.Equal("100%", vm.CurrentZoomText);

        // 拡大後にリセット
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(1.5, vm.DetailEditor.Zoom);
        vm.ZoomResetCommand.Execute(null);
        Assert.Equal(1.0, vm.DetailEditor.Zoom);
        Assert.Equal("100%", vm.CurrentZoomText);
    }

    [Fact]
    public void MainViewModel_UnifiedZoom_GridView_OperatesThumbnailSize()
    {
        // Arrange: グリッドビューに切り替え
        var vm = new MainViewModel
        {
            IsDetailViewActive = false
        };

        // 初期サムネイルサイズ（220px）= 100%
        Assert.Equal(MainViewModel.DefaultThumbnailSize, vm.ThumbnailSize);
        Assert.Equal("100%", vm.CurrentZoomText);
        Assert.True(vm.CanZoomIn);
        Assert.True(vm.CanZoomOut);

        // 拡大 (220 -> 240)
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(240.0, vm.ThumbnailSize);

        // 縮小 (240 -> 220)
        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(220.0, vm.ThumbnailSize);
        Assert.Equal("100%", vm.CurrentZoomText);

        // 変更後にリセット
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        vm.ZoomResetCommand.Execute(null);
        Assert.Equal(MainViewModel.DefaultThumbnailSize, vm.ThumbnailSize);
        Assert.Equal("100%", vm.CurrentZoomText);
    }

    [Fact]
    public void MainViewModel_SwitchToGridView_FromHandwritingTab_SwitchesToViewTab()
    {
        // Arrange: 詳細ビューで手書きタブ（1）を選択
        var vm = new MainViewModel
        {
            IsDetailViewActive = true,
            SelectedRibbonTabIndex = 1
        };

        // Act: グリッドビューへ切り替え
        vm.IsDetailViewActive = false;

        // Assert: 手書きタブが無効化されるため、表示タブ（2）へ自動切り替え
        Assert.Equal(2, vm.SelectedRibbonTabIndex);
    }

    [Fact]
    public void MainViewModel_DetailView_Operations_TargetCurrentPageWhenNoSelection()
    {
        // Arrange: 2ページのドキュメントを作成
        var vm = new MainViewModel();
        vm.AddBlankPage();
        vm.AddBlankPage();
        Assert.Equal(2, vm.Document.PageCount);

        // 全ページの IsSelected は false
        Assert.All(vm.Document.Pages, p => Assert.False(p.IsSelected));

        // Act 1: ページ回転（詳細ビューのカレントページを対象）
        var page1 = vm.Document.Pages[0];
        Assert.Equal(page1, vm.DetailEditor?.CurrentPage);
        vm.RotateClockwiseCommand.Execute(null);
        Assert.Equal(PageRotation.Rotate90, page1.Rotation);

        // Act 2: カレントページをページ2に移動して削除
        var page2 = vm.Document.Pages[1];
        vm.DetailEditor?.ScrollToPage(page2);
        Assert.Equal(page2, vm.DetailEditor?.CurrentPage);
        vm.DeleteSelectedPagesCommand.Execute(null);

        // Assert: ページ2が削除され1ページのみ残る
        Assert.Equal(1, vm.Document.PageCount);
        Assert.Equal(page1, vm.Document.Pages[0]);
    }

    [Fact]
    public async Task MainViewModel_EnsureThumbnailsGenerated_GeneratesOnlyMissingThumbnails()
    {
        // Arrange
        var renderer = new PdfiumRenderer();
        var vm = new MainViewModel(pdfRenderer: renderer);
        vm.AddBlankPage();
        vm.AddBlankPage();

        // 初期状態ではサムネイルが未生成のページが存在
        var page1 = vm.Document.Pages[0];
        var page2 = vm.Document.Pages[1];
        page1.Thumbnail = null;
        page2.Thumbnail = null;

        // Act: サムネイルオンデマンド生成を実行
        await vm.EnsureThumbnailsGeneratedAsync();

        // Assert: 全ページのサムネイルが生成されていること
        Assert.NotNull(page1.Thumbnail);
        Assert.NotNull(page2.Thumbnail);
    }

    [Fact]
    public void DetailEditorViewModel_ScrollToPage_UpdatesCurrentAndRaisesEvent()
    {
        // Arrange
        var renderer = new PdfiumRenderer();
        var doc = new PdfDocumentModel();
        var p1 = new PdfPageModel { PageNumber = 1 };
        var p2 = new PdfPageModel { PageNumber = 2 };
        doc.Pages.Add(p1);
        doc.Pages.Add(p2);

        using var vm = new DetailEditorViewModel(renderer, doc);
        PdfPageModel? requestedPage = null;
        vm.ScrollToPageRequested += page => requestedPage = page;

        // Act
        vm.ScrollToPage(p2);

        // Assert
        Assert.Equal(p2, vm.CurrentPage);
        Assert.Equal(p2, requestedPage);
        Assert.True(vm.Pages[1].IsCurrent);
        Assert.False(vm.Pages[0].IsCurrent);
    }

    [Fact]
    public async Task MainViewModel_HandleFileDrop_WhenNoDocument_OpensFirstAndAppendsSubsequent()
    {
        // Arrange
        var mockService = new MockPdfService();
        var vm = new MainViewModel(pdfService: mockService);
        Assert.Equal(0, vm.Document.PageCount);

        var dropFiles = new[] { "c:\\sample1.pdf", "c:\\sample2.pdf" };

        // Act
        await vm.HandleFileDropAsync(dropFiles);

        // Assert
        Assert.Single(mockService.OpenedFiles);
        Assert.Equal("c:\\sample1.pdf", mockService.OpenedFiles[0]);
        Assert.Single(mockService.AppendedFiles);
        Assert.Equal("c:\\sample2.pdf", mockService.AppendedFiles[0]);
        Assert.Equal(2, vm.Document.PageCount);
    }

    [Fact]
    public async Task MainViewModel_HandleFileDrop_WhenDocumentLoaded_AppendsAllFiles()
    {
        // Arrange
        var mockService = new MockPdfService();
        var vm = new MainViewModel(pdfService: mockService);
        vm.AddBlankPage(); // 既存ページ1枚
        Assert.Equal(1, vm.Document.PageCount);

        var dropFiles = new[] { "c:\\sample1.pdf", "c:\\sample2.pdf" };

        // Act
        await vm.HandleFileDropAsync(dropFiles);

        // Assert
        Assert.Empty(mockService.OpenedFiles);
        Assert.Equal(2, mockService.AppendedFiles.Count);
        Assert.Equal("c:\\sample1.pdf", mockService.AppendedFiles[0]);
        Assert.Equal("c:\\sample2.pdf", mockService.AppendedFiles[1]);
        Assert.Equal(3, vm.Document.PageCount);
    }

    [Fact]
    public async Task MainViewModel_HandleFileDrop_FiltersNonPdfFiles()
    {
        // Arrange
        var mockService = new MockPdfService();
        var vm = new MainViewModel(pdfService: mockService);
        var dropFiles = new[] { "c:\\document.docx", "c:\\image.png", "c:\\data.txt" };

        // Act
        await vm.HandleFileDropAsync(dropFiles);

        // Assert
        Assert.Empty(mockService.OpenedFiles);
        Assert.Empty(mockService.AppendedFiles);
        Assert.Equal(0, vm.Document.PageCount);
    }

    private class MockPdfService : IPdfService
    {
        public List<string> OpenedFiles { get; } = new();
        public List<string> AppendedFiles { get; } = new();

        public Task<PdfDocumentModel> LoadDocumentAsync(string filePath)
        {
            OpenedFiles.Add(filePath);
            var doc = new PdfDocumentModel { FilePath = filePath };
            doc.Pages.Add(new PdfPageModel { PageNumber = 1, SourceFilePath = filePath });
            return Task.FromResult(doc);
        }

        public Task AppendDocumentAsync(PdfDocumentModel targetDoc, string filePath, int insertIndex = -1)
        {
            AppendedFiles.Add(filePath);
            targetDoc.Pages.Add(new PdfPageModel { PageNumber = targetDoc.Pages.Count + 1, SourceFilePath = filePath });
            return Task.CompletedTask;
        }

        public PdfPageModel CreateBlankPage(double width = 595.28, double height = 841.89)
        {
            return new PdfPageModel { Width = width, Height = height };
        }

        public Task SaveDocumentAsync(PdfDocumentModel doc, string outputPath) => Task.CompletedTask;
        public Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputPath) => Task.CompletedTask;
        public Task<int> SplitAllPagesAsync(PdfDocumentModel doc, string outputDirectory, string baseFileName) => Task.FromResult(0);
    }
}
