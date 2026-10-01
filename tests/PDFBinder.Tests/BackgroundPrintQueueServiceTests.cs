using PDFBinder.App.Services;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// BackgroundPrintQueueService の単体テスト
/// </summary>
public class BackgroundPrintQueueServiceTests
{
    private class MockPrintService : IPrintService
    {
        public bool SpoolResult { get; set; } = true;
        public Exception? ExceptionToThrow { get; set; }
        public List<string> SpooledJobNames { get; } = new();
        public int SpoolDelayMs { get; set; } = 10;

        public Task<bool> SpoolDocumentAsync(
            IReadOnlyList<PrintPreparedSheet> preparedSheets,
            PrintSettings settings,
            string jobName,
            CancellationToken cancellationToken = default)
        {
            SpooledJobNames.Add(jobName);
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            if (SpoolDelayMs > 0)
            {
                Thread.Sleep(SpoolDelayMs);
            }
            return Task.FromResult(SpoolResult);
        }

        public IReadOnlyList<string> GetInstalledPrinters() => new[] { "TestPrinter" };
        public string? GetDefaultPrinterName() => "TestPrinter";
        public PrinterSettingsDialogResult? ShowPrinterSettingsDialog(string printerName, nint ownerHwnd, byte[]? currentDevMode = null) => null;

        public Task<IReadOnlyList<PrintPreparedSheet>> PrepareSheetsAsync(
            Func<int, CancellationToken, Task<System.Windows.Media.Imaging.BitmapSource?>> renderPageFunc,
            IReadOnlyList<PrintSheetLayout> sheets,
            IProgress<(int currentSheet, int totalSheets)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PrintPreparedSheet>>(Array.Empty<PrintPreparedSheet>());
        }

        public Task<bool> PrintAsync(
            Func<int, CancellationToken, Task<System.Windows.Media.Imaging.BitmapSource?>> renderPageFunc,
            PrintSettings settings,
            IReadOnlyList<PrintSheetLayout> sheets,
            string jobName = "PDFBinder",
            IProgress<(int currentSheet, int totalSheets)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Enqueue_SingleJob_ProcessesAndTriggersCompletedEvent()
    {
        var mockService = new MockPrintService();
        var queueService = new BackgroundPrintQueueService(mockService);

        var job = new PrintSpoolJob("test - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        bool completedFired = false;
        bool resultSuccess = false;

        queueService.JobCompleted += (j, success, err) =>
        {
            if (j.JobName == "test - PDFBinder")
            {
                completedFired = true;
                resultSuccess = success;
            }
        };

        queueService.Enqueue(job);
        await queueService.WaitForAllJobsAsync();

        Assert.True(completedFired);
        Assert.True(resultSuccess);
        Assert.Equal(0, queueService.ActiveJobCount);
        Assert.False(queueService.HasActiveJobs);
        Assert.Contains("test - PDFBinder", mockService.SpooledJobNames);
    }

    [Fact]
    public async Task Enqueue_MultipleJobs_ProcessesSequentially()
    {
        var mockService = new MockPrintService { SpoolDelayMs = 20 };
        var queueService = new BackgroundPrintQueueService(mockService);

        var job1 = new PrintSpoolJob("job1 - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        var job2 = new PrintSpoolJob("job2 - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());

        var completedJobs = new List<string>();
        queueService.JobCompleted += (j, success, err) =>
        {
            completedJobs.Add(j.JobName);
        };

        queueService.Enqueue(job1);
        queueService.Enqueue(job2);

        await queueService.WaitForAllJobsAsync();

        Assert.Equal(2, completedJobs.Count);
        Assert.Equal("job1 - PDFBinder", completedJobs[0]);
        Assert.Equal("job2 - PDFBinder", completedJobs[1]);
        Assert.Equal(0, queueService.ActiveJobCount);
    }

    [Fact]
    public async Task Enqueue_WhenSpoolFails_ReportsFailure()
    {
        var mockService = new MockPrintService { SpoolResult = false };
        var queueService = new BackgroundPrintQueueService(mockService);

        var job = new PrintSpoolJob("fail - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        bool successReported = true;
        string? errorReported = null;

        queueService.JobCompleted += (j, success, err) =>
        {
            successReported = success;
            errorReported = err;
        };

        queueService.Enqueue(job);
        await queueService.WaitForAllJobsAsync();

        Assert.False(successReported);
        Assert.NotNull(errorReported);
    }

    [Fact]
    public async Task Enqueue_WhenExceptionThrown_CatchesAndReportsError()
    {
        var mockService = new MockPrintService
        {
            ExceptionToThrow = new InvalidOperationException("プリンタードライバーエラー")
        };
        var queueService = new BackgroundPrintQueueService(mockService);

        var job = new PrintSpoolJob("error - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        bool successReported = true;
        string? errorReported = null;

        queueService.JobCompleted += (j, success, err) =>
        {
            successReported = success;
            errorReported = err;
        };

        queueService.Enqueue(job);
        await queueService.WaitForAllJobsAsync();

        Assert.False(successReported);
        Assert.Contains("プリンタードライバーエラー", errorReported);
    }

    [Fact]
    public async Task Enqueue_JobCompleted_ActiveJobCountIsZeroAtCallback()
    {
        var mockService = new MockPrintService();
        var queueService = new BackgroundPrintQueueService(mockService);

        var job = new PrintSpoolJob("count-test - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        int countAtJobCompleted = -1;

        queueService.JobCompleted += (j, success, err) =>
        {
            countAtJobCompleted = queueService.ActiveJobCount;
        };

        queueService.Enqueue(job);
        await queueService.WaitForAllJobsAsync();

        Assert.Equal(0, countAtJobCompleted);
        Assert.Equal(0, queueService.ActiveJobCount);
    }

    [Fact]
    public async Task Enqueue_SingleJob_FiresActiveJobCountChangedWithZero()
    {
        var mockService = new MockPrintService();
        var queueService = new BackgroundPrintQueueService(mockService);

        var job = new PrintSpoolJob("notify-zero - PDFBinder", new PrintSettings(), Array.Empty<PrintPreparedSheet>());
        var recordedCounts = new List<int>();

        queueService.ActiveJobCountChanged += count =>
        {
            recordedCounts.Add(count);
        };

        queueService.Enqueue(job);
        await queueService.WaitForAllJobsAsync();

        Assert.Contains(1, recordedCounts);
        Assert.Contains(0, recordedCounts);
        Assert.Equal(0, recordedCounts.Last());
    }
}
