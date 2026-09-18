using System.Windows.Media.Imaging;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// テスト用の偽印刷サービス
/// </summary>
public class FakePrintService : IPrintService
{
    public IReadOnlyList<string> InstalledPrinters { get; set; } = new List<string> { "Printer A", "Printer B" };
    public string? DefaultPrinter { get; set; } = "Printer A";
    public bool PrintResult { get; set; } = true;
    public PrinterSettingsDialogResult? DialogResultToReturn { get; set; }
    public int ShowDialogCallCount { get; private set; }
    public string? LastPrinterNamePassed { get; private set; }

    public IReadOnlyList<string> GetInstalledPrinters() => InstalledPrinters;
    public string? GetDefaultPrinterName() => DefaultPrinter;

    public PrinterSettingsDialogResult? ShowPrinterSettingsDialog(
        string printerName,
        nint ownerHwnd,
        byte[]? currentDevMode = null)
    {
        ShowDialogCallCount++;
        LastPrinterNamePassed = printerName;
        return DialogResultToReturn;
    }

    public Task<bool> PrintAsync(
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        PrintSettings settings,
        IReadOnlyList<PrintSheetLayout> sheets,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PrintResult);
    }
}

/// <summary>
/// PrintViewModel の単体テスト
/// </summary>
public class PrintViewModelTests
{
    private readonly FakePrintService _fakePrintService;

    public PrintViewModelTests()
    {
        _fakePrintService = new FakePrintService();
    }

    private PrintViewModel CreateViewModel(int totalPages = 5, int currentPage = 0, PrintSettings? settings = null)
    {
        var printSettings = settings ?? new PrintSettings();
        return new PrintViewModel(
            _fakePrintService,
            printSettings,
            totalPages,
            currentPage,
            (idx, w, h, ct) => Task.FromResult<BitmapSource?>(null),
            (idx, ct) => Task.FromResult<BitmapSource?>(null));
    }

    [Fact]
    public void Constructor_InitializesPrintersAndDefaultSelection()
    {
        var vm = CreateViewModel();

        Assert.Equal(2, vm.Printers.Count);
        Assert.Equal("Printer A", vm.Settings.PrinterName);
        Assert.True(vm.CanPrint);
        Assert.Equal(5, vm.TotalSheets);
    }

    [Fact]
    public void SettingLayoutModeToBooklet_AutoSetsLandscapeAndShortEdgeDuplex()
    {
        var vm = CreateViewModel();
        vm.Settings.Orientation = PrintOrientation.Portrait;
        vm.Settings.DuplexMode = PrintDuplexMode.OneSided;

        vm.Settings.LayoutMode = PrintLayoutMode.Booklet;

        Assert.Equal(PrintOrientation.Landscape, vm.Settings.Orientation);
        Assert.Equal(PrintDuplexMode.TwoSidedShortEdge, vm.Settings.DuplexMode);
    }

    [Fact]
    public void CustomRange_InvalidInput_DisablesPrintingAndSetsError()
    {
        var vm = CreateViewModel(totalPages: 5);

        vm.Settings.RangeType = PrintRangeType.Custom;
        vm.Settings.CustomRangeText = "1-10"; // 5ページなので範囲外

        Assert.False(vm.CanPrint);
        Assert.NotNull(vm.RangeErrorMessage);
        Assert.Equal(0, vm.TotalSheets);
    }

    [Fact]
    public void CustomRange_ValidInput_UpdatesSheetsAndAllowsPrint()
    {
        var vm = CreateViewModel(totalPages: 5);

        vm.Settings.RangeType = PrintRangeType.Custom;
        vm.Settings.CustomRangeText = "1-3, 5";

        Assert.True(vm.CanPrint);
        Assert.Null(vm.RangeErrorMessage);
        Assert.Equal(4, vm.TotalSheets);
    }

    [Fact]
    public void SheetNavigation_NextAndPrevious_UpdatesCurrentIndex()
    {
        var vm = CreateViewModel(totalPages: 3);

        Assert.Equal(0, vm.CurrentSheetIndex);

        vm.NextSheetCommand.Execute(null);
        Assert.Equal(1, vm.CurrentSheetIndex);

        vm.NextSheetCommand.Execute(null);
        Assert.Equal(2, vm.CurrentSheetIndex);

        // 上限超え防止
        vm.NextSheetCommand.Execute(null);
        Assert.Equal(2, vm.CurrentSheetIndex);

        vm.PreviousSheetCommand.Execute(null);
        Assert.Equal(1, vm.CurrentSheetIndex);
    }

