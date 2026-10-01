using PDFBinder.App.Services;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// FakeBackgroundPrintQueueService: 単体テスト用の印刷キューサービス
/// </summary>
public class FakeBackgroundPrintQueueService : IBackgroundPrintQueueService
{
    public int ActiveJobCount { get; set; } = 0;
    public bool HasActiveJobs => ActiveJobCount > 0;

    public event Action<int>? ActiveJobCountChanged;
    public event Action<PrintSpoolJob>? JobStarted;
    public event Action<PrintSpoolJob, bool, string?>? JobCompleted;

    public List<PrintSpoolJob> EnqueuedJobs { get; } = new();

    public void Enqueue(PrintSpoolJob job)
    {
        EnqueuedJobs.Add(job);
        ActiveJobCount++;
        JobStarted?.Invoke(job);
        ActiveJobCountChanged?.Invoke(ActiveJobCount);
    }

    public void SimulateJobCompleted(PrintSpoolJob job, bool success, string? errorMessage)
    {
        if (ActiveJobCount > 0)
        {
            ActiveJobCount--;
        }
        JobCompleted?.Invoke(job, success, errorMessage);
        ActiveJobCountChanged?.Invoke(ActiveJobCount);
    }

    public Task WaitForAllJobsAsync() => Task.CompletedTask;
}

/// <summary>
/// MainViewModel の印刷ジョブ制御、終了待機ダイアログ、キャンセル、エラー通知の単体テスト
/// </summary>
public class MainViewModelPrintExitTests
{
    private static MainViewModel CreateViewModel(FakeBackgroundPrintQueueService fakeQueue)
    {
        var vm = new MainViewModel(printQueueService: fakeQueue);
        vm.UiDispatcherOverride = action => action();
        return vm;
    }

    [Fact]
    public void InitialState_HasNoActivePrintTasksAndDialogClosed()
    {
        var fakeQueue = new FakeBackgroundPrintQueueService();
        var vm = CreateViewModel(fakeQueue);

        Assert.False(vm.HasActivePrintTasks);
        Assert.False(vm.IsPrintWaitDialogVisible);
    }

    [Fact]
    public void ShowPrintWaitDialog_SetsVisibilityAndStatusText()
    {
        var fakeQueue = new FakeBackgroundPrintQueueService { ActiveJobCount = 2 };
        var vm = CreateViewModel(fakeQueue);

        Assert.True(vm.HasActivePrintTasks);
        vm.ShowPrintWaitDialog();

        Assert.True(vm.IsPrintWaitDialogVisible);
        Assert.Contains("2 件", vm.PrintWaitTaskStatusText);
    }

    [Fact]
    public void CancelPrintWaitCommand_ClosesWaitDialog()
    {
        var fakeQueue = new FakeBackgroundPrintQueueService { ActiveJobCount = 1 };
        var vm = CreateViewModel(fakeQueue);

        vm.ShowPrintWaitDialog();
        Assert.True(vm.IsPrintWaitDialogVisible);

        vm.CancelPrintWaitCommand.Execute(null);
        Assert.False(vm.IsPrintWaitDialogVisible);
    }

    [Fact]
    public void PrintJobCompleted_WhenWaitDialogVisible_TriggersRequestCloseWindow()
    {
        var fakeQueue = new FakeBackgroundPrintQueueService();
        var vm = CreateViewModel(fakeQueue);

        bool closeRequested = false;
        vm.RequestCloseWindow = () => closeRequested = true;

        var job = new PrintSpoolJob("test - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        fakeQueue.Enqueue(job);

        vm.ShowPrintWaitDialog();
        Assert.True(vm.IsPrintWaitDialogVisible);

        // ジョブ完了シミュレーション
        fakeQueue.SimulateJobCompleted(job, true, null);

        Assert.True(closeRequested);
        Assert.False(vm.IsPrintWaitDialogVisible);
    }

    [Fact]
    public void PrintJobCompleted_WhenFailed_ShowsErrorDialog()
    {
        var fakeQueue = new FakeBackgroundPrintQueueService();
        var vm = CreateViewModel(fakeQueue);

        string? promptTitle = null;
        string? promptMessage = null;
        vm.ShowErrorPrompt = (title, msg) =>
        {
            promptTitle = title;
            promptMessage = msg;
        };

        var job = new PrintSpoolJob("fail - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        fakeQueue.Enqueue(job);

        fakeQueue.SimulateJobCompleted(job, false, "通信エラーが発生しました");

        Assert.True(vm.IsErrorDialogVisible);
        Assert.Equal("印刷エラー", vm.ErrorDialogTitle);
        Assert.Contains("通信エラーが発生しました", vm.ErrorDialogMessage);
        Assert.Equal("印刷エラー", promptTitle);
        Assert.Contains("通信エラーが発生しました", promptMessage);
    }

    private class ControllablePrintService : IPrintService
    {
        public TaskCompletionSource<bool> SpoolTcs { get; } = new();

        public Task<bool> SpoolDocumentAsync(
            IReadOnlyList<PrintPreparedSheet> preparedSheets,
            PrintSettings settings,
            string jobName,
            CancellationToken cancellationToken = default)
        {
            return SpoolTcs.Task;
        }

        public IReadOnlyList<string> GetInstalledPrinters() => Array.Empty<string>();
        public string? GetDefaultPrinterName() => null;
        public PrinterSettingsDialogResult? ShowPrinterSettingsDialog(string printerName, nint ownerHwnd, byte[]? currentDevMode = null) => null;
        public Task<IReadOnlyList<PrintPreparedSheet>> PrepareSheetsAsync(
            Func<int, CancellationToken, Task<System.Windows.Media.Imaging.BitmapSource?>> renderPageFunc,
            IReadOnlyList<PrintSheetLayout> sheets,
            IProgress<(int currentSheet, int totalSheets)>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrintPreparedSheet>>(Array.Empty<PrintPreparedSheet>());
        public Task<bool> PrintAsync(
            Func<int, CancellationToken, Task<System.Windows.Media.Imaging.BitmapSource?>> renderPageFunc,
            PrintSettings settings,
            IReadOnlyList<PrintSheetLayout> sheets,
            string jobName = "PDFBinder",
            IProgress<(int currentSheet, int totalSheets)>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    [Fact]
    public async Task PrintJob_WithRealBackgroundPrintQueueService_ClosesWaitDialogAndRequestsCloseWindowOnCompletion()
    {
        var printService = new ControllablePrintService();
        var realQueue = new BackgroundPrintQueueService(printService);
        var vm = new MainViewModel(printQueueService: realQueue);
        vm.UiDispatcherOverride = action => action();

        bool closeRequested = false;
        vm.RequestCloseWindow = () => closeRequested = true;

        var job = new PrintSpoolJob("real-queue-test - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        realQueue.Enqueue(job);

        // 印刷処理中にアプリ終了が試行され待機ダイアログが表示された状態をシミュレート
        vm.ShowPrintWaitDialog();
        Assert.True(vm.IsPrintWaitDialogVisible);

        // バックグラウンド印刷スプールの完了をシグナル
        printService.SpoolTcs.SetResult(true);
        await realQueue.WaitForAllJobsAsync();

        Assert.True(closeRequested);
        Assert.False(vm.IsPrintWaitDialogVisible);
        Assert.Equal("印刷データを送信しました", vm.StatusMessage);
    }
}
