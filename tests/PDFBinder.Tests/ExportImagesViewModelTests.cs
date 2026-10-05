using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// テスト用の偽画像書き出しサービス
/// </summary>
public class FakeImageExportService : IImageExportService
{
    public List<(BitmapSource Bitmap, string FilePath, ImageExportFormat Format, int Dpi, int Quality)> SavedImages { get; } = new();

    public (int Width, int Height) CalculatePixelSize(double pointWidth, double pointHeight, int dpi)
    {
        return ((int)pointWidth, (int)pointHeight);
    }

    public Task SaveImageAsync(BitmapSource bitmap, string filePath, ImageExportFormat format, int dpi, int quality = 90)
    {
        SavedImages.Add((bitmap, filePath, format, dpi, quality));
        return Task.CompletedTask;
    }
}

/// <summary>
/// 画像書き出しダイアログ用 ViewModel（ExportImagesViewModel）の単体テスト
/// </summary>
public class ExportImagesViewModelTests
{
    private readonly FakeImageExportService _fakeService = new();

    private static BitmapSource CreateDummyBitmap()
    {
        var bitmap = BitmapSource.Create(10, 10, 96, 96, PixelFormats.Bgra32, null, new byte[400], 40);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void Constructor_InitializesWithAllPagesDefault()
    {
        var settings = new ImageExportSettings();
        var vm = new ExportImagesViewModel(
            _fakeService,
            settings,
            totalPages: 5,
            currentPageIndex: 1,
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            idx => (595, 842),
            "Sample.pdf");

        Assert.Equal(5, vm.TotalExportPages);
        Assert.Equal(0, vm.CurrentExportPageIndex);
        Assert.Equal("1 / 5", vm.DisplayExportPageText);
        Assert.True(vm.CanExport);
        Assert.Null(vm.RangeErrorMessage);
    }

    [Fact]
    public void CurrentPageSelection_UpdatesTargetPageCountToOne()
    {
        var settings = new ImageExportSettings();
        var vm = new ExportImagesViewModel(
            _fakeService,
            settings,
            totalPages: 5,
            currentPageIndex: 2,
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            idx => (595, 842),
            "Sample.pdf");

        settings.RangeType = ImageExportRangeType.CurrentPage;

        Assert.Equal(1, vm.TotalExportPages);
        Assert.Equal(0, vm.CurrentExportPageIndex);
        Assert.Equal("1 / 1", vm.DisplayExportPageText);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public void CustomRange_InvalidInput_DisablesExportAndSetsErrorMessage()
    {
        var settings = new ImageExportSettings();
        var vm = new ExportImagesViewModel(
            _fakeService,
            settings,
            totalPages: 5,
            currentPageIndex: 0,
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            idx => (595, 842),
            "Sample.pdf");

        settings.RangeType = ImageExportRangeType.Custom;
        settings.CustomRangeText = "1-10"; // 5ページ中10は範囲外

        Assert.False(vm.CanExport);
        Assert.NotNull(vm.RangeErrorMessage);
        Assert.Equal(0, vm.TotalExportPages);
    }

    [Fact]
    public void CustomRange_ValidInput_UpdatesPagesCorrectly()
    {
        var settings = new ImageExportSettings();
        var vm = new ExportImagesViewModel(
            _fakeService,
            settings,
            totalPages: 10,
            currentPageIndex: 0,
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            idx => (595, 842),
            "Sample.pdf");

        settings.RangeType = ImageExportRangeType.Custom;
        settings.CustomRangeText = "1-3, 5";

        Assert.True(vm.CanExport);
        Assert.Null(vm.RangeErrorMessage);
        Assert.Equal(4, vm.TotalExportPages);
        Assert.Equal("1 / 4", vm.DisplayExportPageText);
    }

    [Fact]
    public void Navigation_PreviousAndNext_ChangesPageIndex()
    {
        var settings = new ImageExportSettings();
        var vm = new ExportImagesViewModel(
            _fakeService,
            settings,
            totalPages: 3,
            currentPageIndex: 0,
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            idx => (595, 842),
            "Sample.pdf");

        Assert.False(vm.CanGoToPreviousPage);
        Assert.True(vm.CanGoToNextPage);

        vm.NextPageCommand.Execute(null);
        Assert.Equal(1, vm.CurrentExportPageIndex);
        Assert.Equal("2 / 3", vm.DisplayExportPageText);
        Assert.True(vm.CanGoToPreviousPage);

        vm.PreviousPageCommand.Execute(null);
        Assert.Equal(0, vm.CurrentExportPageIndex);
        Assert.Equal("1 / 3", vm.DisplayExportPageText);
    }

    [Fact]
    public async Task ExecuteExportAsync_SinglePage_CallsSaveFileDialogAndSaves()
    {
        var settings = new ImageExportSettings { RangeType = ImageExportRangeType.CurrentPage };
        var vm = new ExportImagesViewModel(
            _fakeService,
            settings,
            totalPages: 5,
            currentPageIndex: 0,
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            idx => (595, 842),
            "Document.pdf");

        string chosenFile = @"C:\Test\output.png";
        vm.PickSaveFileFunc = (defaultName, filter, title) => chosenFile;

        bool closeCalled = false;
        int exportedCount = 0;
        string? resultPath = null;
        vm.RequestClose += (success, count, path) =>
        {
            closeCalled = true;
            exportedCount = count;
            resultPath = path;
        };

        await vm.ExecuteExportAsync();

        Assert.True(closeCalled);
        Assert.Equal(1, exportedCount);
        Assert.Equal(chosenFile, resultPath);
        Assert.Single(_fakeService.SavedImages);
        Assert.Equal(chosenFile, _fakeService.SavedImages[0].FilePath);
        Assert.Equal(ImageExportFormat.Png, _fakeService.SavedImages[0].Format);
        Assert.Equal((int)ImageExportDpi.Dpi400, _fakeService.SavedImages[0].Dpi);
    }

    [Fact]
    public async Task ExecuteExportAsync_MultiplePages_CallsFolderDialogAndSavesAll()
    {
        var settings = new ImageExportSettings { RangeType = ImageExportRangeType.AllPages };
        var vm = new ExportImagesViewModel(
            _fakeService,
            settings,
            totalPages: 3,
            currentPageIndex: 0,
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(CreateDummyBitmap()),
            idx => (595, 842),
            "Report.pdf");

        string chosenFolder = @"C:\ExportFolder";
        vm.PickFolderFunc = title => chosenFolder;

        bool closeCalled = false;
        int exportedCount = 0;
        vm.RequestClose += (success, count, path) =>
        {
            closeCalled = true;
            exportedCount = count;
        };

        await vm.ExecuteExportAsync();

        Assert.True(closeCalled);
        Assert.Equal(3, exportedCount);
        Assert.Equal(3, _fakeService.SavedImages.Count);
        Assert.All(_fakeService.SavedImages, item => Assert.StartsWith(chosenFolder, item.FilePath));
    }
}
