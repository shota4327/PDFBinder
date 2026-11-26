using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.Services;

/// <summary>
/// バックグラウンドで順次実行する印刷スプールジョブのデータ
/// </summary>
public record PrintSpoolJob
{
    /// <summary>印刷ジョブ名</summary>
    public string JobName { get; }

    /// <summary>印刷設定</summary>
    public PrintSettings Settings { get; }

    /// <summary>レンダリング済みシート一覧（従来方式用）</summary>
    public IReadOnlyList<PrintPreparedSheet> PreparedSheets { get; }

    /// <summary>面付けシート一覧（ストリーミング方式用）</summary>
    public IReadOnlyList<PrintSheetLayout>? Sheets { get; }

    /// <summary>印刷時点のページスナップショット一覧（ストリーミング方式用）</summary>
    public IReadOnlyList<PrintPageSource>? PageSources { get; }

    /// <summary>ストリーミング印刷ジョブかどうか</summary>
    public bool IsStreamingJob => Sheets != null && PageSources != null;

    /// <summary>コンストラクタ（従来方式用）</summary>
    public PrintSpoolJob(
        string jobName,
        PrintSettings settings,
        IReadOnlyList<PrintPreparedSheet> preparedSheets)
    {
        JobName = jobName;
        Settings = settings;
        PreparedSheets = preparedSheets;
    }

    /// <summary>コンストラクタ（ストリーミング方式用）</summary>
    public PrintSpoolJob(
        string jobName,
        PrintSettings settings,
        IReadOnlyList<PrintSheetLayout> sheets,
        IReadOnlyList<PrintPageSource> pageSources)
    {
        JobName = jobName;
        Settings = settings;
        PreparedSheets = Array.Empty<PrintPreparedSheet>();
        Sheets = sheets;
        PageSources = pageSources;
    }
}

/// <summary>
/// バックグラウンド印刷スプールキューを管理するサービスインターフェース
/// </summary>
public interface IBackgroundPrintQueueService
{
    /// <summary>現在処理中および待機中のジョブ数</summary>
    int ActiveJobCount { get; }

    /// <summary>実行中または待機中の印刷ジョブが存在するかどうか</summary>
    bool HasActiveJobs { get; }

    /// <summary>アクティブなジョブ数が変更されたときのイベント（現在の件数を通知）</summary>
    event Action<int>? ActiveJobCountChanged;

    /// <summary>ジョブの処理が開始されたときのイベント</summary>
    event Action<PrintSpoolJob>? JobStarted;

    /// <summary>ジョブのシート送信進捗が更新されたときのイベント (job, currentSheet, totalSheets)</summary>
    event Action<PrintSpoolJob, int, int>? JobProgressChanged
    {
        add { }
        remove { }
    }

    /// <summary>ジョブの処理が完了したときのイベント (job, isSuccess, errorMessage)</summary>
    event Action<PrintSpoolJob, bool, string?>? JobCompleted;

    /// <summary>印刷ジョブをキューに追加し、順次処理を開始します。</summary>
    void Enqueue(PrintSpoolJob job);

    /// <summary>現在キューに入っているすべての印刷ジョブの完了を待機します。</summary>
    Task WaitForAllJobsAsync();
}

/// <summary>
/// バックグラウンド印刷スプールキューを管理するサービス実装クラス
/// </summary>
public class BackgroundPrintQueueService : IBackgroundPrintQueueService
{
    private readonly IPrintService _printService;
    private readonly Queue<PrintSpoolJob> _queue = new();
    private readonly object _lock = new();
    private bool _isProcessing;
    private PrintSpoolJob? _currentJob;
    private TaskCompletionSource<bool>? _allCompletedTcs;

    /// <inheritdoc/>
    public int ActiveJobCount
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count + (_currentJob != null ? 1 : 0);
            }
        }
    }

    /// <inheritdoc/>
    public bool HasActiveJobs => ActiveJobCount > 0;

    /// <inheritdoc/>
    public event Action<int>? ActiveJobCountChanged;

    /// <inheritdoc/>
    public event Action<PrintSpoolJob>? JobStarted;

    /// <inheritdoc/>
    public event Action<PrintSpoolJob, int, int>? JobProgressChanged;

    /// <inheritdoc/>
    public event Action<PrintSpoolJob, bool, string?>? JobCompleted;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public BackgroundPrintQueueService(IPrintService printService)
    {
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
    }

    /// <inheritdoc/>
    public void Enqueue(PrintSpoolJob job)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));

        int count;
        lock (_lock)
        {
            _queue.Enqueue(job);
            count = _queue.Count + (_currentJob != null ? 1 : 0);
        }

        ActiveJobCountChanged?.Invoke(count);
        StartProcessingLoop();
    }

    /// <inheritdoc/>
    public Task WaitForAllJobsAsync()
    {
        lock (_lock)
        {
            if (!HasActiveJobs)
            {
                return Task.CompletedTask;
            }

            _allCompletedTcs ??= new TaskCompletionSource<bool>();
            return _allCompletedTcs.Task;
        }
    }

    /// <summary>
    /// キューの順次処理ループを開始します（多重起動防止）。
    /// </summary>
    private void StartProcessingLoop()
    {
        lock (_lock)
        {
            if (_isProcessing) return;
            _isProcessing = true;
        }

        _ = ProcessQueueLoopAsync();
    }

    /// <summary>
    /// キュー内の印刷ジョブを順次スプール送信します。
    /// </summary>
    private async Task ProcessQueueLoopAsync()
    {
        while (true)
        {
            PrintSpoolJob job;
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    _currentJob = null;
                    _isProcessing = false;
                    _allCompletedTcs?.TrySetResult(true);
                    _allCompletedTcs = null;
                    break;
                }

                job = _queue.Dequeue();
                _currentJob = job;
            }

            await ExecuteSingleJobAsync(job);
        }
    }

    /// <summary>
    /// 単一の印刷ジョブを非同期スプール送信し、結果イベントを発行します。
    /// </summary>
    private async Task ExecuteSingleJobAsync(PrintSpoolJob job)
    {
        JobStarted?.Invoke(job);
        bool success = false;
        string? errorMessage = null;

        try
        {
            if (job.IsStreamingJob)
            {
                var progress = new DirectProgress<(int currentSheet, int totalSheets)>(p =>
                {
                    JobProgressChanged?.Invoke(job, p.currentSheet, p.totalSheets);
                });
                success = await _printService.PrintStreamingAsync(
                    job.Sheets!,
                    job.PageSources!,
                    job.Settings,
                    job.JobName,
                    progress);
            }
            else
            {
                success = await _printService.SpoolDocumentAsync(job.PreparedSheets, job.Settings, job.JobName);
            }

            if (!success)
            {
                errorMessage = "印刷処理が完了しませんでした。";
            }
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = ex.Message;
        }

        int remaining;
        lock (_lock)
        {
            _currentJob = null;
            remaining = _queue.Count;
            if (remaining == 0)
            {
                _allCompletedTcs?.TrySetResult(true);
                _allCompletedTcs = null;
            }
        }

        JobCompleted?.Invoke(job, success, errorMessage);
        ActiveJobCountChanged?.Invoke(remaining);
    }

    /// <summary>
    /// 進捗通知をスレッドプール遅延なしで即座に同期配信する <see cref="IProgress{T}"/> 実装
    /// （進捗通知がジョブ完了通知の後に逆転到着する競合を防止）
    /// </summary>
    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
