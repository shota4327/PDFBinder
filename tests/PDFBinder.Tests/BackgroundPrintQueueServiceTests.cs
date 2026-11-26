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
        public List<string> StreamingJobNames { get; } = new();
        public int SimulatedTotalSheets { get; set; } = 3;
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

        public Task<IReadOnlyList<PrintPreparedSheet>> PrepareSheetsAsync(
            Func<int, int, int, CancellationToken, Task<System.Windows.Media.Imaging.BitmapSource?>> renderPageFunc,
            IReadOnlyList<PrintSheetLayout> sheets,
            IReadOnlyList<(double width, double height)>? pageDimensions = null,
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

        public Task<bool> PrintStreamingAsync(
            IReadOnlyList<PrintSheetLayout> sheets,
            IReadOnlyList<PrintPageSource> pageSources,
            PrintSettings settings,
            string jobName,
            IProgress<(int currentSheet, int totalSheets)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            StreamingJobNames.Add(jobName);
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            for (int i = 1; i <= SimulatedTotalSheets; i++)
            {
                progress?.Report((i, SimulatedTotalSheets));
            }
            if (SpoolDelayMs > 0)
            {
                Thread.Sleep(SpoolDelayMs);
            }
            return Task.FromResult(SpoolResult);
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

    [Fact]
    public async Task Enqueue_StreamingJob_CallsPrintStreamingAndReportsSheetProgress()
    {
        var mockService = new MockPrintService { SimulatedTotalSheets = 3 };
        var queueService = new BackgroundPrintQueueService(mockService);

        var sheets = new List<PrintSheetLayout> { new(1, PrintOrientation.Portrait, PrintPaperSize.A4) };
        var pageSources = new List<PrintPageSource>();
        var job = new PrintSpoolJob("stream - PDFBinder", new PrintSettings(), sheets, pageSources);

        var progressReports = new List<(int current, int total)>();
        queueService.JobProgressChanged += (j, current, total) =>
        {
            if (j.JobName == "stream - PDFBinder")
            {
                progressReports.Add((current, total));
            }
        };

        queueService.Enqueue(job);
        await queueService.WaitForAllJobsAsync();

        Assert.Contains("stream - PDFBinder", mockService.StreamingJobNames);
        Assert.Empty(mockService.SpooledJobNames);
        Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, progressReports);
    }
}