    [Fact]
    public void AvailableNUpOptions_ContainsStandardOptions()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm.AvailableNUpOptions);
        Assert.Equal(3, vm.AvailableNUpOptions.Count);
        Assert.Equal(NUpPagesPerSheet.Two, vm.AvailableNUpOptions[0].Count);
        Assert.Equal("2 ページ", vm.AvailableNUpOptions[0].DisplayName);
        Assert.Equal(NUpPagesPerSheet.Four, vm.AvailableNUpOptions[1].Count);
        Assert.Equal(NUpPagesPerSheet.Eight, vm.AvailableNUpOptions[2].Count);
    }

    [Fact]
    public void CanOpenPrinterSettings_ReflectsPrinterNameAndPrintingState()
    {
        var vm = CreateViewModel();

        // プリンター選択済み初期状態
        Assert.True(vm.CanOpenPrinterSettings);
        Assert.True(vm.OpenPrinterSettingsCommand.CanExecute(null));

        // プリンター名が空の場合
        vm.Settings.PrinterName = string.Empty;
        Assert.False(vm.CanOpenPrinterSettings);
        Assert.False(vm.OpenPrinterSettingsCommand.CanExecute(null));

        // プリンター名再設定
        vm.Settings.PrinterName = "Printer B";
        Assert.True(vm.CanOpenPrinterSettings);

        // 印刷中の場合
        vm.IsPrinting = true;
        Assert.False(vm.CanOpenPrinterSettings);
        Assert.False(vm.OpenPrinterSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void OpenPrinterSettings_WhenConfirmed_AppliesResultAndUpdatesPreview()
    {
        var vm = CreateViewModel();
        var dummyDevMode = new byte[] { 1, 2, 3, 4 };
        _fakePrintService.DialogResultToReturn = new PrinterSettingsDialogResult(
            dummyDevMode,
            PrintPaperSize.A3,
            PrintOrientation.Landscape,
            3,
            PrintDuplexMode.TwoSidedLongEdge);

        vm.OpenPrinterSettingsCommand.Execute(null);

        Assert.Equal(1, _fakePrintService.ShowDialogCallCount);
        Assert.Equal("Printer A", _fakePrintService.LastPrinterNamePassed);
        Assert.Equal(PrintPaperSize.A3, vm.Settings.PaperSize);
        Assert.Equal(PrintOrientation.Landscape, vm.Settings.Orientation);
        Assert.Equal(3, vm.Settings.Copies);
        Assert.Equal(PrintDuplexMode.TwoSidedLongEdge, vm.Settings.DuplexMode);
        Assert.Equal(dummyDevMode, vm.Settings.DriverDevMode);
    }

    [Fact]
    public void OpenPrinterSettings_WhenCancelled_LeavesSettingsIntact()
    {
        var vm = CreateViewModel();
        vm.Settings.PaperSize = PrintPaperSize.A4;
        vm.Settings.Orientation = PrintOrientation.Portrait;
        vm.Settings.Copies = 1;
        _fakePrintService.DialogResultToReturn = null; // キャンセル

        vm.OpenPrinterSettingsCommand.Execute(null);

        Assert.Equal(1, _fakePrintService.ShowDialogCallCount);
        Assert.Equal(PrintPaperSize.A4, vm.Settings.PaperSize);
        Assert.Equal(PrintOrientation.Portrait, vm.Settings.Orientation);
        Assert.Equal(1, vm.Settings.Copies);
        Assert.Null(vm.Settings.DriverDevMode);
    }

    [Fact]
    public void PrinterChange_ClearsDriverDevMode()
    {
        var vm = CreateViewModel();
        vm.Settings.DriverDevMode = new byte[] { 10, 20, 30 };

        // 異なるプリンターに切り替え
        vm.Settings.PrinterName = "Printer B";

        Assert.Null(vm.Settings.DriverDevMode);
    }
}
