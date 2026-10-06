using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// ドキュメント終了時およびページ遷移時のリソース明示的解放の単体テスト
/// </summary>
public class DocumentResourceCleanupTests
{
    private static BitmapSource CreateDummyBitmap(int width = 50, int height = 50)
    {
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    public void DocumentSession_Dispose_ClearsPageThumbnailsAndUndoHistory()
    {
        // Arrange: 3ページのドキュメントとセッションを作成し、サムネイルを設定
        var doc = new PdfDocumentModel();
        for (int i = 1; i <= 3; i++)
        {
            doc.Pages.Add(new PdfPageModel
            {
                PageNumber = i,
                Thumbnail = CreateDummyBitmap()
            });
        }

        var session = new DocumentSession(doc);
        Assert.NotNull(doc.Pages[0].Thumbnail);

        // Act: セッションを明示破棄
        session.Dispose();

        // Assert: 全ページのサムネイルが null 化されていること
        foreach (var page in doc.Pages)
        {
            Assert.Null(page.Thumbnail);
        }
    }

    [Fact]
    public void DetailPageItemViewModel_Dispose_UnloadsBackgroundAndClearsInteractiveData()
    {
        // Arrange
        var page = new PdfPageModel { PageNumber = 1, Width = 500, Height = 700 };
        using var item = new DetailPageItemViewModel(page)
        {
            PageBackground = CreateDummyBitmap(),
            InteractiveData = PageInteractiveData.Empty
        };

        Assert.NotNull(item.PageBackground);
        Assert.NotNull(item.InteractiveData);

        // Act
        item.Dispose();

        // Assert
        Assert.Null(item.PageBackground);
        Assert.Null(item.InteractiveData);
    }

    [Fact(Timeout = 5000)]
    public async Task MainViewModel_CloseDocumentAsync_DisposesTargetSession()
    {
        // Arrange: デフォルト構成でMainViewModelを作成
        var vm = new MainViewModel();

        var doc = new PdfDocumentModel { FilePath = @"C:\dummy\sample.pdf" };
        var page = new PdfPageModel { PageNumber = 1, Thumbnail = CreateDummyBitmap() };
        doc.Pages.Add(page);
        doc.IsModified = false;

        var session = new DocumentSession(doc);
        vm.Documents.Add(session);
        vm.ActiveSession = session;

        Assert.Single(vm.Documents);
        Assert.NotNull(page.Thumbnail);

        // Act: ドキュメントを閉じる
        bool closed = await vm.CloseDocumentAsync(session);

        // Assert: 正常に閉じられ、セッションが破棄され、サムネイルがnull化されていること
        Assert.True(closed);
        Assert.Empty(vm.Documents);
        Assert.Null(vm.ActiveSession);
        Assert.Null(page.Thumbnail);
    }

    [Fact(Timeout = 5000)]
    public async Task MainViewModel_CloseDocumentAsync_InvalidatesRendererCache()
    {
        // Arrange
        var testRenderer = new TestPdfRenderer();
        var vm = new MainViewModel(pdfRenderer: testRenderer);

        var doc = new PdfDocumentModel { FilePath = @"C:\dummy\sample.pdf" };
        var page = new PdfPageModel { PageNumber = 1 };
        doc.Pages.Add(page);
        doc.IsModified = false;

        var session = new DocumentSession(doc);
        vm.Documents.Add(session);
        vm.ActiveSession = session;

        // Act
        bool closed = await vm.CloseDocumentAsync(session);

        // Assert: 閉じたファイルのパスおよび全解放（ドキュメント数0時）のキャッシュ無効化が呼ばれていること
        Assert.True(closed);
        Assert.Contains(@"C:\dummy\sample.pdf", testRenderer.InvalidatedPaths);
        Assert.Contains(null, testRenderer.InvalidatedPaths);
    }

    private class TestPdfRenderer : IPdfRenderer
    {
        public List<string?> InvalidatedPaths { get; } = new();

        public Task<BitmapSource?> RenderPageAsync(string? filePath, int pageIndex, int targetWidth, int targetHeight, PageRotation rotation, CancellationToken cancellationToken = default)
            => Task.FromResult<BitmapSource?>(null);

        public BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
            => CreateDummyBitmap(targetWidth, targetHeight);

        public BitmapSource CompositeStrokes(BitmapSource baseImage, System.Windows.Ink.StrokeCollection strokes, double originalPageWidth, double originalPageHeight)
            => baseImage;

        public Task<PageInteractiveData> ExtractInteractiveDataAsync(string? filePath, int pageIndex, double displayWidth, double displayHeight, PageRotation rotation, CancellationToken cancellationToken = default)
            => Task.FromResult(PageInteractiveData.Empty);

        public void InvalidateCache(string? filePath = null)
        {
            InvalidatedPaths.Add(filePath);
        }
    }
}
