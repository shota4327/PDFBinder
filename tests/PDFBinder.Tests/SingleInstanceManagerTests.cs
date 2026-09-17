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
