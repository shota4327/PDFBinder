using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="PriorityAsyncLock"/> の優先度制御および排他制御単体テスト
/// </summary>
public class PriorityAsyncLockTests
{
    [Fact]
    public async Task AcquireAsync_SingleThread_LocksAndReleasesCorrectly()
    {
        // Arrange
        var asyncLock = new PriorityAsyncLock();
        Assert.False(asyncLock.IsLocked);

        // Act
        using (var releaser = await asyncLock.AcquireAsync())
        {
            // Assert
            Assert.True(asyncLock.IsLocked);
        }

        Assert.False(asyncLock.IsLocked);
    }

    [Fact]
    public async Task AcquireAsync_PriorityOrdering_HighAcquiresBeforeLow()
    {
        // Arrange
        var asyncLock = new PriorityAsyncLock();
        var executionOrder = new List<string>();

        // 最初にロックを取得して後続リクエストをキューに溜める
        var initialReleaser = await asyncLock.AcquireAsync();

        // Act: Low優先度を先にキューに入れ、その後にHigh優先度を入れる
        var lowTask = Task.Run(async () =>
        {
            using (await asyncLock.AcquireAsync(RenderPriority.Low))
            {
                executionOrder.Add("Low");
            }
        });

        // 少し待機してLowが確実にキューに入った状態にする
        await Task.Delay(50);

        var highTask = Task.Run(async () =>
        {
            using (await asyncLock.AcquireAsync(RenderPriority.High))
            {
                executionOrder.Add("High");
            }
        });

        await Task.Delay(50);

        // 初期ロックを解放
        initialReleaser.Dispose();

        // 両方のタスクが完了するのを待機
        await Task.WhenAll(lowTask, highTask);

        // Assert: HighがLowよりも先に実行されていることを検証
        Assert.Equal(2, executionOrder.Count);
        Assert.Equal("High", executionOrder[0]);
        Assert.Equal("Low", executionOrder[1]);
    }

    [Fact]
    public async Task AcquireAsync_CancelledRequest_DoesNotBlockSubsequentRequests()
    {
        // Arrange
        var asyncLock = new PriorityAsyncLock();
        var initialReleaser = await asyncLock.AcquireAsync();

        using var cts = new CancellationTokenSource();
        var cancelledTask = asyncLock.AcquireAsync(RenderPriority.High, cts.Token);

        // キャンセルを実行
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledTask);

        // 後続のリクエスト
        var nextTask = Task.Run(async () =>
        {
            using (await asyncLock.AcquireAsync(RenderPriority.Normal))
            {
                return true;
            }
        });

        initialReleaser.Dispose();

        bool result = await nextTask;
        Assert.True(result);
        Assert.False(asyncLock.IsLocked);
    }

    [Fact]
    public async Task AcquireAsync_AlreadyCancelledToken_ThrowsImmediately()
    {
        // Arrange
        var asyncLock = new PriorityAsyncLock();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await asyncLock.AcquireAsync(RenderPriority.Normal, cts.Token);
        });
    }
}
