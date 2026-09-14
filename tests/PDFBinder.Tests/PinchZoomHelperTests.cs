using System.Windows;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="PinchZoomHelper"/> のピンチズーム計算ロジックの単体テスト
/// </summary>
public class PinchZoomHelperTests
{
    [Fact]
    public void Calculate_WithInvalidDistance_ReturnsUnchangedZoomAndOffset()
    {
        // Arrange
        double currentZoom = 1.0;
        Point center = new(200, 150);

        // Act & Assert (前回の距離が小さすぎる場合)
        var result1 = PinchZoomHelper.Calculate(
            currentZoom: currentZoom,
            previousDistance: 0.5,
            currentDistance: 100.0,
            previousCenter: center,
            currentCenter: center,
            horizontalOffset: 50,
            verticalOffset: 50);

        Assert.Equal(currentZoom, result1.NewZoom);
        Assert.Equal(50, result1.TargetHorizontalOffset);
        Assert.Equal(50, result1.TargetVerticalOffset);

        // Act & Assert (現在の距離が小さすぎる場合)
        var result2 = PinchZoomHelper.Calculate(
            currentZoom: currentZoom,
            previousDistance: 100.0,
            currentDistance: 0.0,
            previousCenter: center,
            currentCenter: center,
            horizontalOffset: 50,
            verticalOffset: 50);

        Assert.Equal(currentZoom, result2.NewZoom);
        Assert.Equal(50, result2.TargetHorizontalOffset);
        Assert.Equal(50, result2.TargetVerticalOffset);
    }

    [Fact]
    public void Calculate_PinchZoomInPlace_CalculatesExactNewZoomAndOffset()
    {
        // Arrange: 画面中央(400, 300)で距離が100pxから200pxへ2倍に拡大
        double currentZoom = 1.0;
        Point center = new(400, 300);
        double offsetH = 100;
        double offsetV = 100;

        // Act
        var result = PinchZoomHelper.Calculate(
            currentZoom: currentZoom,
            previousDistance: 100.0,
            currentDistance: 200.0,
            previousCenter: center,
            currentCenter: center,
            horizontalOffset: offsetH,
            verticalOffset: offsetV);

        // Assert:
        // NewZoom = 1.0 * (200 / 100) = 2.0
        // TargetH = (100 + 400) * 2.0 - 400 = 600
        // TargetV = (100 + 300) * 2.0 - 300 = 500
        Assert.Equal(2.0, result.NewZoom);
        Assert.Equal(600.0, result.TargetHorizontalOffset);
        Assert.Equal(500.0, result.TargetVerticalOffset);
    }

    [Fact]
    public void Calculate_PinchZoomWithPanMovement_AdjustsOffsetWithCenterDelta()
    {
        // Arrange: 2倍拡大しつつ、中心位置が(400, 300)から(410, 305)へ移動
        double currentZoom = 1.0;
        Point prevCenter = new(400, 300);
        Point currCenter = new(410, 305);
        double offsetH = 100;
        double offsetV = 100;

        // Act
        var result = PinchZoomHelper.Calculate(
            currentZoom: currentZoom,
            previousDistance: 100.0,
            currentDistance: 200.0,
            previousCenter: prevCenter,
            currentCenter: currCenter,
            horizontalOffset: offsetH,
            verticalOffset: offsetV);

        // Assert:
        // TargetH = (100 + 400) * 2.0 - 410 = 590
        // TargetV = (100 + 300) * 2.0 - 305 = 495
        Assert.Equal(2.0, result.NewZoom);
        Assert.Equal(590.0, result.TargetHorizontalOffset);
        Assert.Equal(495.0, result.TargetVerticalOffset);
    }

    [Fact]
    public void Calculate_ClampsToMinAndMaxZoom()
    {
        // Arrange
        Point center = new(200, 200);

        // 極端な拡大（50倍） -> DefaultMaxZoom (32.0) にクランプ
        var zoomInResult = PinchZoomHelper.Calculate(
            currentZoom: 1.0,
            previousDistance: 100.0,
            currentDistance: 5000.0,
            previousCenter: center,
            currentCenter: center,
            horizontalOffset: 0,
            verticalOffset: 0);

        Assert.Equal(PinchZoomHelper.DefaultMaxZoom, zoomInResult.NewZoom);

        // 極端な縮小（0.1倍） -> MinZoom (0.5) にクランプ
        var zoomOutResult = PinchZoomHelper.Calculate(
            currentZoom: 1.0,
            previousDistance: 1000.0,
            currentDistance: 100.0,
            previousCenter: center,
            currentCenter: center,
            horizontalOffset: 200,
            verticalOffset: 200);

        Assert.Equal(PinchZoomHelper.DefaultMinZoom, zoomOutResult.NewZoom);
    }

    [Fact]
    public void Calculate_NegativeOffsets_ClampedToZero()
    {
        // Arrange: 縮小時にオフセット計算結果が負値になるケース
        Point center = new(500, 500);
        var result = PinchZoomHelper.Calculate(
            currentZoom: 2.0,
            previousDistance: 200.0,
            currentDistance: 100.0,
            previousCenter: center,
            currentCenter: center,
            horizontalOffset: 10,
            verticalOffset: 10);

        // (10 + 500) * 0.5 - 500 = 255 - 500 = -245 -> 0にクランプ
        Assert.Equal(1.0, result.NewZoom);
        Assert.Equal(0.0, result.TargetHorizontalOffset);
        Assert.Equal(0.0, result.TargetVerticalOffset);
    }
}
