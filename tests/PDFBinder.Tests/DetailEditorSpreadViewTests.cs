using System.Windows.Input;
using System.Windows.Media.Imaging;
using PDFBinder.App;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 詳細エディタの見開き表示モード・PDF初期表示フラグ反映・ナビゲーション・レンダリング連携の単体テスト
/// </summary>
public class DetailEditorSpreadViewTests
{
    private sealed class FakePdfRenderer : IPdfRenderer
    {
        public Task<BitmapSource?> RenderPageAsync(
            string? filePath,
            int pageIndex,
            int targetWidth,
            int targetHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<BitmapSource?>(CreateBlankPageBitmap(targetWidth, targetHeight, rotation));
        }

        public BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
        {
            int w = Math.Max(1, targetWidth);
            int h = Math.Max(1, targetHeight);
            var bitmap = BitmapSource.Create(
                w, h, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                new byte[w * h * 4],
                w * 4);
            bitmap.Freeze();
            return bitmap;
        }

        public BitmapSource CompositeStrokes(
            BitmapSource baseImage,
            System.Windows.Ink.StrokeCollection strokes,
            double originalPageWidth,
            double originalPageHeight) => baseImage;

        public Task<PageInteractiveData> ExtractInteractiveDataAsync(
            string? filePath,
            int pageIndex,
            double displayWidth,
            double displayHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default) => Task.FromResult(PageInteractiveData.Empty);
    }

    private static PdfDocumentModel CreateDocument(
        int pageCount,
        PdfPageLayout layout = PdfPageLayout.Default,
        PdfReadingDirection direction = PdfReadingDirection.LeftToRight,
        DocumentKind kind = DocumentKind.Pdf,
        double pageWidth = 600,
        double pageHeight = 800)
    {
        var doc = new PdfDocumentModel
        {
            FilePath = kind == DocumentKind.Image ? "photo.png" : "sample.pdf",
            DocumentKind = kind,
            InitialPageLayout = layout,
            ReadingDirection = direction
        };

        for (int i = 0; i < pageCount; i++)
        {
            doc.Pages.Add(new PdfPageModel
            {
                PageNumber = i + 1,
                OriginalPageIndex = i,
                Width = pageWidth,
                Height = pageHeight
            });
        }

        return doc;
    }

    [Theory]
    [InlineData(PdfPageLayout.TwoPageRight, PdfReadingDirection.LeftToRight, DetailPageViewMode.TwoPage, true, PdfReadingDirection.LeftToRight)]
    [InlineData(PdfPageLayout.TwoPageLeft, PdfReadingDirection.RightToLeft, DetailPageViewMode.TwoPage, false, PdfReadingDirection.RightToLeft)]
    [InlineData(PdfPageLayout.TwoColumnRight, PdfReadingDirection.RightToLeft, DetailPageViewMode.TwoPageContinuous, true, PdfReadingDirection.RightToLeft)]
    [InlineData(PdfPageLayout.OneColumn, PdfReadingDirection.LeftToRight, DetailPageViewMode.Continuous, true, PdfReadingDirection.LeftToRight)]
    [InlineData(PdfPageLayout.Default, PdfReadingDirection.LeftToRight, DetailPageViewMode.SinglePage, true, PdfReadingDirection.LeftToRight)]
    public void InitializeDocument_WithApplyDisplaySettings_AppliesPdfFlags(
        PdfPageLayout layout,
        PdfReadingDirection direction,
        DetailPageViewMode expectedMode,
        bool expectedCover,
        PdfReadingDirection expectedDirection)
    {
        var doc = CreateDocument(4, layout, direction);
        using var vm = new DetailEditorViewModel(new FakePdfRenderer(), doc, autoLoadBackground: false, applyDocumentDisplaySettings: true);

        Assert.Equal(expectedMode, vm.PageViewMode);
        Assert.Equal(expectedCover, vm.ShowCoverPageSeparately);
        Assert.Equal(expectedDirection, vm.ReadingDirection);
    }

