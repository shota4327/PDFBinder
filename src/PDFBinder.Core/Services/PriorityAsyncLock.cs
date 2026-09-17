namespace PDFBinder.Core.Services;

/// <summary>
/// 優先度付き非同期排他ロック（PDFiumネイティブAPIの直列化と優先実行を制御）
/// </summary>
public sealed class PriorityAsyncLock
{
    private readonly object _syncRoot = new();
    private readonly Queue<WaitNode> _highQueue = new();
    private readonly Queue<WaitNode> _normalQueue = new();
    private readonly Queue<WaitNode> _lowQueue = new();
    private bool _isLocked;

    /// <summary>
    /// 現在ロックが取得されているかどうかを取得します。
    /// </summary>
    public bool IsLocked
    {
        get { lock (_syncRoot) { return _isLocked; } }
    }

    /// <summary>
    /// 待機中のリクエスト総数を取得します。
    /// </summary>
    public int WaitingCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _highQueue.Count + _normalQueue.Count + _lowQueue.Count;
            }
        }
    }

    /// <summary>
    /// 指定された優先度で排他ロックを非同期に取得します。
    /// </summary>
    public Task<IDisposable> AcquireAsync(
        RenderPriority priority = RenderPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IDisposable>(cancellationToken);
        }

        lock (_syncRoot)
        {
            if (!_isLocked)
            {
                _isLocked = true;
                return Task.FromResult<IDisposable>(new Releaser(this));
            }

            var tcs = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
            var node = new WaitNode(tcs, priority);

            if (cancellationToken.CanBeCanceled)
            {
                node.CancellationTokenRegistration = cancellationToken.Register(() =>
                {
                    lock (_syncRoot)
                    {
                        node.IsCancelled = true;
                        tcs.TrySetCanceled(cancellationToken);
                    }
                });
            }

            GetQueue(priority).Enqueue(node);
            return tcs.Task;
        }
    }

    private Queue<WaitNode> GetQueue(RenderPriority priority) => priority switch
    {
        RenderPriority.High => _highQueue,
        RenderPriority.Normal => _normalQueue,
        _ => _lowQueue
    };

    private void Release()
    {
        lock (_syncRoot)
        {
            while (TryDequeueNext(out var nextNode))
            {
                if (nextNode.IsCancelled)
                {
                    nextNode.DisposeRegistration();
                    continue;
                }

                nextNode.DisposeRegistration();
                if (nextNode.Tcs.TrySetResult(new Releaser(this)))
                {
                    return;
                }
            }

            _isLocked = false;
        }
    }

    private bool TryDequeueNext(out WaitNode node)
    {
        if (_highQueue.Count > 0)
        {
            node = _highQueue.Dequeue();
            return true;
        }
        if (_normalQueue.Count > 0)
        {
            node = _normalQueue.Dequeue();
            return true;
        }
        if (_lowQueue.Count > 0)
        {
            node = _lowQueue.Dequeue();
            return true;
        }

        node = default!;
        return false;
    }

    private sealed class WaitNode
    {
        public TaskCompletionSource<IDisposable> Tcs { get; }
        public RenderPriority Priority { get; }
        public bool IsCancelled { get; set; }
        public CancellationTokenRegistration CancellationTokenRegistration { get; set; }

        public WaitNode(TaskCompletionSource<IDisposable> tcs, RenderPriority priority)
        {
            Tcs = tcs;
            Priority = priority;
        }

        public void DisposeRegistration()
        {
            CancellationTokenRegistration.Dispose();
        }
    }

    private sealed class Releaser : IDisposable
    {
        private PriorityAsyncLock? _owner;

        public Releaser(PriorityAsyncLock owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
