using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PDFBinder.App.Helpers;
using PDFBinder.App.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="SingleInstanceManager"/> の単体テスト
/// </summary>
[Collection("SingleInstance")]
public class SingleInstanceManagerTests
{
    [Fact]
    public void TryAcquireOwnership_FirstInstance_ReturnsTrue()
    {
        using var manager = new SingleInstanceManager();
        var acquired = manager.TryAcquireOwnership();

        Assert.True(acquired);
    }

    [Fact]
    public void TryAcquireOwnership_SecondInstanceRetriesAfterFirstDisposed_AcquiresOwnership()
    {
        var manager1 = new SingleInstanceManager();
        Assert.True(manager1.TryAcquireOwnership());

        using var manager2 = new SingleInstanceManager();
        // 1回目: manager1 が所有しているため失敗
        Assert.False(manager2.TryAcquireOwnership());
        // 2回目（再試行）: 依然として manager1 が所有しているため失敗（既存の未所有ミューテックスが安全に破棄される）
        Assert.False(manager2.TryAcquireOwnership());

        // manager1 を解放
        manager1.Dispose();

        // 3回目: manager1 解放後は所有権を取得できること
        Assert.True(manager2.TryAcquireOwnership());
    }

    [Fact]
    public async Task IPC_Communication_SendsAndReceivesPayload()
    {
        using var serverManager = new SingleInstanceManager();
        var acquired = serverManager.TryAcquireOwnership();
        Assert.True(acquired);

        var receivedTcs = new TaskCompletionSource<SingleInstancePayload>();
        serverManager.StartServer(payload =>
        {
            receivedTcs.TrySetResult(payload);
            return Task.CompletedTask;
        });

        // クライアント側から送信テスト
        var clientArgs = new CommandLineArgsResult(
            new List<string> { @"C:\test\sample.pdf" },
            ForceNewWindow: false);

        using var clientManager = new SingleInstanceManager();
        var sent = await clientManager.TrySendToExistingInstanceAsync(clientArgs);

        Assert.True(sent);

        var completedTask = await Task.WhenAny(receivedTcs.Task, Task.Delay(3000));
        Assert.Same(receivedTcs.Task, completedTask);

        var received = await receivedTcs.Task;
        Assert.NotNull(received);
        Assert.False(received.ForceNewWindow);
        Assert.Single(received.Files);
        Assert.Equal(@"C:\test\sample.pdf", received.Files[0]);
    }
}
