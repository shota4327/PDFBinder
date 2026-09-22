using PDFBinder.App.Controls;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="AutoScroller"/> の速度計算ロジックおよび境界値判定の単体テスト
/// </summary>
public class AutoScrollerTests
{
    private const double ViewportHeight = 500.0;
    private const double ZoneHeight = 40.0;
    private const double MaxSpeed = 24.0;

    [Fact]
    public void CalculateScrollSpeed_AtTopEdge_ReturnsMaxNegativeSpeed()
    {
        // 上端（Y = 0）では上方向への最大速度（-MaxSpeed）
        double speed = AutoScroller.CalculateScrollSpeed(0, ViewportHeight, ZoneHeight, MaxSpeed);
        Assert.Equal(-MaxSpeed, speed, precision: 3);
    }

    [Fact]
    public void CalculateScrollSpeed_InsideTopZone_ReturnsProportionalNegativeSpeed()
    {
        // 上端ゾーンの中央（Y = 20, Zone = 40）では半分の上方向速度（-MaxSpeed * 0.5）
        double speed = AutoScroller.CalculateScrollSpeed(20, ViewportHeight, ZoneHeight, MaxSpeed);
        Assert.Equal(-12.0, speed, precision: 3);
    }

    [Fact]
    public void CalculateScrollSpeed_AtTopZoneBoundary_ReturnsZero()
    {
        // 上端ゾーンの境界（Y = 40）ではスクロールしない（0）
        double speed = AutoScroller.CalculateScrollSpeed(40, ViewportHeight, ZoneHeight, MaxSpeed);
        Assert.Equal(0.0, speed, precision: 3);
    }

    [Fact]
    public void CalculateScrollSpeed_InsideDeadZone_ReturnsZero()
    {
        // 不感帯（Y = 100, 250, 400）ではスクロールしない（0）
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(100, ViewportHeight, ZoneHeight, MaxSpeed));
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(250, ViewportHeight, ZoneHeight, MaxSpeed));
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(400, ViewportHeight, ZoneHeight, MaxSpeed));
    }

    [Fact]
    public void CalculateScrollSpeed_AtBottomZoneBoundary_ReturnsZero()
    {
        // 下端ゾーンの境界（Y = 460, Viewport = 500, Zone = 40）ではスクロールしない（0）
        double speed = AutoScroller.CalculateScrollSpeed(460, ViewportHeight, ZoneHeight, MaxSpeed);
        Assert.Equal(0.0, speed, precision: 3);
    }

    [Fact]
    public void CalculateScrollSpeed_InsideBottomZone_ReturnsProportionalPositiveSpeed()
    {
        // 下端ゾーンの中央（Y = 480, Viewport = 500, Zone = 40）では半分の下方向速度（MaxSpeed * 0.5）
        double speed = AutoScroller.CalculateScrollSpeed(480, ViewportHeight, ZoneHeight, MaxSpeed);
        Assert.Equal(12.0, speed, precision: 3);
    }

    [Fact]
    public void CalculateScrollSpeed_AtBottomEdge_ReturnsMaxPositiveSpeed()
    {
        // 下端（Y = 500）では下方向への最大速度（MaxSpeed）
        double speed = AutoScroller.CalculateScrollSpeed(500, ViewportHeight, ZoneHeight, MaxSpeed);
        Assert.Equal(MaxSpeed, speed, precision: 3);
    }

    [Fact]
    public void CalculateScrollSpeed_WithInvalidParameters_ReturnsZero()
    {
        // ビューポートの高さが0以下の場合
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(10, 0, ZoneHeight, MaxSpeed));
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(10, -100, ZoneHeight, MaxSpeed));

        // ゾーンの高さが0以下の場合
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(10, ViewportHeight, 0, MaxSpeed));

        // 最大速度が0以下の場合
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(10, ViewportHeight, ZoneHeight, 0));
    }

    [Fact]
    public void CalculateScrollSpeed_OutOfBounds_ReturnsZero()
    {
        // 上端より外側（負の座標）や下端より外側の座標
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(-10, ViewportHeight, ZoneHeight, MaxSpeed));
        Assert.Equal(0.0, AutoScroller.CalculateScrollSpeed(510, ViewportHeight, ZoneHeight, MaxSpeed));
    }
}
