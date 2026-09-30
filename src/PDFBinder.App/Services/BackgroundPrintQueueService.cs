using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.Services;

/// <summary>
/// バックグラウンドで順次実行する印刷スプールジョブのデータ
/// </summary>
/// <param name="JobName">印刷ジョブ名</param>
/// <param name="Settings">印刷設定</param>
/// <param name="PreparedSheets">レンダリング済みシート一覧</param>
public record PrintSpoolJob(
    string JobName,
    PrintSettings Settings,
    IReadOnlyList<PrintPreparedSheet> PreparedSheets);

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
    private TaskCompletionSource<bool>? _allCompletedTcs;

    /// <inheritdoc/>
    public int ActiveJobCount
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count + (_isProcessing ? 1 : 0);
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
            count = _queue.Count + (_isProcessing ? 1 : 0);
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
                    _isProcessing = false;
                    _allCompletedTcs?.TrySetResult(true);
                    _allCompletedTcs = null;
                    break;
                }

                job = _queue.Dequeue();
            }

            await ExecuteSingleJobAsync(job);

            int remaining;
            lock (_lock)
            {
                remaining = _queue.Count + (_isProcessing ? 1 : 0);
            }
            ActiveJobCountChanged?.Invoke(remaining);
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
            success = await _printService.SpoolDocumentAsync(job.PreparedSheets, job.Settings, job.JobName);
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

        JobCompleted?.Invoke(job, success, errorMessage);
    }
}
