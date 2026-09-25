using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="ZoomHelper"/> のズーム倍率スナップ目盛りおよび拡大・縮小遷移ロジックの単体テスト
/// </summary>
public class ZoomHelperTests
{
    [Fact]
    public void ZoomConstants_AreExpectedValues()
    {
        // 最小5%、最大3200%
        Assert.Equal(0.05, ZoomHelper.MinZoom);
        Assert.Equal(32.0, ZoomHelper.MaxZoom);
        Assert.Equal(0.05, ZoomHelper.ZoomSnapSteps[0]);
        Assert.Equal(32.0, ZoomHelper.ZoomSnapSteps[^1]);
    }

    [Theory]
    [InlineData(0.05, 0.1)]
    [InlineData(0.1, 0.15)]
    [InlineData(0.4, 0.5)]
    [InlineData(0.5, 0.75)]
    [InlineData(0.75, 1.0)]
    [InlineData(1.0, 1.25)]
    [InlineData(1.1, 1.25)]
    [InlineData(2.0, 2.5)]
    [InlineData(28.0, 32.0)]
    [InlineData(32.0, 32.0)]
    [InlineData(40.0, 32.0)]
    public void GetNextZoomIn_ReturnsNextStep_OrMaxZoom(double currentZoom, double expectedZoom)
    {
        // Act
        double nextZoom = ZoomHelper.GetNextZoomIn(currentZoom);

        // Assert
        Assert.Equal(expectedZoom, nextZoom);
    }

    [Theory]
    [InlineData(32.0, 28.0)]
    [InlineData(28.0, 24.0)]
    [InlineData(1.25, 1.0)]
    [InlineData(1.1, 1.0)]
    [InlineData(1.0, 0.75)]
    [InlineData(0.75, 0.5)]
    [InlineData(0.5, 0.4)]
    [InlineData(0.4, 0.3)]
    [InlineData(0.3, 0.2)]
    [InlineData(0.2, 0.15)]
    [InlineData(0.15, 0.1)]
    [InlineData(0.1, 0.05)]
    [InlineData(0.05, 0.05)]
    [InlineData(0.03, 0.05)]
    public void GetNextZoomOut_ReturnsPreviousStep_OrMinZoom(double currentZoom, double expectedZoom)
    {
        // Act
        double prevZoom = ZoomHelper.GetNextZoomOut(currentZoom);

        // Assert
        Assert.Equal(expectedZoom, prevZoom);
    }
}
