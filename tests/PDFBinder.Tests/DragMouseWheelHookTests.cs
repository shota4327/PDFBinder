using System;
using PDFBinder.App.Controls;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="DragMouseWheelHook"/> のライフサイクル管理および安全性の単体テスト
/// </summary>
public class DragMouseWheelHookTests
{
    [Fact]
    public void DragMouseWheelHook_StartAndStop_DoesNotThrow()
    {
        using var hook = new DragMouseWheelHook();

        // フックの開始と停止が例外なく実行できること
        var exception = Record.Exception(() =>
        {
            hook.StartHook();
            hook.StopHook();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void DragMouseWheelHook_MultipleDispose_IsSafe()
    {
        var hook = new DragMouseWheelHook();
        hook.StartHook();

        // 複数回 Dispose しても例外が発生しないこと
        var exception = Record.Exception(() =>
        {
            hook.Dispose();
            hook.Dispose();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void DragMouseWheelHook_AttachAndStopThreadFilter_DoesNotThrow()
    {
        using var hook = new DragMouseWheelHook();

        var exception = Record.Exception(() =>
        {
            hook.AttachThreadFilter();
            hook.StopHook();
        });

        Assert.Null(exception);
    }
}
