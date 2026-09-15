using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.Controls;
using PDFBinder.App.Converters;
using PDFBinder.App.Models;
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

    [Fact]
    public void InitialValues_PenDefaultIsBlackAnd1px()
    {
        // Arrange & Act
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Assert: ペンの初期値は黒色・太さ1.0px
        Assert.Equal(EditorToolMode.Pen, vm.SelectedTool);
        Assert.Equal(Colors.Black, vm.SelectedColor);
        Assert.Equal(1.0, vm.StrokeThickness);

        // プリセット（0.5, 1.0, 2.0, 4.0）および選択状態の確認
        Assert.Equal(4, vm.ActiveThicknessPresets.Count);
        Assert.Equal(0.5, vm.ActiveThicknessPresets[0].Thickness);
        Assert.False(vm.ActiveThicknessPresets[0].IsSelected);
        Assert.Equal(1.0, vm.ActiveThicknessPresets[1].Thickness);
        Assert.True(vm.ActiveThicknessPresets[1].IsSelected);
        Assert.Equal(2.0, vm.ActiveThicknessPresets[2].Thickness);
        Assert.False(vm.ActiveThicknessPresets[2].IsSelected);
        Assert.Equal(4.0, vm.ActiveThicknessPresets[3].Thickness);
        Assert.False(vm.ActiveThicknessPresets[3].IsSelected);

        // 有効化フラグの確認
        Assert.True(vm.CanChangeThickness);
        Assert.True(vm.CanChangeColor);
    }

    [Fact]
    public void ToolStatePreservation_PenAndHighlighterRetainIndependentColorAndThickness()
    {
        // Arrange
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Act: ペンの色を赤、太さを2.0pxに変更
        var redColor = Color.FromRgb(0xEF, 0x44, 0x44);
        vm.SelectedColor = redColor;
        vm.StrokeThickness = 2.0;

        // Act: 蛍光ペンに切り替え
        vm.SelectedTool = EditorToolMode.Highlighter;

        // Assert: 蛍光ペンの初期値（黄色、12.0px）が適用され、プリセットが蛍光ペン用になる
        Assert.Equal(DetailEditorViewModel.YellowPresetColor, vm.SelectedColor);
        Assert.Equal(12.0, vm.StrokeThickness);
        Assert.Equal(4, vm.ActiveThicknessPresets.Count);
        Assert.Equal(8.0, vm.ActiveThicknessPresets[0].Thickness);
        Assert.Equal(12.0, vm.ActiveThicknessPresets[1].Thickness);
        Assert.True(vm.ActiveThicknessPresets[1].IsSelected);
        Assert.Equal(16.0, vm.ActiveThicknessPresets[2].Thickness);
        Assert.Equal(24.0, vm.ActiveThicknessPresets[3].Thickness);

        // Act: 蛍光ペンの色を青、太さを16.0pxに変更
        var blueColor = Color.FromRgb(0x25, 0x63, 0xEB);
        vm.SelectedColor = blueColor;
        vm.StrokeThickness = 16.0;
        Assert.True(vm.ActiveThicknessPresets[2].IsSelected);

        // Act: ペンに復帰
        vm.SelectedTool = EditorToolMode.Pen;

        // Assert: ペンの直前状態（赤、2.0px）が完全に保持されていること
        Assert.Equal(redColor, vm.SelectedColor);
        Assert.Equal(2.0, vm.StrokeThickness);

        // Act: 蛍光ペンに再切り替え
        vm.SelectedTool = EditorToolMode.Highlighter;

        // Assert: 蛍光ペンの直前状態（青、16.0px）が完全に保持されていること
        Assert.Equal(blueColor, vm.SelectedColor);
        Assert.Equal(16.0, vm.StrokeThickness);
    }

    [Fact]
    public void ToolStatePreservation_EraserPointRetainsThicknessAndSharesHighlighterPresets()
    {
        // Arrange
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Act: 部分消しゴムに切り替え
        vm.SelectedTool = EditorToolMode.EraserPoint;

        // Assert: 初期太さ12.0px、プリセットは8, 12, 16, 24px
        Assert.Equal(12.0, vm.StrokeThickness);
        Assert.Equal(4, vm.ActiveThicknessPresets.Count);
        Assert.Equal(8.0, vm.ActiveThicknessPresets[0].Thickness);
        Assert.True(vm.ActiveThicknessPresets[1].IsSelected); // 12.0px
        Assert.Equal(24.0, vm.ActiveThicknessPresets[3].Thickness);
        Assert.True(vm.CanChangeThickness);
        Assert.False(vm.CanChangeColor);

        // Act: 太さを24.0pxに変更
        vm.StrokeThickness = 24.0;
        Assert.True(vm.ActiveThicknessPresets[3].IsSelected); // 24.0px

        // Act: ペンに切り替え
        vm.SelectedTool = EditorToolMode.Pen;
        Assert.Equal(1.0, vm.StrokeThickness);

        // Act: 再度部分消しゴムに切り替え
        vm.SelectedTool = EditorToolMode.EraserPoint;

        // Assert: 部分消しゴムの太さ24.0pxが保持されていること
        Assert.Equal(24.0, vm.StrokeThickness);
        Assert.True(vm.ActiveThicknessPresets[3].IsSelected);
    }

    [Fact]
    public void ToolAvailability_CanChangeThicknessAndCanChangeColor_ReflectsSelectedTool()
    {
        // Arrange
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Pen
        vm.SelectedTool = EditorToolMode.Pen;
        Assert.True(vm.CanChangeThickness);
        Assert.True(vm.CanChangeColor);

        // Highlighter
        vm.SelectedTool = EditorToolMode.Highlighter;
        Assert.True(vm.CanChangeThickness);
        Assert.True(vm.CanChangeColor);

        // EraserPoint
        vm.SelectedTool = EditorToolMode.EraserPoint;
        Assert.True(vm.CanChangeThickness);
        Assert.False(vm.CanChangeColor);

        // EraserStroke
        vm.SelectedTool = EditorToolMode.EraserStroke;
        Assert.False(vm.CanChangeThickness);
        Assert.False(vm.CanChangeColor);

        // Select
        vm.SelectedTool = EditorToolMode.Select;
        Assert.False(vm.CanChangeThickness);
        Assert.False(vm.CanChangeColor);

        // Hand
        vm.SelectedTool = EditorToolMode.Hand;
        Assert.False(vm.CanChangeThickness);
        Assert.False(vm.CanChangeColor);
    }

    [Fact]
    public void Converters_DoubleEqualsToBooleanConverter_WorksCorrectly()
    {
        var converter = new DoubleEqualsToBooleanConverter();

        // 一致ケース (1.0 vs 1.0, 1.0001 vs 1.0)
        Assert.True((bool)converter.Convert([1.0, 1.0], typeof(bool), null!, null!));
        Assert.True((bool)converter.Convert([1.001, 1.0], typeof(bool), null!, null!));

        // 不一致ケース (1.0 vs 2.0)
        Assert.False((bool)converter.Convert([1.0, 2.0], typeof(bool), null!, null!));

        // 境界・UnsetValue
        Assert.False((bool)converter.Convert([System.Windows.DependencyProperty.UnsetValue, 1.0], typeof(bool), null!, null!));
    }

    [Fact]
    public void Converters_ColorEqualsToVisibilityConverter_WorksCorrectly()
    {
        var converter = new ColorEqualsToVisibilityConverter();

        // 一致ケース -> Visible
        Assert.Equal(
            System.Windows.Visibility.Visible,
            converter.Convert([Colors.Black, Colors.Black], typeof(System.Windows.Visibility), null!, null!));

        // 不一致ケース -> Collapsed
        Assert.Equal(
            System.Windows.Visibility.Collapsed,
            converter.Convert([Colors.Black, Colors.Red], typeof(System.Windows.Visibility), null!, null!));
    }

    [Fact]
    public void Converters_ColorToContrastingBrushConverter_WorksCorrectly()
    {
        var converter = new ColorToContrastingBrushConverter();

        // 暗い色（黒、濃い青） -> 白ブラシ
        var blackBrush = converter.Convert(Colors.Black, typeof(Brush), null!, null!);
        Assert.Same(Brushes.White, blackBrush);

        // 明るい色（黄、白） -> 黒ブラシ
        var yellowBrush = converter.Convert(DetailEditorViewModel.YellowPresetColor, typeof(Brush), null!, null!);
        Assert.Same(Brushes.Black, yellowBrush);
    }

    [Fact]
    public void Converters_CountToVisibilityConverter_WorksCorrectly()
    {
        var converter = new CountToVisibilityConverter();

        // 0件 -> Collapsed
        Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert(0, typeof(System.Windows.Visibility), null!, null!));

        // 1件以上 -> Visible
        Assert.Equal(System.Windows.Visibility.Visible, converter.Convert(1, typeof(System.Windows.Visibility), null!, null!));
        Assert.Equal(System.Windows.Visibility.Visible, converter.Convert(5, typeof(System.Windows.Visibility), null!, null!));
    }

    [Fact]
    public void UpdatePageStrokeCache_GeneratesCacheBitmap_WhenStrokesExist()
    {
        // Arrange
        var page = CreateSamplePage();
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(50.0, 50.0) });
        page.InkStrokes.Add(stroke);

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        var item = vm.Pages[0];

        // Act
        vm.UpdatePageStrokeCache(item);

        // Assert
        Assert.NotNull(item.StrokeCache);
        Assert.True(item.StrokeCache.PixelWidth > 0);
        Assert.True(item.StrokeCache.PixelHeight > 0);
    }

    [Fact]
    public void UpdatePageStrokeCache_SetsNull_WhenNoStrokes()
    {
        // Arrange
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        var item = vm.Pages[0];

        // Act
        vm.UpdatePageStrokeCache(item);

        // Assert
        Assert.Null(item.StrokeCache);
    }

    [Fact]
    public void PageViewMode_DefaultIsSinglePage()
    {
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        Assert.Equal(DetailPageViewMode.SinglePage, vm.PageViewMode);
    }

    [Fact]
    public void InitialState_WithoutDocument_PagesEmptyAndCurrentPageItemNull()
    {
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, (PdfDocumentModel?)null);

        Assert.Empty(vm.Pages);
        Assert.Null(vm.CurrentPageItem);
        Assert.Null(vm.CurrentPage);
        Assert.Equal(DetailPageViewMode.SinglePage, vm.PageViewMode);
    }

    [Fact]
    public void PageViewMode_SwitchToContinuous_TriggersScrollRequest()
    {
        var page = CreateSamplePage();
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        PdfPageModel? requestedPage = null;
        vm.ScrollToPageRequested += p => requestedPage = p;

        vm.SetPageViewMode(DetailPageViewMode.Continuous);

        Assert.Equal(DetailPageViewMode.Continuous, vm.PageViewMode);
        Assert.Same(page, requestedPage);
    }

    [Fact]
    public void FitMode_SinglePageMode_RecalculatesFitOnPageChange()
    {
        // 異なる幅を持つ2ページ
        var page1 = CreateSamplePage(500, 800);
        page1.PageNumber = 1;
        var page2 = CreateSamplePage(1000, 800);
        page2.PageNumber = 2;

        var doc = new PdfDocumentModel();
        doc.Pages.Add(page1);
        doc.Pages.Add(page2);

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);
        vm.PageViewMode = DetailPageViewMode.SinglePage;
        vm.UpdateViewportSize(880, 1000); // availableWidth = 880 - 80 - 2 = 798

        vm.FitMode = DetailViewFitMode.FitToWidth;
        // page1 の幅500, 高さ800 -> 縦スクロール発生のため 780 / 500 = 1.56
        Assert.Equal(1.56, vm.Zoom, 2);

        // page2 へ移動
        vm.GoToNextPage();
        // page2 の幅1000, 高さ800 -> 縦スクロールなしのため 798 / 1000 = 0.798 ≒ 0.80
        Assert.Equal(0.8, vm.Zoom, 2);
    }

    [Fact]
    public void FitMode_ContinuousMode_DoesNotRecalculateFitOnCurrentPageChange()
    {
        // 異なる幅を持つ2ページ
        var page1 = CreateSamplePage(500, 800);
        page1.PageNumber = 1;
        var page2 = CreateSamplePage(1000, 800);
        page2.PageNumber = 2;

        var doc = new PdfDocumentModel();
        doc.Pages.Add(page1);
        doc.Pages.Add(page2);

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);
        vm.PageViewMode = DetailPageViewMode.Continuous;
        vm.UpdateViewportSize(880, 1000);

        vm.FitMode = DetailViewFitMode.FitToWidth;
        double initialZoom = vm.Zoom; // page1基準（スクロールバー考慮）: 1.56

        // スクロール等で CurrentPage が page2 に変わった場合
        vm.CurrentPage = page2;

        // 連続表示モードではスクロール途中で拡大率は固定維持されるべき
        Assert.Equal(initialZoom, vm.Zoom, 2);
    }

    [Fact]
    public void FitMode_ContinuousMode_RecalculatesFitOnWindowResize()
    {
        var page1 = CreateSamplePage(500, 800);
        page1.PageNumber = 1;
        var page2 = CreateSamplePage(1000, 800);
        page2.PageNumber = 2;

        var doc = new PdfDocumentModel();
        doc.Pages.Add(page1);
        doc.Pages.Add(page2);

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);
        vm.PageViewMode = DetailPageViewMode.Continuous;
        vm.UpdateViewportSize(880, 1000);

        vm.FitMode = DetailViewFitMode.FitToWidth;
        Assert.Equal(1.56, vm.Zoom, 2);

        // カレントページを page2 にしてウィンドウをリサイズ
        vm.CurrentPage = page2;
        // リサイズ発生 (ViewportWidth = 1080 -> availableWidth = 1080 - 82 = 998, 縦スクロール考慮で 980)
        vm.UpdateViewportSize(1080, 1000);

        // リサイズ時はカレントページ（page2: 幅1000）を基準に再計算 -> 980 / 1000 = 0.98
        Assert.Equal(0.98, vm.Zoom, 2);
    }

    [Fact]
    public void FitMode_SinglePageMode_FitToWindow_FitsCompletelyInsideViewport()
    {
        // 縦長ページ（A4比率: 595 x 842）
        var page = CreateSamplePage(595, 842);
        var doc = new PdfDocumentModel();
        doc.Pages.Add(page);

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);
        vm.PageViewMode = DetailPageViewMode.SinglePage;

        // ビューポートサイズ: 800 x 600
        double viewportW = 800.0;
        double viewportH = 600.0;
        vm.UpdateViewportSize(viewportW, viewportH);

        // FitToWindow 適用
        vm.FitMode = DetailViewFitMode.FitToWindow;

        // 計算後のコンテンツサイズ（Padding=40、影マージン=40、SafetyBuffer=2 を考慮）
        double renderedWidth = page.DisplayWidth * vm.Zoom;
        double renderedHeight = page.DisplayHeight * vm.Zoom;

        // コンテンツ幅 + 合計水平マージン(80.0) が ViewportWidth 以内に確実に収まる（スクロールバーが出ない）
        Assert.True(renderedWidth + DetailEditorViewModel.TotalHorizontalMargin <= viewportW);

        // コンテンツ高さ + 合計垂直マージン(80.0) が ViewportHeight 以内に確実に収まる（スクロールバーが出ない）
        Assert.True(renderedHeight + DetailEditorViewModel.TotalVerticalMargin <= viewportH);

        // 影用マージン(上下左右20px)が確保されているため、上下影(上12px, 下20px)・左右影(16px)が完全に収まる
        Assert.True(DetailEditorViewModel.PageShadowMargin >= 20.0);
        Assert.True(DetailEditorViewModel.ScrollViewerPadding == 20.0);
    }

    [Fact]
    public void InitializeDocument_SetsFirstAndLastPageFlagsCorrectly()
    {
        // 3ページのドキュメントを作成
        var doc = new PdfDocumentModel();
        doc.Pages.Add(CreateSamplePage(500, 700));
        doc.Pages.Add(CreateSamplePage(500, 700));
        doc.Pages.Add(CreateSamplePage(500, 700));

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);

        // 1ページ目: 先頭=true, 最終=false
        Assert.True(vm.Pages[0].IsFirstPage);
        Assert.False(vm.Pages[0].IsLastPage);

        // 2ページ目: 先頭=false, 最終=false
        Assert.False(vm.Pages[1].IsFirstPage);
        Assert.False(vm.Pages[1].IsLastPage);

        // 3ページ目: 先頭=false, 最終=true
        Assert.False(vm.Pages[2].IsFirstPage);
        Assert.True(vm.Pages[2].IsLastPage);
    }

    [Fact]
    public void GetNextZoomIn_And_GetNextZoomOut_SnapToPresetStepsCorrectly()
    {
        // 等倍(1.0)からのズームイン・ズームアウト
        Assert.Equal(1.25, DetailEditorViewModel.GetNextZoomIn(1.0));
        Assert.Equal(0.75, DetailEditorViewModel.GetNextZoomOut(1.0));

        // 端数(1.37)からのズームイン・ズームアウト（直近上位・下位へのスナップ）
        Assert.Equal(1.5, DetailEditorViewModel.GetNextZoomIn(1.37));
        Assert.Equal(1.25, DetailEditorViewModel.GetNextZoomOut(1.37));

        // 高倍率域(16.0)からのズームイン・ズームアウト
        Assert.Equal(20.0, DetailEditorViewModel.GetNextZoomIn(16.0));
        Assert.Equal(14.0, DetailEditorViewModel.GetNextZoomOut(16.0));

        // 境界値（上限 32.0）
        Assert.Equal(DetailEditorViewModel.MaxZoom, DetailEditorViewModel.GetNextZoomIn(32.0));
        Assert.Equal(DetailEditorViewModel.MaxZoom, DetailEditorViewModel.GetNextZoomIn(35.0));
        Assert.Equal(28.0, DetailEditorViewModel.GetNextZoomOut(32.0));

        // 境界値（下限 0.5）
        Assert.Equal(DetailEditorViewModel.MinZoom, DetailEditorViewModel.GetNextZoomOut(0.5));
        Assert.Equal(DetailEditorViewModel.MinZoom, DetailEditorViewModel.GetNextZoomOut(0.2));
        Assert.Equal(0.75, DetailEditorViewModel.GetNextZoomIn(0.5));
    }

    [Fact]
    public void ZoomIn_And_ZoomOut_Commands_TraverseStepsAndClamp()
    {
        var doc = new PdfDocumentModel();
        doc.Pages.Add(CreateSamplePage(500, 700));
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);

        // 初期値 1.0
        Assert.Equal(1.0, vm.Zoom);

        // ズームインを繰り返して MaxZoom (32.0) まで到達
        for (int i = 0; i < DetailEditorViewModel.ZoomSnapSteps.Length; i++)
        {
            vm.ZoomInCommand.Execute(null);
        }
        Assert.Equal(DetailEditorViewModel.MaxZoom, vm.Zoom);

        // 最大値からの追加ズームインでも MaxZoom を維持
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(DetailEditorViewModel.MaxZoom, vm.Zoom);

        // ズームアウトを繰り返して MinZoom (0.5) まで到達
        for (int i = 0; i < DetailEditorViewModel.ZoomSnapSteps.Length + 5; i++)
        {
            vm.ZoomOutCommand.Execute(null);
        }
        Assert.Equal(DetailEditorViewModel.MinZoom, vm.Zoom);

        // 最小値からの追加ズームアウトでも MinZoom を維持
        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(DetailEditorViewModel.MinZoom, vm.Zoom);
    }

    [Fact]
    public void CalculateRenderDimensions_ClampsToMaxRenderDimension8192()
    {
        var page = CreateSamplePage(1000, 1000);
        var doc = new PdfDocumentModel();
        doc.Pages.Add(page);
        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);

        // 32倍ズーム時、1000pt * 1.333 * 32 ≒ 42666px だが 8192px にクランプされる
        var (width, height) = vm.CalculateRenderDimensions(page, 32.0);
        Assert.Equal(DetailEditorViewModel.MaxRenderDimension, width);
        Assert.Equal(DetailEditorViewModel.MaxRenderDimension, height);
        Assert.Equal(8192, DetailEditorViewModel.MaxRenderDimension);
    }

    [Fact]
    public void FitMode_SinglePageMode_LandscapePage_FitToWindow_FitsCompletelyInsideViewport()
    {
        // 横長ページ（A4横: 842 x 595）
        var page = CreateSamplePage(842, 595);
        var doc = new PdfDocumentModel();
        doc.Pages.Add(page);

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);
        vm.PageViewMode = DetailPageViewMode.SinglePage;

        // ビューポートサイズ: 800 x 600
        double viewportW = 800.0;
        double viewportH = 600.0;
        vm.UpdateViewportSize(viewportW, viewportH);

        // FitToWindow 適用（横幅が上限になる）
        vm.FitMode = DetailViewFitMode.FitToWindow;

        double renderedWidth = page.DisplayWidth * vm.Zoom;
        double renderedHeight = page.DisplayHeight * vm.Zoom;

        // コンテンツ幅 + 合計水平マージン(80.0) が ViewportWidth 以内に確実に収まる
        Assert.True(renderedWidth + DetailEditorViewModel.TotalHorizontalMargin <= viewportW);
        // コンテンツ高さ + 合計垂直マージン(80.0) が ViewportHeight 以内に確実に収まる
        Assert.True(renderedHeight + DetailEditorViewModel.TotalVerticalMargin <= viewportH);
    }

    [Fact]
    public void InitializeDocument_ConsecutiveCalls_UpdatesCurrentPageItem_AndRaisesPropertyChanged()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        doc.Pages.Add(CreateSamplePage(500, 700));
        doc.Pages.Add(CreateSamplePage(500, 700));

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);

        var firstItem = vm.CurrentPageItem;
        Assert.NotNull(firstItem);
        Assert.Same(doc.Pages[0], firstItem.Page);

        int currentPageItemChangedCount = 0;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DetailEditorViewModel.CurrentPageItem))
            {
                currentPageItemChangedCount++;
            }
        };

        // Act: 同一ドキュメントで再度 InitializeDocument を実行
        vm.InitializeDocument(doc);

        // Assert: CurrentPageItem の変更通知が発火し、Pages 内の最新インスタンスを参照していること
        Assert.True(currentPageItemChangedCount > 0);
        var newItem = vm.CurrentPageItem;
        Assert.NotNull(newItem);
        Assert.NotSame(firstItem, newItem);
        Assert.Same(doc.Pages[0], newItem.Page);
        Assert.Contains(newItem, vm.Pages);
        Assert.True(newItem.IsCurrent);
    }

    [Fact]
    public void InitializeDocument_RetainsCurrentPage_WhenPageStillExists()
    {
        // Arrange: 3ページのドキュメントで2ページ目を選択
        var doc = new PdfDocumentModel();
        doc.Pages.Add(CreateSamplePage(500, 700));
        doc.Pages.Add(CreateSamplePage(500, 700));
        doc.Pages.Add(CreateSamplePage(500, 700));

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc);
        vm.CurrentPage = doc.Pages[1];

        // Act: ドキュメントを再初期化（白紙追加・結合時等の挙動を模倣）
        vm.InitializeDocument(doc);

        // Assert: 2ページ目が維持されていること
        Assert.Same(doc.Pages[1], vm.CurrentPage);
        Assert.NotNull(vm.CurrentPageItem);
        Assert.Same(doc.Pages[1], vm.CurrentPageItem.Page);
        Assert.True(vm.Pages[1].IsCurrent);
    }

    [Fact]
    public void InitializeDocument_SelectsFirstPage_WhenPreviousPageNoLongerExists()
    {
        // Arrange: 以前のドキュメントで2ページ目を選択
        var doc1 = new PdfDocumentModel();
        doc1.Pages.Add(CreateSamplePage(500, 700));
        doc1.Pages.Add(CreateSamplePage(500, 700));

        var renderer = new FakePdfRenderer();
        using var vm = new DetailEditorViewModel(renderer, doc1);
        vm.CurrentPage = doc1.Pages[1];

        // Act: 異なるドキュメントで初期化
        var doc2 = new PdfDocumentModel();
        doc2.Pages.Add(CreateSamplePage(600, 800));
        doc2.Pages.Add(CreateSamplePage(600, 800));
        vm.InitializeDocument(doc2);

        // Assert: 存在しないため新規ドキュメントの先頭ページが選択されること
        Assert.Same(doc2.Pages[0], vm.CurrentPage);
        Assert.NotNull(vm.CurrentPageItem);
        Assert.Same(doc2.Pages[0], vm.CurrentPageItem.Page);
        Assert.True(vm.Pages[0].IsCurrent);
    }
}