    [Fact]
    public void InitializeDocument_WithoutApplyDisplaySettings_PreservesUserSelectedMode()
    {
        var doc = CreateDocument(4, PdfPageLayout.TwoPageRight, PdfReadingDirection.LeftToRight);
        using var vm = new DetailEditorViewModel(new FakePdfRenderer(), doc, autoLoadBackground: false, applyDocumentDisplaySettings: true);

        // ユーザーが手動で単一ページ・右綴じへ変更
        vm.PageViewMode = DetailPageViewMode.SinglePage;
        vm.IsRightToLeft = true;

        // 編集操作等による再同期ではユーザー選択を維持
        vm.InitializeDocument(doc, autoLoadBackground: false, applyDocumentDisplaySettings: false);

        Assert.Equal(DetailPageViewMode.SinglePage, vm.PageViewMode);
        Assert.True(vm.IsRightToLeft);
    }

    [Fact]
    public void InitializeDocument_ImageDocument_ForcesSinglePageAndDisablesSpreadOptions()
    {
        var imageDoc = CreateDocument(1, PdfPageLayout.TwoPageRight, PdfReadingDirection.RightToLeft, DocumentKind.Image);
        var mainVm = new MainViewModel(pdfRenderer: new FakePdfRenderer());
        mainVm.Document = imageDoc;

        Assert.NotNull(mainVm.DetailEditor);
        Assert.Equal(DetailPageViewMode.SinglePage, mainVm.DetailEditor!.PageViewMode);
        Assert.False(mainVm.CanToggleContinuousScroll);
        Assert.False(mainVm.CanConfigureSpreadOptions);
    }

    [Fact]
    public void TwoPageMode_NavigationMovesBySpreadAndHandlesLastSpreadBoundary()
    {
        var doc = CreateDocument(5, PdfPageLayout.TwoPageRight, PdfReadingDirection.LeftToRight);
        using var vm = new DetailEditorViewModel(new FakePdfRenderer(), doc, autoLoadBackground: false, applyDocumentDisplaySettings: true);

        // 行構成: [0], [1,2], [3,4]
        Assert.Equal(3, vm.SpreadRows.Count);
        Assert.Equal(1, vm.CurrentPageNumber);
        Assert.False(vm.CanGoToPreviousPage);
        Assert.True(vm.CanGoToNextPage);

        // 次の見開きへ -> [2, 3]
        vm.GoToNextPage();
        Assert.Equal(2, vm.CurrentPageNumber);
        Assert.True(vm.CanGoToPreviousPage);
        Assert.True(vm.CanGoToNextPage);

        // 次の見開きへ -> [4, 5]
        vm.GoToNextPage();
        Assert.Equal(4, vm.CurrentPageNumber);
        Assert.True(vm.CanGoToPreviousPage);
        Assert.False(vm.CanGoToNextPage);

        // 同じ最終見開き内の5ページ目をアクティブにしても次は無効のまま
        vm.CurrentPage = doc.Pages[4];
        Assert.False(vm.CanGoToNextPage);
        Assert.True(vm.CanGoToPreviousPage);

        // 前の見開きへ -> [2, 3]
        vm.GoToPreviousPage();
        Assert.Equal(2, vm.CurrentPageNumber);
    }

    [Fact]
    public void TwoPageMode_ClickingPartnerPageInSameSpread_PreservesSpreadRowAndZoom()
    {
        var doc = CreateDocument(4, PdfPageLayout.TwoPageLeft, PdfReadingDirection.LeftToRight);
        using var vm = new DetailEditorViewModel(new FakePdfRenderer(), doc, autoLoadBackground: false, applyDocumentDisplaySettings: true);
        vm.UpdateViewportSize(1200, 800);
        vm.SetZoom(1.25);

        var initialRow = vm.CurrentSpreadRow;
        Assert.NotNull(initialRow);
        Assert.Equal(1, vm.CurrentPageNumber);

        // 同じ見開き行内の2ページ目を選択
        vm.CurrentPage = doc.Pages[1];

        Assert.Same(initialRow, vm.CurrentSpreadRow);
        Assert.Equal(1.25, vm.Zoom);
        Assert.Equal(2, vm.CurrentPageNumber);
    }

