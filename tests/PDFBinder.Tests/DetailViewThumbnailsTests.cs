using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 詳細エディタ表示中の前後10ページサムネイル先行生成機能の単体テスト
/// </summary>
public class DetailViewThumbnailsTests
{
    private class DummyPdfRenderer : IPdfRenderer
    {
        public Task<BitmapSource?> RenderPageAsync(
            string? filePath,
            int pageIndex,
            int targetWidth,
            int targetHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
            => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken, RenderPriority.Normal);

        public Task<BitmapSource?> RenderPageAsync(
            string? filePath,
            int pageIndex,
            int targetWidth,
            int targetHeight,
            PageRotation rotation,
            CancellationToken cancellationToken,
            RenderPriority priority)
        {
            var bitmap = CreateDummyBitmap(targetWidth, targetHeight);
            return Task.FromResult<BitmapSource?>(bitmap);
        }

        public BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
            => CreateDummyBitmap(targetWidth, targetHeight);

        public BitmapSource CompositeStrokes(
            BitmapSource baseImage,
            StrokeCollection strokes,
            double originalWidth,
            double originalHeight)
            => baseImage;

        public Task<PageInteractiveData> ExtractInteractiveDataAsync(
            string? filePath,
            int pageIndex,
            double displayWidth,
            double displayHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PageInteractiveData(
                Array.Empty<PdfTextCharacter>(),
                Array.Empty<PdfLinkAnnotation>(),
                rotation));

