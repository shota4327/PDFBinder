using System.Windows.Media.Imaging;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="DetailEditorViewModel"/> の動的レンダリング、デバウンス、および競合対策の単体テスト
/// </summary>
public class DetailEditorViewModelTests
{
    private class FakePdfRenderer : IPdfRenderer
    {
        public int RenderCallCount { get; private set; }
        public int LastTargetWidth { get; private set; }
        public int LastTargetHeight { get; private set; }
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public async Task<BitmapSource?> RenderPageAsync(
            string? filePath,
            int pageIndex,
            int targetWidth,
            int targetHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
        {
            RenderCallCount++;
            LastTargetWidth = targetWidth;
            LastTargetHeight = targetHeight;

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
        }

        public BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
        {
            var bitmap = BitmapSource.Create(
                Math.Max(1, targetWidth),
                Math.Max(1, targetHeight),
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                new byte[Math.Max(1, targetWidth) * Math.Max(1, targetHeight) * 4],
                Math.Max(1, targetWidth) * 4);
            bitmap.Freeze();
            return bitmap;
        }

        public BitmapSource CompositeStrokes(
            BitmapSource baseImage,
            System.Windows.Ink.StrokeCollection strokes,
            double originalPageWidth,
            double originalPageHeight)
        {
            return baseImage;
        }
    }

    private static PdfPageModel CreateSamplePage(double width = 595.28, double height = 841.89)
    {
        return new PdfPageModel
        {
            Width = width,
            Height = height,
            PageNumber = 1
        };
    }

    [Fact]
    public void CalculateRenderDimensions_CalculatesCorrectDimensionsForDifferentZooms()
    {
        // Arrange
        var page = CreateSamplePage(600, 800);
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Act & Assert
        // zoom = 1.0 -> 600 * (96/72) = 800, 800 * (96/72) = 1067
        var (w1, h1) = vm.CalculateRenderDimensions(1.0);
        Assert.Equal(800, w1);
        Assert.Equal(1067, h1);

        // zoom = 0.5 (縮小) -> 600 * (96/72) * 0.5 = 400, 800 * (96/72) * 0.5 = 533
        var (wHalf, hHalf) = vm.CalculateRenderDimensions(0.5);
        Assert.Equal(400, wHalf);
        Assert.Equal(533, hHalf);

        // zoom = 2.0 (拡大) -> 600 * (96/72) * 2.0 = 1600, 800 * (96/72) * 2.0 = 2133
        var (wDouble, hDouble) = vm.CalculateRenderDimensions(2.0);
        Assert.Equal(1600, wDouble);
        Assert.Equal(2133, hDouble);
    }

    [Fact]
    public void CalculateRenderDimensions_ClampsToMinAndMaxDimensions()
    {
        // Arrange
        var tinyPage = CreateSamplePage(10, 10);
        var hugePage = CreateSamplePage(5000, 5000);
        var renderer = new FakePdfRenderer();

        using var vmTiny = new DetailEditorViewModel(tinyPage, renderer, () => { }, _ => null);
        using var vmHuge = new DetailEditorViewModel(hugePage, renderer, () => { }, _ => null);

        // Act & Assert: 極小サイズは MinRenderDimension (200) にクランプ
        var (wMin, hMin) = vmTiny.CalculateRenderDimensions(0.5);
        Assert.Equal(DetailEditorViewModel.MinRenderDimension, wMin);
        Assert.Equal(DetailEditorViewModel.MinRenderDimension, hMin);

        // 極大サイズは MaxRenderDimension (4096) にクランプ
        var (wMax, hMax) = vmHuge.CalculateRenderDimensions(3.0);
        Assert.Equal(DetailEditorViewModel.MaxRenderDimension, wMax);
        Assert.Equal(DetailEditorViewModel.MaxRenderDimension, hMax);
    }

    [Fact]
    public async Task InitialRender_SetsPageBackgroundProperly()
    {
        // Arrange
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer();

        // Act
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);
        await vm.LoadPageBackgroundAsync();

        // Assert
        Assert.NotNull(vm.PageBackground);
        Assert.True(renderer.RenderCallCount >= 1);
    }

    [Fact]
    public async Task ZoomChange_DebouncesAndExecutesAfterDelay()
    {
        // Arrange
        var page = CreateSamplePage(600, 800);
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null)
        {
            DebounceDelayMs = 50 // テスト用に短縮
        };
        await vm.LoadPageBackgroundAsync();
        int initialCallCount = renderer.RenderCallCount;

        // Act: ズーム変更直後（デバウンス前）
        vm.Zoom = 2.0;
        Assert.Equal(initialCallCount, renderer.RenderCallCount);

        // 待機（デバウンス経過後）
        await Task.Delay(100);

        // Assert: デバウンス後に最新のズーム倍率でレンダリングが実行されていること
        Assert.True(renderer.RenderCallCount > initialCallCount);
        Assert.Equal(1600, renderer.LastTargetWidth);
        Assert.Equal(2133, renderer.LastTargetHeight);
    }

    [Fact]
    public async Task RapidZoomChanges_CancelsPreviousTasks_AndOnlyAppliesLast()
    {
        // Arrange
        var page = CreateSamplePage(600, 800);
        var renderer = new FakePdfRenderer { Delay = TimeSpan.FromMilliseconds(50) };
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null)
        {
            DebounceDelayMs = 20
        };
        await vm.LoadPageBackgroundAsync();

        // Act: 高速にズームを連続変更
        vm.Zoom = 1.25;
        await Task.Delay(5);
        vm.Zoom = 1.5;
        await Task.Delay(5);
        vm.Zoom = 1.75;
        await Task.Delay(5);
        vm.Zoom = 2.5;

        // 全タスク完了まで待機
        await Task.Delay(150);

        // Assert: 最後のズーム倍率（2.5）の寸法で完了していること
        // 600 * (96/72) * 2.5 = 2000
        Assert.Equal(2000, renderer.LastTargetWidth);
        Assert.NotNull(vm.PageBackground);
    }

    [Fact]
    public async Task DoubleBuffering_DoesNotResetPageBackgroundToNullDuringRender()
    {
        // Arrange
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer { Delay = TimeSpan.FromMilliseconds(50) };
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null)
        {
            DebounceDelayMs = 10
        };
        await vm.LoadPageBackgroundAsync();

        var initialBackground = vm.PageBackground;
        Assert.NotNull(initialBackground);

        // Act: ズームを変更
        vm.Zoom = 1.5;

        // デバウンス中およびレンダリング中（30ms時点）で PageBackground が null になっていないことを検証
        await Task.Delay(30);
        Assert.NotNull(vm.PageBackground);
        Assert.Same(initialBackground, vm.PageBackground);

        // レンダリング完了後
        await Task.Delay(100);
        Assert.NotNull(vm.PageBackground);
        Assert.NotSame(initialBackground, vm.PageBackground);
    }
}
