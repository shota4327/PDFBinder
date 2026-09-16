using System.Windows;
using PDFBinder.App.Interop.WinTab;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// WinTab連携サービス（WinTabService）の動作およびフォールバックの単体テスト。
/// </summary>
public class WinTabServiceTests
{
    [Fact]
    public void Initialize_WithZeroHwnd_FailsGracefully()
    {
        // IntPtr.Zeroを渡した場合、例外をスローせず安全にfalseを返しIsAvailableがfalseであることを検証
        using var service = new WinTabService();
        bool result = service.Initialize(IntPtr.Zero);

        Assert.False(result);
        Assert.False(service.IsAvailable);
        Assert.False(service.IsPenDown);
    }

    [Fact]
    public void CloseAndDispose_MultipleCalls_AreIdempotentAndSafe()
    {
        // CloseおよびDisposeを複数回呼び出しても例外が発生しないことを検証
        using var service = new WinTabService();
        service.Close();
        service.Close();
        service.Dispose();
        service.Dispose();

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public void WinTabPacketEventArgs_HoldsValuesCorrectly()
    {
        // イベント引数が指定したスクリーン座標・筆圧・種別を正確に保持することを検証
        var point = new Point(120, 340);
        var args = new WinTabPacketEventArgs(point, 2048, 0.5f, WinTabPacketType.Down);

        Assert.Equal(point, args.ScreenPoint);
        Assert.Equal(2048, args.RawPressure);
        Assert.Equal(0.5f, args.PressureFactor);
        Assert.Equal(WinTabPacketType.Down, args.PacketType);
    }
}