    [Fact]
    public void TwoPageMode_ApplyFitMode_UsesCombinedSpreadWidth()
    {
        var doc = CreateDocument(4, PdfPageLayout.TwoPageLeft, PdfReadingDirection.LeftToRight, pageWidth: 600, pageHeight: 800);
        using var vm = new DetailEditorViewModel(new FakePdfRenderer(), doc, autoLoadBackground: false, applyDocumentDisplaySettings: true);

        // 見開き幅 = 600 + 16 + 600 = 1216
        // availableWidth = 1268 - 50 - 2 = 1216 -> FitToWidth zoom = 1.0
        vm.UpdateViewportSize(1268, 1200);
        vm.FitMode = DetailViewFitMode.FitToWidth;

        Assert.Equal(1.0, vm.Zoom, precision: 2);
    }

    [Fact]
    public void TwoPageMode_PrimarySecondaryAndEvictionTargetsCoverBothPagesInSpread()
    {
        var doc = CreateDocument(12, PdfPageLayout.TwoPageLeft, PdfReadingDirection.LeftToRight);
        using var vm = new DetailEditorViewModel(new FakePdfRenderer(), doc, autoLoadBackground: false, applyDocumentDisplaySettings: true);

        // 行構成: [0,1], [2,3], [4,5], [6,7], [8,9], [10,11]
        vm.CurrentPage = doc.Pages[2]; // 行1 [2,3]

        var primary = vm.GetPrimaryPagesToRender();
        Assert.Equal(2, primary.Count);
        Assert.Contains(vm.Pages[2], primary);
        Assert.Contains(vm.Pages[3], primary);

        var secondary = vm.GetSecondaryNeighborPagesToRender();
        Assert.Equal(4, secondary.Count);
        Assert.Contains(vm.Pages[0], secondary);
        Assert.Contains(vm.Pages[1], secondary);
        Assert.Contains(vm.Pages[4], secondary);
        Assert.Contains(vm.Pages[5], secondary);

        // 全ページにダミー背景を入れてから Evict 実行（現在行0: 前後3行 = 行0〜3 = ページ0〜7保持、ページ8〜11解放）
        var dummyBitmap = new FakePdfRenderer().CreateBlankPageBitmap(10, 10, PageRotation.Rotate0);
        foreach (var item in vm.Pages)
        {
            item.PageBackground = dummyBitmap;
        }
        vm.CurrentPage = doc.Pages[0];
        vm.EvictOffscreenPageBackgrounds();

        Assert.NotNull(vm.Pages[0].PageBackground);
        Assert.NotNull(vm.Pages[1].PageBackground);
        Assert.NotNull(vm.Pages[7].PageBackground);
        Assert.Null(vm.Pages[8].PageBackground);
        Assert.Null(vm.Pages[11].PageBackground);
    }

    [Fact]
    public void TwoPageContinuousMode_SoloPagesAlignToSpineWhileTwoPageModeCentersSoloPage()
    {
        var doc = CreateDocument(4, PdfPageLayout.TwoPageRight, PdfReadingDirection.LeftToRight, pageWidth: 500, pageHeight: 700);
        using var vm = new DetailEditorViewModel(new FakePdfRenderer(), doc, autoLoadBackground: false, applyDocumentDisplaySettings: true);

        // TwoPage（単一見開き）の表紙単独行は中央配置（スペーサーなし）
        Assert.False(vm.SpreadRows[0].AlignSinglePageToSpine);
        Assert.Equal(0.0, vm.SpreadRows[0].LeftPlaceholderWidth);
        Assert.Equal(500.0, vm.SpreadRows[0].DisplayWidth);

        // TwoPageContinuous に切り替えると表紙は右側配置・左側スペーサーで綴じ目揃え
        vm.PageViewMode = DetailPageViewMode.TwoPageContinuous;
        Assert.True(vm.SpreadRows[0].AlignSinglePageToSpine);
        Assert.Null(vm.SpreadRows[0].LeftPage);
        Assert.NotNull(vm.SpreadRows[0].RightPage);
        Assert.Equal(500.0, vm.SpreadRows[0].LeftPlaceholderWidth);
        Assert.Equal(500.0 * 2 + DetailSpreadRowViewModel.DefaultPageGap, vm.SpreadRows[0].DisplayWidth);
    }

