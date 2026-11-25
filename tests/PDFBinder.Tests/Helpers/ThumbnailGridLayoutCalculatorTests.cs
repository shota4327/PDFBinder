#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="ThumbnailGridLayoutCalculator"/> の画像高さ計算、折り返し配置、実体化範囲、および座標連携の単体テスト
/// </summary>
public class ThumbnailGridLayoutCalculatorTests
{
    [Fact]
    public void CalculateImageHeight_HandlesPortraitLandscapeAndInvalidDimensions()
    {
        // A4縦 (595.28 x 841.89)
        double portraitH = ThumbnailGridLayoutCalculator.CalculateImageHeight(220.0, 595.28, 841.89);
        Assert.Equal(311.0, portraitH);

        // 横長 (841.89 x 595.28)
        double landscapeH = ThumbnailGridLayoutCalculator.CalculateImageHeight(220.0, 841.89, 595.28);
        Assert.Equal(156.0, landscapeH);

        // 0・負寸法・NaN時はA4比へフォールバック
        double fallbackH = ThumbnailGridLayoutCalculator.CalculateImageHeight(220.0, 0.0, -10.0);
        Assert.Equal(311.0, fallbackH);

        // 基準幅が0以下なら最小値 1.0
        Assert.Equal(1.0, ThumbnailGridLayoutCalculator.CalculateImageHeight(0.0, 595.28, 841.89));
    }

    [Fact]
    public void Build_WrapsRowsCorrectlyForUniformMixedAndSingleColumnLayouts()
    {
        // 1アイテム幅 = 200 + 30 = 230 -> 利用可能幅 700 なら 3列/行
        var pages = new List<PageSizeMetric>
        {
            new(600, 800), // H = 267 + 30 = 297
            new(800, 600), // H = 150 + 30 = 180
            new(600, 800), // H = 297
            new(600, 800), // 2行目へ折り返し
        };

        var layout = ThumbnailGridLayoutCalculator.Build(pages, availableWidth: 700.0, thumbnailSize: 200.0);
        Assert.Equal(2, layout.Rows.Count);
        Assert.Equal(new ThumbnailGridRow(0, 3, 0.0, 297.0), layout.Rows[0]);
        Assert.Equal(new ThumbnailGridRow(3, 1, 297.0, 594.0), layout.Rows[1]);
        Assert.Equal(690.0, layout.TotalWidth);
        Assert.Equal(594.0, layout.TotalHeight);

        // 極大ズームで1アイテム幅が利用可能幅を超える場合は1列配置
        var narrowLayout = ThumbnailGridLayoutCalculator.Build(pages, availableWidth: 400.0, thumbnailSize: 500.0);
        Assert.Equal(4, narrowLayout.Rows.Count);
    }

    [Fact]
    public void BuildItemBounds_LargeGrid500Pages_IntegratesWithGridLayoutCache()
    {
        var pages = new List<PageSizeMetric>(500);
        for (int i = 0; i < 500; i++)
        {
            pages.Add(new PageSizeMetric(600, 800));
        }

        // 1アイテム幅 = 220 + 30 = 250 -> 幅 1000 で 4列 × 125行
        var layout = ThumbnailGridLayoutCalculator.Build(pages, availableWidth: 1000.0, thumbnailSize: 220.0);
        Assert.Equal(125, layout.Rows.Count);

        // スクロール中盤での実体化範囲が全件ではなく少数行に限定されること
        double row50Top = layout.Rows[50].Top;
        var (first, last) = layout.CalculateRealizationRange(row50Top, viewportHeight: 800.0, bufferHeight: 350.0, minBufferRows: 1);
        Assert.True(first > 0 && last < 499);
        Assert.True(last - first + 1 <= 36, $"Expected realized items <= 36, got {last - first + 1}");

        // オフセット付きで全500件の境界を構築し、GridLayoutCacheで画面外ページへの挿入・交差判定が一致すること
        var allBounds = layout.BuildItemBounds(new Vector(20, 20));
        Assert.Equal(500, allBounds.Count);

        var cache = GridLayoutCache.Build(allBounds, new GridLayoutSignature(500, 1000, layout.TotalHeight, 220));
        var lastRowBounds = allBounds[499].Bounds;
        var (targetIndex, _) = cache.CalculateInsertionTarget(
            new Point(lastRowBounds.Right - 5, lastRowBounds.Top + 50),
            totalPageCount: 500);
        Assert.Equal(500, targetIndex);

        var hits = new HashSet<int>();
        var selectRect = new Rect(allBounds[496].Bounds.Left, allBounds[496].Bounds.Top, 600, 200);
        cache.FindIntersectingIndices(selectRect, hits);
        Assert.Contains(496, hits);
        Assert.Contains(497, hits);
        Assert.Contains(498, hits);
    }
}
