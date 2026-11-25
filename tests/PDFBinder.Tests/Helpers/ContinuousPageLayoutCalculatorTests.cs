#nullable enable
using System;
using System.Collections.Generic;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="ContinuousPageLayoutCalculator"/> の累積座標、二分探索実体化範囲、および境界計算の単体テスト
/// </summary>
public class ContinuousPageLayoutCalculatorTests
{
    private static List<PageSizeMetric> CreateUniformPages(int count, double width = 600, double height = 800)
    {
        var list = new List<PageSizeMetric>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(new PageSizeMetric(width, height));
        }
        return list;
    }

    [Fact]
    public void Build_EmptyPages_ReturnsZeroMetricsAndEmptyRange()
    {
        var layout = ContinuousPageLayoutCalculator.Build(Array.Empty<PageSizeMetric>());

        Assert.Equal(0, layout.Count);
        Assert.Equal(0.0, layout.TotalWidth);
        Assert.Equal(0.0, layout.TotalHeight);
        Assert.Equal((-1, -1), layout.CalculateRealizationRange(0, 800, 1.0));
        Assert.Equal(0.0, layout.GetPageScrollOffset(0, 1.0));
        Assert.Null(layout.GetViewportRelativeBounds(0, 1.0, 0, 800));
    }

    [Fact]
    public void Build_MixedPageSizes_ComputesCumulativeTopsAndTotalsAccurately()
    {
        var pages = new[]
        {
            new PageSizeMetric(600, 800),   // A4縦: Y=0..800
            new PageSizeMetric(1200, 900),  // 横長: Y=830..1730
            new PageSizeMetric(500, 700)    // 小サイズ: Y=1760..2460
        };

        var layout = ContinuousPageLayoutCalculator.Build(pages, pageSpacing: 30.0);

        Assert.Equal(3, layout.Count);
        Assert.Equal(1200.0, layout.TotalWidth);
        Assert.Equal(2460.0, layout.TotalHeight);
        Assert.Equal(0.0, layout.GetPageTop(0));
        Assert.Equal(830.0, layout.GetPageTop(1));
        Assert.Equal(1760.0, layout.GetPageTop(2));
        Assert.Equal(800.0, layout.GetPageBottom(0));
        Assert.Equal(1730.0, layout.GetPageBottom(1));
        Assert.Equal(2460.0, layout.GetPageBottom(2));
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    [InlineData(32.0)]
    public void CalculateRealizationRange_LargeDocument_RestrictsRangeAroundViewportAcrossZoomLevels(double zoom)
    {
        var layout = ContinuousPageLayoutCalculator.Build(CreateUniformPages(1000), pageSpacing: 30.0);
        double viewportHeight = 800.0;
        double middleOffset = layout.GetPageScrollOffset(500, zoom, topMargin: 20.0);

        var (first, last) = layout.CalculateRealizationRange(
            middleOffset,
            viewportHeight,
            zoom,
            topMargin: 20.0,
            bottomMargin: 62.0,
            bufferViewports: 1.0,
            minBufferPages: 2);

        Assert.InRange(first, 0, 500);
        Assert.InRange(last, 500, 999);
        Assert.True(first <= 498, $"Expected at least 2 buffer pages before 500, got first={first}");
        Assert.True(last >= 502, $"Expected at least 2 buffer pages after 500, got last={last}");
        Assert.True(last - first + 1 < 100, $"Realized range too large at zoom={zoom}: {first}..{last}");
    }

    [Fact]
    public void GetPageScrollOffset_And_GetViewportRelativeBounds_AreConsistentInScrolledAndCenteredStates()
    {
        // スクロールが発生する多数ページケース
        var largeLayout = ContinuousPageLayoutCalculator.Build(CreateUniformPages(20, 600, 800), pageSpacing: 30.0);
        double offsetPage10 = largeLayout.GetPageScrollOffset(10, zoom: 1.5, topMargin: 20.0);
        // page 10 top = 10 * 830 = 8300; offset = 20 + 8300 * 1.5 = 12470
        Assert.Equal(12470.0, offsetPage10, precision: 3);

        var boundsPage10 = largeLayout.GetViewportRelativeBounds(10, zoom: 1.5, offsetPage10, viewportHeight: 900.0, topMargin: 20.0, bottomMargin: 62.0);
        Assert.NotNull(boundsPage10);
        Assert.Equal(0.0, boundsPage10!.Value.Top, precision: 3);
        Assert.Equal(1200.0, boundsPage10.Value.Bottom, precision: 3);

        // 1ページのみ（画面内に収まり中央揃えされるケース）
        var singleLayout = ContinuousPageLayoutCalculator.Build(CreateUniformPages(1, 500, 400), pageSpacing: 30.0);
        // contentTotalHeight = 400 + 20 + 62 = 482, viewportHeight = 1000 -> originY = (1000 - 482)/2 + 20 = 279
        var centeredBounds = singleLayout.GetViewportRelativeBounds(0, zoom: 1.0, verticalOffset: 0.0, viewportHeight: 1000.0, topMargin: 20.0, bottomMargin: 62.0);
        Assert.NotNull(centeredBounds);
        Assert.Equal(279.0, centeredBounds!.Value.Top, precision: 3);
        Assert.Equal(679.0, centeredBounds.Value.Bottom, precision: 3);
    }
}
