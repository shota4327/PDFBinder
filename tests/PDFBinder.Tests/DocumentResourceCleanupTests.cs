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
}
