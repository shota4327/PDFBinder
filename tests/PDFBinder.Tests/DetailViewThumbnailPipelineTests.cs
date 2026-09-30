using System;
using System.Collections.Generic;
using System.Linq;
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
/// 詳細ビューにおける 4 段階優先順位パイプライン（高解像度・サムネイル統合制御）の単体テスト
/// </summary>
public class DetailViewThumbnailPipelineTests
{
    private class PipelineLoggingRenderer : IPdfRenderer
    {
        public List<string> EventLog { get; } = new();

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
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<BitmapSource?>(cancellationToken);
            }

            bool isThumbnail = (targetWidth == MainViewModel.ThumbnailRenderWidth &&
                                targetHeight == MainViewModel.ThumbnailRenderHeight);

            string stepName = isThumbnail ? "Thumbnail" : "HighRes";
            lock (EventLog)
            {
                EventLog.Add($"{stepName}:Page={pageIndex}:Priority={priority}");
            }

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

        public static BitmapSource CreateDummyBitmap(int width = 10, int height = 10)
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

    private static MainViewModel CreateTestViewModel(int pageCount, PipelineLoggingRenderer? renderer = null)
    {
        var rend = renderer ?? new PipelineLoggingRenderer();
        var vm = new MainViewModel(pdfRenderer: rend);
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
    public void GetRemainingPreloadTargetPages_ExcludesAlreadyGeneratedAndReturnsDistanceOrder()
    {
        // Arrange: 60ページのドキュメントを作成し、0〜10ページはサムネイル生成済みとする
        var vm = CreateTestViewModel(60);
        var dummyBitmap = PipelineLoggingRenderer.CreateDummyBitmap();

        for (int i = 0; i <= 10; i++)
        {
            vm.Document.Pages[i].Thumbnail = dummyBitmap;
            vm.Document.Pages[i].IsThumbnailDirty = false;
        }

        // Act: カレント0ページ基準で先頭50ページの未生成分を取得
        var targets = vm.GetRemainingPreloadTargetPages(currentIndex: 0, maxPageCount: 50);

        // Assert: 11〜49ページ（計39ページ）が昇順（0からの距離順）で取得されること
        Assert.Equal(39, targets.Count);
        Assert.Equal(11, targets.First().OriginalPageIndex);
        Assert.Equal(49, targets.Last().OriginalPageIndex);

        for (int i = 0; i < targets.Count - 1; i++)
        {
            Assert.True(targets[i].OriginalPageIndex < targets[i + 1].OriginalPageIndex);
        }
    }

    [Fact]
    public void GetRemainingPreloadTargetPages_PageInMiddle_ReturnsDistanceOrderWithForwardPriority()
    {
        // Arrange: 60ページのドキュメント、カレント20ページ。10〜30ページ（前後10ページ）は生成済みとする
        var vm = CreateTestViewModel(60);
        var dummyBitmap = PipelineLoggingRenderer.CreateDummyBitmap();

        for (int i = 10; i <= 30; i++)
        {
            vm.Document.Pages[i].Thumbnail = dummyBitmap;
            vm.Document.Pages[i].IsThumbnailDirty = false;
        }

        // Act: カレント20ページ基準で未生成分を取得
        var targets = vm.GetRemainingPreloadTargetPages(currentIndex: 20, maxPageCount: 50);

        // Assert: 距離11において前方（index 31: 20+11）が後方（index 9: 20-11）より優先されること
        Assert.NotEmpty(targets);
        Assert.Equal(31, targets[0].OriginalPageIndex); // 距離11（前方）
        Assert.Equal(9, targets[1].OriginalPageIndex);  // 距離11（後方）

        // 距離12において前方（index 32）が後方（index 8）より優先されること
        Assert.Equal(32, targets[2].OriginalPageIndex);
        Assert.Equal(8, targets[3].OriginalPageIndex);

        // 50ページ以上（index >= 50）は含まれないこと
        Assert.DoesNotContain(targets, p => p.OriginalPageIndex >= 50);
        // 生成済み（10〜30）は含まれないこと
        Assert.DoesNotContain(targets, p => p.OriginalPageIndex >= 10 && p.OriginalPageIndex <= 30);
    }

    [Fact]
    public async Task RunDetailViewPipelineAsync_ExecutesStepsInStrictOrder()
    {
        // Arrange: 60ページのドキュメントでパイプラインを同期的に実行
        var renderer = new PipelineLoggingRenderer();
        var vm = CreateTestViewModel(60, renderer);

        foreach (var item in vm.DetailEditor!.Pages)
        {
            item.PageBackground = null;
            item.LastRenderedWidth = 0;
            item.LastRenderedHeight = 0;
            item.InteractiveData = null;
        }
        renderer.EventLog.Clear();

        using var cts = new CancellationTokenSource();

        // Act: カレント0ページでパイプラインを実行
        await vm.RunDetailViewPipelineAsync(currentIndex: 0, cts: cts, debounceMs: 0);

        // Assert: ログの順序を検証
        var logs = renderer.EventLog;
        Assert.NotEmpty(logs);

        // Step 1: カレントページの高解像度背景（Page=0, Priority=High）
        Assert.StartsWith("HighRes:Page=0:Priority=High", logs[0]);

        // Step 2: 前後10ページのサムネイル（Thumbnail:Page=0〜10）
        var step2Logs = logs.Skip(1).Take(11).ToList();
        Assert.All(step2Logs, log => Assert.StartsWith("Thumbnail:Page=", log));

        // Step 3: 前後1ページの高解像度背景（0ページのカレントの直後: Page=1, Priority=Low）
        int step3Index = 1 + 11;
        Assert.StartsWith("HighRes:Page=1:Priority=Low", logs[step3Index]);

        // Step 4: 先頭50ページの未生成サムネイル（Thumbnail:Page=11〜49）
        var step4Logs = logs.Skip(step3Index + 1).ToList();
        Assert.Equal(39, step4Logs.Count);
        Assert.StartsWith("Thumbnail:Page=11", step4Logs.First());
        Assert.StartsWith("Thumbnail:Page=49", step4Logs.Last());
    }

    [Fact]
    public async Task ScheduleDetailViewPipelineAsync_CancelsPreviousTaskOnRestart()
    {
        // Arrange
        var renderer = new PipelineLoggingRenderer();
        var vm = CreateTestViewModel(60, renderer);

        // Act: ページ0のパイプラインを開始後、直ちにページ5へ切り替えて再スケジュール
        var task1 = vm.ScheduleDetailViewPipelineAsync(debounceMs: 50);
        vm.DetailEditor!.CurrentPage = vm.Document.Pages[5];
        var task2 = vm.ScheduleDetailViewPipelineAsync(debounceMs: 0);

        await Task.WhenAll(task1, task2);

        // Assert: 最終的にページ5の高解像度がレンダリングされていること
        Assert.NotNull(vm.DetailEditor.CurrentPageItem?.PageBackground);
    }

    [Fact]
    public async Task DetailEditor_WithoutHandler_FallbackDirectlyRendersStep1AndStep3()
    {
        // Arrange: PipelineExecutionHandler が null の単体状態
        var renderer = new PipelineLoggingRenderer();
        var doc = new PdfDocumentModel();
        for (int i = 0; i < 5; i++)
        {
            doc.Pages.Add(new PdfPageModel
            {
                PageNumber = i + 1,
                SourceFilePath = @"C:\dummy.pdf",
                OriginalPageIndex = i
            });
        }

        var editor = new DetailEditorViewModel(renderer, doc);
        editor.PipelineExecutionHandler = null; // フォールバック検証のため明示的にnull設定

        // Act: スケジュール実行
        await editor.ScheduleDynamicRender(immediate: true);

        // Assert: カレント（Page 0）および隣接（Page 1）の背景が生成されていること
        Assert.NotNull(editor.Pages[0].PageBackground);
        Assert.NotNull(editor.Pages[1].PageBackground);
        Assert.Null(editor.Pages[2].PageBackground); // 2ページ以降は対象外
    }
}