        private static BitmapSource CreateDummyBitmap(int width, int height)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            var bitmap = BitmapSource.Create(
                w, h, 96, 96,
                PixelFormats.Bgra32, null,
                new byte[w * h * 4], w * 4);
            bitmap.Freeze();
            return bitmap;
        }
    }

    private static MainViewModel CreateTestViewModel(int pageCount)
    {
        var renderer = new DummyPdfRenderer();
        var vm = new MainViewModel(pdfRenderer: renderer);
        vm.IsDetailViewActive = true;

        for (int i = 0; i < pageCount; i++)
        {
            var page = new PdfPageModel
            {
                PageNumber = i + 1,
                SourceFilePath = @"C:\dummy.pdf",
                OriginalPageIndex = i
            };
            vm.Document.Pages.Add(page);
        }

        vm.DetailEditor?.InitializeDocument(vm.Document);
        return vm;
    }

    [Fact]
    public void GetInitialPreloadTargetPages_EmptyDocument_ReturnsEmptyList()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act
        var targets = vm.GetInitialPreloadTargetPages();

        // Assert
        Assert.Empty(targets);
    }

    [Fact]
    public void GetInitialPreloadTargetPages_LargeDocument_ReturnsFirst50PagesInOrder()
    {
        // Arrange (100ページ)
        var vm = CreateTestViewModel(100);

        // Act
        var targets = vm.GetInitialPreloadTargetPages();

        // Assert (先頭50ページが昇順で取得されること)
        Assert.Equal(50, targets.Count);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(i + 1, targets[i].PageNumber);
        }
    }

    [Fact]
    public void GetInitialPreloadTargetPages_SmallDocument_ReturnsAllPagesInOrder()
    {
        // Arrange (30ページ)
        var vm = CreateTestViewModel(30);

        // Act
        var targets = vm.GetInitialPreloadTargetPages();

        // Assert (上限50に達しないため、全30ページが取得されること)
        Assert.Equal(30, targets.Count);
        for (int i = 0; i < 30; i++)
        {
            Assert.Equal(i + 1, targets[i].PageNumber);
        }
    }

    [Fact]
    public void GetInitialPreloadTargetPages_SkipsPagesWithExistingCleanThumbnails()
    {
        // Arrange (60ページ)
        var vm = CreateTestViewModel(60);
        var dummyBitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        dummyBitmap.Freeze();

        // ページ1（0-based 0）とページ3（0-based 2）に既にサムネイルを設定
        vm.Document.Pages[0].Thumbnail = dummyBitmap;
        vm.Document.Pages[0].IsThumbnailDirty = false;

        vm.Document.Pages[2].Thumbnail = dummyBitmap;
        vm.Document.Pages[2].IsThumbnailDirty = false;

        // ページ2（0-based 1）はサムネイルありだがダーティ
        vm.Document.Pages[1].Thumbnail = dummyBitmap;
        vm.Document.Pages[1].IsThumbnailDirty = true;

        // Act
        var targets = vm.GetInitialPreloadTargetPages();

        // Assert: ページ1と3は除外され、ダーティなページ2は含まれること
        Assert.DoesNotContain(vm.Document.Pages[0], targets);
        Assert.DoesNotContain(vm.Document.Pages[2], targets);
        Assert.Contains(vm.Document.Pages[1], targets);
        Assert.Equal(48, targets.Count); // 50件中2件除外
    }

    [Fact]
    public void GetDetailViewTargetPages_EmptyDocument_ReturnsEmptyList()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act
        var targets = vm.GetDetailViewTargetPages(0);

        // Assert
        Assert.Empty(targets);
    }

    [Fact]
    public void GetDetailViewTargetPages_CurrentPageAtBeginning_ReturnsClampedForwardOrder()
    {
        // Arrange (25ページ、カレント = 0)
        var vm = CreateTestViewModel(25);

        // Act
        var targets = vm.GetDetailViewTargetPages(0);

        // Assert (0から半径10なので、0..10の計11ページ)
        Assert.Equal(11, targets.Count);
        for (int i = 0; i <= 10; i++)
        {
            Assert.Equal(i + 1, targets[i].PageNumber);
        }
    }

    [Fact]
    public void GetDetailViewTargetPages_CurrentPageInMiddle_ReturnsDistanceOrderedPages()
    {
        // Arrange (50ページ、カレント = 15: 0-based)
        var vm = CreateTestViewModel(50);

        // Act
        var targets = vm.GetDetailViewTargetPages(15);

        // Assert (距離0: 15、距離1: 16, 14、距離2: 17, 13 ... 距離10: 25, 5 の計21ページ)
        Assert.Equal(21, targets.Count);
        Assert.Equal(16, targets[0].PageNumber); // 0-based 15 => PageNumber 16
        Assert.Equal(17, targets[1].PageNumber); // 0-based 16 => PageNumber 17
        Assert.Equal(15, targets[2].PageNumber); // 0-based 14 => PageNumber 15
        Assert.Equal(18, targets[3].PageNumber); // 0-based 17 => PageNumber 18
        Assert.Equal(14, targets[4].PageNumber); // 0-based 13 => PageNumber 14
        Assert.Equal(26, targets[19].PageNumber); // 0-based 25 => PageNumber 26
        Assert.Equal(6, targets[20].PageNumber);  // 0-based 5 => PageNumber 6
    }

    [Fact]
    public void GetDetailViewTargetPages_SkipsPagesWithExistingCleanThumbnails()
    {
        // Arrange (20ページ、カレント = 5)
        var vm = CreateTestViewModel(20);
        var dummyBitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        dummyBitmap.Freeze();

        // ページ5（0-based 4）とページ6（0-based 5）に既にサムネイルを設定
        vm.Document.Pages[4].Thumbnail = dummyBitmap;
        vm.Document.Pages[4].IsThumbnailDirty = false;

        vm.Document.Pages[5].Thumbnail = dummyBitmap;
        vm.Document.Pages[5].IsThumbnailDirty = false;

        // ページ7（0-based 6）はサムネイルありだがダーティ
        vm.Document.Pages[6].Thumbnail = dummyBitmap;
        vm.Document.Pages[6].IsThumbnailDirty = true;

        // Act
        var targets = vm.GetDetailViewTargetPages(5);

        // Assert: ページ5と6は除外され、ダーティなページ7は含まれること
        Assert.DoesNotContain(vm.Document.Pages[4], targets);
        Assert.DoesNotContain(vm.Document.Pages[5], targets);
        Assert.Contains(vm.Document.Pages[6], targets);
    }

    [Fact]
    public void GetDetailViewTargetPages_ClampsOutOfBoundsIndex()
    {
        // Arrange (15ページ)
        var vm = CreateTestViewModel(15);

        // Act (負のインデックス -5 は 0 にクランプ)
        var targetsNegative = vm.GetDetailViewTargetPages(-5);
        Assert.Equal(1, targetsNegative[0].PageNumber);

        // Act (範囲外のインデックス 100 は 14 にクランプ)
        var targetsOverflow = vm.GetDetailViewTargetPages(100);
        Assert.Equal(15, targetsOverflow[0].PageNumber);
    }

    [Fact]
    public async Task ScheduleDetailViewThumbnailsAsync_WhenDetailViewInactive_DoesNotRun()
    {
        // Arrange
        var vm = CreateTestViewModel(20);
        vm.IsDetailViewActive = false; // グリッド表示モード（全件生成をキャンセル・待機）
        await vm.CancelAndAwaitThumbnailsAsync();

        foreach (var p in vm.Document.Pages)
        {
            p.Thumbnail = null;
        }

        // Act
        var task = vm.ScheduleDetailViewThumbnailsAsync(debounceMs: 0);
        await task;

        // Assert: サムネイルは生成されない
        Assert.All(vm.Document.Pages, p => Assert.Null(p.Thumbnail));
    }

    [Fact]
    public async Task ScheduleDetailViewThumbnailsAsync_ExecutesAndGeneratesThumbnailsForWindow()
    {
        // Arrange (30ページ、カレント = 10)
        var vm = CreateTestViewModel(30);
        Assert.NotNull(vm.DetailEditor);
        vm.DetailEditor.CurrentPage = vm.Document.Pages[10];

        // Act (デバウンス0で実行)
        var task = vm.ScheduleDetailViewThumbnailsAsync(debounceMs: 0);
        await task;

        // Assert: 10を中心とした前後10ページ（0..20）のみサムネイルが生成され、21ページ以降はnullのまま
        for (int i = 0; i <= 20; i++)
        {
            Assert.NotNull(vm.Document.Pages[i].Thumbnail);
            Assert.False(vm.Document.Pages[i].IsThumbnailDirty);
        }

        for (int i = 21; i < 30; i++)
        {
            Assert.Null(vm.Document.Pages[i].Thumbnail);
        }
    }

    [Fact]
    public async Task ScheduleDetailViewThumbnailsAsync_CancelsWhenPageChangesRapidly()
    {
        // Arrange (30ページ)
        var vm = CreateTestViewModel(30);

        // Act: ページ0でデバウンス100msでスケジュール直後に、ページ20へ切り替え
        var task1 = vm.ScheduleDetailViewThumbnailsAsync(debounceMs: 100);
        vm.DetailEditor!.CurrentPage = vm.Document.Pages[20];
        var task2 = vm.ScheduleDetailViewThumbnailsAsync(debounceMs: 0);

        await task2;

        // Assert: ページ20周辺のサムネイルが生成されていること
        Assert.NotNull(vm.Document.Pages[20].Thumbnail);
    }
}
