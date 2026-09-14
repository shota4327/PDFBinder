using System.Windows.Media.Imaging;
using PDFBinder.App.Converters;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// ページ移動機能および表示オプション（フィットモード）の単体テスト
/// </summary>
public class PageNavigationAndViewOptionsTests
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
            return Task.FromResult<BitmapSource?>(bitmap);
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

    private static PdfDocumentModel CreateSampleDocument(int pageCount)
    {
        var doc = new PdfDocumentModel { FilePath = "sample.pdf" };
        for (int i = 0; i < pageCount; i++)
        {
            doc.Pages.Add(new PdfPageModel
            {
                PageNumber = i + 1,
                OriginalPageIndex = i,
                Width = 600,
                Height = 800
            });
        }
        return doc;
    }

    [Fact]
    public void InitialFitMode_ShouldBeFitToWindow()
    {
        // Arrange & Act
        var doc = CreateSampleDocument(3);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);

        // Assert: 初期状態のフィットモードはFitToWindow
        Assert.Equal(DetailViewFitMode.FitToWindow, vm.FitMode);
    }

    [Fact]
    public void SetFitModeCommand_ShouldUpdateFitModeAndRecalculate()
    {
        // Arrange
        var doc = CreateSampleDocument(3);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);
        vm.UpdateViewportSize(660, 860);

        // Act & Assert: 幅に合わせる
        vm.SetFitModeCommand.Execute(DetailViewFitMode.FitToWidth);
        Assert.Equal(DetailViewFitMode.FitToWidth, vm.FitMode);
        Assert.Equal(1.0, vm.Zoom, precision: 2); // availableWidth = 660 - 60 = 600, 600/600 = 1.0

        // Act & Assert: 100%（原寸）
        vm.SetFitModeCommand.Execute(DetailViewFitMode.ActualSize);
        Assert.Equal(DetailViewFitMode.ActualSize, vm.FitMode);
        Assert.Equal(1.0, vm.Zoom);
    }

    [Fact]
    public void FitModePropertySetter_ShouldImmediatelyRecalculateZoom()
    {
        // Arrange
        var doc = CreateSampleDocument(3);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);
        vm.UpdateViewportSize(1260, 800); // availableWidth = 1260 - 60 - 2 = 1198, 縦スクロール発生のため 1198 - 18 = 1180

        // Act: ラジオボタンのTwoWayバインディングと同様にプロパティを直接設定
        vm.FitMode = DetailViewFitMode.FitToWidth;

        // Assert: 縦スクロールバー幅とセーフティバッファを控除した幅（1180 / 600 = 1.967 ≒ 1.97）に再計算される
        Assert.Equal(1.97, vm.Zoom, precision: 2);

        // Act: 等倍に切り替え
        vm.FitMode = DetailViewFitMode.ActualSize;

        // Assert: 即座に 1.0 に再計算される
        Assert.Equal(1.0, vm.Zoom);
    }

    [Fact]
    public void InitializeDocument_ShouldApplyFitModeImmediately()
    {
        // Arrange: 初期ドキュメント（幅600）
        var doc = CreateSampleDocument(1);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);
        vm.UpdateViewportSize(660, 860); // availableWidth = 600
        vm.FitMode = DetailViewFitMode.FitToWidth;
        Assert.Equal(1.0, vm.Zoom, precision: 2);

        // Act: 異なるページサイズ（幅1200）の別ドキュメントを読み込み
        var doc2 = new PdfDocumentModel { FilePath = "sample2.pdf" };
        doc2.Pages.Add(new PdfPageModel { PageNumber = 1, Width = 1200, Height = 1600 });
        vm.InitializeDocument(doc2);

        // Assert: 読み込み直後に即座に幅に合わせて縮小（600 / 1200 = 0.5）される
        Assert.Equal(0.5, vm.Zoom, precision: 2);
    }

    [Fact]
    public void ManualZoom_ShouldSwitchFitModeToNone()
    {
        // Arrange
        var doc = CreateSampleDocument(3);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);
        Assert.Equal(DetailViewFitMode.FitToWindow, vm.FitMode);

        // Act: ズームイン実行
        vm.ZoomInCommand.Execute(null);

        // Assert: フィットモードがNone（手動固定倍率）へ解除される
        Assert.Equal(DetailViewFitMode.None, vm.FitMode);

        // Act: 再びフィットモードを設定した後にズームアウト
        vm.SetFitMode(DetailViewFitMode.FitToWidth);
        vm.ZoomOutCommand.Execute(null);

        // Assert: ズームアウトでもNoneへ解除される
        Assert.Equal(DetailViewFitMode.None, vm.FitMode);
    }

    [Fact]
    public void ZoomReset_ShouldSetActualSizeFitMode()
    {
        // Arrange
        var doc = CreateSampleDocument(3);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(DetailViewFitMode.None, vm.FitMode);

        // Act
        vm.ZoomResetCommand.Execute(null);

        // Assert
        Assert.Equal(DetailViewFitMode.ActualSize, vm.FitMode);
        Assert.Equal(1.0, vm.Zoom);
    }

    [Fact]
    public void UpdateViewportSize_ShouldDynamicallyRecalculateWhenFitModeActive()
    {
        // Arrange
        var doc = CreateSampleDocument(3);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);
        vm.SetFitMode(DetailViewFitMode.FitToWidth);

        // Act: 横幅が拡大された場合 (availableWidth: 1260 - 60 - 2 = 1198, 縦スクロール発生のため 1180)
        vm.UpdateViewportSize(1260, 800);

        // Assert: 1180 / 600 = 1.967 ≒ 1.97
        Assert.Equal(1.97, vm.Zoom, precision: 2);

        // Act: 横幅が縮小された場合 (availableWidth: 360 - 60 = 300)
        vm.UpdateViewportSize(360, 800);

        // Assert: 300 / 600 = 0.5
        Assert.Equal(0.5, vm.Zoom, precision: 2);
    }

    [Fact]
    public void PageNavigation_CommandsAndCanExecute_ShouldWorkCorrectly()
    {
        // Arrange
        var doc = CreateSampleDocument(3);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);

        // Assert: 先頭ページ（Page 1）
        Assert.Equal(1, vm.CurrentPageNumber);
        Assert.False(vm.CanGoToPreviousPage);
        Assert.True(vm.CanGoToNextPage);

        // Act: 次のページへ
        vm.GoToNextPageCommand.Execute(null);

        // Assert: 2ページ目
        Assert.Equal(2, vm.CurrentPageNumber);
        Assert.True(vm.CanGoToPreviousPage);
        Assert.True(vm.CanGoToNextPage);

        // Act: 3ページ目へ
        vm.GoToNextPageCommand.Execute(null);

        // Assert: 3ページ目（末尾）
        Assert.Equal(3, vm.CurrentPageNumber);
        Assert.True(vm.CanGoToPreviousPage);
        Assert.False(vm.CanGoToNextPage);

        // Act: 前のページへ戻る
        vm.GoToPreviousPageCommand.Execute(null);

        // Assert: 2ページ目へ復帰
        Assert.Equal(2, vm.CurrentPageNumber);
    }

    [Fact]
    public void CurrentPageNumber_Setter_ShouldNavigateToSpecifiedPage()
    {
        // Arrange
        var doc = CreateSampleDocument(5);
        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), doc);

        // Act: 4ページ目へ直接ジャンプ
        vm.CurrentPageNumber = 4;

        // Assert
        Assert.Equal(4, vm.CurrentPageNumber);
        Assert.Equal(4, vm.CurrentPage?.PageNumber);

        // Act: 範囲外の数値を設定した場合は無視される
        vm.CurrentPageNumber = 99;
        Assert.Equal(4, vm.CurrentPageNumber);

        vm.CurrentPageNumber = 0;
        Assert.Equal(4, vm.CurrentPageNumber);
    }

    [Fact]
    public void MainViewModel_PageNavigationAndFitMode_Integration()
    {
        // Arrange
        var mainVm = new MainViewModel();
        var doc = CreateSampleDocument(3);
        mainVm.Document = doc;

        // Assert: 詳細ビュー有効時の初期状態
        Assert.True(mainVm.IsDetailViewActive);
        Assert.True(mainVm.CanNavigatePages);
        Assert.False(mainVm.CanGoToPreviousPage);
        Assert.True(mainVm.CanGoToNextPage);
        Assert.Equal(1, mainVm.CurrentPageNumber);

        // Act: 次ページ移動
        mainVm.GoToNextPageCommand.Execute(null);
        Assert.Equal(2, mainVm.CurrentPageNumber);
        Assert.True(mainVm.CanGoToPreviousPage);

        // Act: グリッドビューに切り替えた場合はページ移動が無効化される
        mainVm.IsDetailViewActive = false;
        Assert.False(mainVm.CanNavigatePages);
        Assert.False(mainVm.CanGoToPreviousPage);
        Assert.False(mainVm.CanGoToNextPage);

        // Act: 詳細ビューに戻した場合は再び有効化される
        mainVm.IsDetailViewActive = true;
        Assert.True(mainVm.CanNavigatePages);
        Assert.True(mainVm.CanGoToPreviousPage);
        Assert.True(mainVm.CanGoToNextPage);
    }

    [Fact]
    public void EqualityToBooleanConverter_ShouldHandleDetailViewFitModeEnum()
    {
        // Arrange
        var converter = new EqualityToBooleanConverter();

        // Convert
        var isFitToWindow = converter.Convert(DetailViewFitMode.FitToWindow, typeof(bool), "FitToWindow", System.Globalization.CultureInfo.InvariantCulture);
        var isActualSize = converter.Convert(DetailViewFitMode.FitToWindow, typeof(bool), "ActualSize", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(true, isFitToWindow);
        Assert.Equal(false, isActualSize);

        // ConvertBack
        var convertedBack = converter.ConvertBack(true, typeof(DetailViewFitMode), "FitToWidth", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(DetailViewFitMode.FitToWidth, convertedBack);
    }
}