    [Fact]
    public void MainViewModel_MultipleSessions_PreservesSpreadSettingsIndependently()
    {
        var docSpread = CreateDocument(4, PdfPageLayout.TwoPageRight, PdfReadingDirection.LeftToRight);
        docSpread.FilePath = "spread.pdf";
        var docRtl = CreateDocument(4, PdfPageLayout.TwoColumnRight, PdfReadingDirection.RightToLeft);
        docRtl.FilePath = "rtl.pdf";

        var mainVm = new MainViewModel(pdfRenderer: new FakePdfRenderer());
        mainVm.Document = docSpread;
        var sessionSpread = mainVm.ActiveSession!;
        mainVm.Document = docRtl;
        var sessionRtl = mainVm.ActiveSession!;

        Assert.NotNull(mainVm.DetailEditor);
        Assert.Equal(DetailPageViewMode.TwoPageContinuous, mainVm.DetailEditor!.PageViewMode);
        Assert.True(mainVm.DetailEditor.IsRightToLeft);
        Assert.True(mainVm.CanConfigureSpreadOptions);

        // 1つ目のファイルに切り替え
        mainVm.ActiveSession = sessionSpread;
        Assert.Equal(DetailPageViewMode.TwoPage, mainVm.DetailEditor!.PageViewMode);
        Assert.False(mainVm.DetailEditor.IsRightToLeft);
        Assert.True(mainVm.DetailEditor.ShowCoverPageSeparately);

        // 2つ目のファイルに戻る
        mainVm.ActiveSession = sessionRtl;
        Assert.Equal(DetailPageViewMode.TwoPageContinuous, mainVm.DetailEditor!.PageViewMode);
        Assert.True(mainVm.DetailEditor.IsRightToLeft);
    }

    [Fact]
    public void HandlePageNavigation_RightToLeftSpread_ReversesLeftAndRightArrowKeysOnly()
    {
        var doc = CreateDocument(5, PdfPageLayout.TwoPageRight, PdfReadingDirection.RightToLeft);
        var mainVm = new MainViewModel(pdfRenderer: new FakePdfRenderer());
        mainVm.Document = doc;
        mainVm.IsDetailViewActive = true;

        // 行構成: [1], [2,3], [4,5] -> 初期は1ページ目
        Assert.Equal(1, mainVm.CurrentPageNumber);

        // 右綴じ見開きでは ← で次の見開き（2ページ目）へ進む
        Assert.True(MainWindow.HandlePageNavigation(mainVm, Key.Left, ModifierKeys.None));
        Assert.Equal(2, mainVm.CurrentPageNumber);

        // ↓ / PgDn は従来通り次へ（4ページ目）
        Assert.True(MainWindow.HandlePageNavigation(mainVm, Key.Down, ModifierKeys.None));
        Assert.Equal(4, mainVm.CurrentPageNumber);

        // → で前の見開き（2ページ目）へ戻る
        Assert.True(MainWindow.HandlePageNavigation(mainVm, Key.Right, ModifierKeys.None));
        Assert.Equal(2, mainVm.CurrentPageNumber);

        // ↑ / PgUp は従来通り前へ（1ページ目）
        Assert.True(MainWindow.HandlePageNavigation(mainVm, Key.PageUp, ModifierKeys.None));
        Assert.Equal(1, mainVm.CurrentPageNumber);
    }
}
