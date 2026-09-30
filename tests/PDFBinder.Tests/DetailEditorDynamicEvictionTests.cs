using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 詳細エディタにおける動的背景アンロード（エビクション機構）の単体テスト
/// </summary>
public class DetailEditorDynamicEvictionTests
{
    private static BitmapSource CreateDummyBitmap(int width = 100, int height = 100)
    {
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bmp.Freeze();
        return bmp;
    }

    private static PdfPageModel CreatePage(int pageNumber, bool addStroke = false)
    {
        var page = new PdfPageModel
        {
            PageNumber = pageNumber,
            Width = 595.28,
            Height = 841.89
        };

        if (addStroke)
        {
            var points = new StylusPointCollection { new StylusPoint(10, 10), new StylusPoint(20, 20) };
            page.InkStrokes.Add(new Stroke(points));
        }

        return page;
    }

    [Fact]
    public void UnloadBackground_ClearsPageBackground_AndClearsStrokeCacheWhenNoStrokes()
    {
        // Arrange: 手書きストロークを持たないページ
        var page = CreatePage(1, addStroke: false);
        using var item = new DetailPageItemViewModel(page)
        {
            PageBackground = CreateDummyBitmap(),
            StrokeCache = CreateDummyBitmap(),
            LastRenderedWidth = 800,
            LastRenderedHeight = 1100,
            LastRenderedRotation = PageRotation.Rotate0
        };

        // Act: 背景アンロードを実行
        item.UnloadBackground();

        // Assert: 背景画像およびストロークキャッシュが破棄され、寸法がリセットされること
        Assert.Null(item.PageBackground);
        Assert.Null(item.StrokeCache);
        Assert.Equal(0, item.LastRenderedWidth);
        Assert.Equal(0, item.LastRenderedHeight);
        Assert.Equal(PageRotation.Rotate0, item.LastRenderedRotation);
    }

    [Fact]
    public void UnloadBackground_PreservesStrokeCache_WhenPageHasStrokes()
    {
        // Arrange: 手書きストロークを保持するページ
        var page = CreatePage(1, addStroke: true);
        var strokeBmp = CreateDummyBitmap();
        using var item = new DetailPageItemViewModel(page)
        {
            PageBackground = CreateDummyBitmap(),
            StrokeCache = strokeBmp,
            LastRenderedWidth = 800,
            LastRenderedHeight = 1100
        };

        // Act: 背景アンロードを実行
        item.UnloadBackground();

        // Assert: 背景画像は解放されるが、手書き線画の表示消失を防ぐためストロークキャッシュは維持されること
        Assert.Null(item.PageBackground);
        Assert.NotNull(item.StrokeCache);
        Assert.Same(strokeBmp, item.StrokeCache);
    }

    [Fact]
    public void EvictOffscreenPageBackgrounds_SinglePageMode_KeepsWithinRadius_AndEvictsBeyondRadius()
    {
        // Arrange: 10ページのドキュメントを作成し、全ページにダミー背景画像を設定
        var doc = new PdfDocumentModel();
        for (int i = 1; i <= 10; i++)
        {
            doc.Pages.Add(CreatePage(i));
        }

        var renderer = new PdfiumRenderer();
        using var vm = new DetailEditorViewModel(renderer);
        vm.InitializeDocument(doc);
        vm.PageViewMode = DetailPageViewMode.SinglePage;

        foreach (var item in vm.Pages)
        {
            item.PageBackground = CreateDummyBitmap();
        }

        // Act: カレントページを5ページ目（インデックス4）に設定してエビクション実行
        // 保持範囲: 半径3ページ（インデックス 1〜7、ページ 2〜8）
        // 解放対象: インデックス 0（ページ1）およびインデックス 8, 9（ページ 9, 10）
        vm.CurrentPage = vm.Pages[4].Page;
        vm.EvictOffscreenPageBackgrounds();

        // Assert: 保持範囲内（インデックス 1〜7）は維持
        for (int i = 1; i <= 7; i++)
        {
            Assert.NotNull(vm.Pages[i].PageBackground);
        }

        // Assert: 保持範囲外（インデックス 0, 8, 9）はアンロードされていること
        Assert.Null(vm.Pages[0].PageBackground);
        Assert.Null(vm.Pages[8].PageBackground);
        Assert.Null(vm.Pages[9].PageBackground);
    }

    [Fact]
    public void EvictOffscreenPageBackgrounds_ContinuousMode_ProtectsVisiblePagesAndBuffer()
    {
        // Arrange: 10ページのドキュメントを作成し、全ページにダミー背景画像を設定
        var doc = new PdfDocumentModel();
        for (int i = 1; i <= 10; i++)
        {
            doc.Pages.Add(CreatePage(i));
        }

        var renderer = new PdfiumRenderer();
        using var vm = new DetailEditorViewModel(renderer);
        vm.InitializeDocument(doc);
        vm.PageViewMode = DetailPageViewMode.Continuous;

        foreach (var item in vm.Pages)
        {
            item.PageBackground = CreateDummyBitmap();
        }

        // Act: 可視ページプロバイダーがインデックス 4 と 5 を返すように設定
        // 可視範囲: インデックス 4〜5
        // 保持範囲: 最低インデックス 4 - 3 = 1 から、最大インデックス 5 + 3 = 8 まで（インデックス 1〜8）
        // 解放対象: インデックス 0 および 9
        vm.VisiblePagesProvider = () => new[] { vm.Pages[4], vm.Pages[5] };
        vm.EvictOffscreenPageBackgrounds();

        // Assert: 可視範囲＋前後3ページ（インデックス 1〜8）は維持
        for (int i = 1; i <= 8; i++)
        {
            Assert.NotNull(vm.Pages[i].PageBackground);
        }

        // Assert: 範囲外（インデックス 0, 9）はアンロードされていること
        Assert.Null(vm.Pages[0].PageBackground);
        Assert.Null(vm.Pages[9].PageBackground);
    }
}
