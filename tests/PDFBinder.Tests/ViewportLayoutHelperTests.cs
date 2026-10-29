using System;
using System.Collections.Generic;
using System.Linq;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="ViewportLayoutHelper"/> の垂直スクロール可視判定およびカレントページ判定の単体テスト
/// </summary>
public class ViewportLayoutHelperTests
{
    [Fact]
    public void FindVisibleItems_ZeroTotalCount_ReturnsEmpty()
    {
        var result = ViewportLayoutHelper.FindVisibleItems(0, 800, i => new PageBounds(i * 100, (i + 1) * 100));
        Assert.Empty(result);
    }

    [Fact]
    public void FindVisibleItems_ZeroOrNegativeViewportHeight_ReturnsEmpty()
    {
        var result = ViewportLayoutHelper.FindVisibleItems(10, 0, i => new PageBounds(i * 100, (i + 1) * 100));
        Assert.Empty(result);

        var negativeResult = ViewportLayoutHelper.FindVisibleItems(10, -500, i => new PageBounds(i * 100, (i + 1) * 100));
        Assert.Empty(negativeResult);
    }

    [Fact]
    public void FindVisibleItems_SingleItem_AboveViewport_ReturnsEmpty()
    {
        // アイテムは [-200, -50] にあり、ビューポート [0, 800] の上方外側
        var result = ViewportLayoutHelper.FindVisibleItems(1, 800, _ => new PageBounds(-200, -50));
        Assert.Empty(result);
    }

    [Fact]
    public void FindVisibleItems_SingleItem_BelowViewport_ReturnsEmpty()
    {
        // アイテムは [850, 1000] にあり、ビューポート [0, 800] の下方外側
        var result = ViewportLayoutHelper.FindVisibleItems(1, 800, _ => new PageBounds(850, 1000));
        Assert.Empty(result);
    }

    [Fact]
    public void FindVisibleItems_SingleItem_InsideViewport_ReturnsItem()
    {
        // アイテムは [100, 400] にあり、ビューポート [0, 800] に完全に収まる
        var result = ViewportLayoutHelper.FindVisibleItems(1, 800, _ => new PageBounds(100, 400));
        Assert.Single(result);
        Assert.Equal(0, result[0].Index);
        Assert.Equal(100, result[0].Bounds.Top);
        Assert.Equal(400, result[0].Bounds.Bottom);
    }

    [Fact]
    public void FindVisibleItems_SingleItem_SpanningEntireViewport_ReturnsItem()
    {
        // アイテムは [-100, 1000] にあり、ビューポート [0, 800] を完全に覆う
        var result = ViewportLayoutHelper.FindVisibleItems(1, 800, _ => new PageBounds(-100, 1000));
        Assert.Single(result);
        Assert.Equal(0, result[0].Index);
    }

    [Fact]
    public void FindVisibleItems_ManyItems_ScrolledToTop_ReturnsFirstPages()
    {
        // 各ページ高さ300px、マージン30px（ピッチ330px）。0ページ目がトップ（Y=20）
        // Page 0: [20, 320]
        // Page 1: [350, 650]
        // Page 2: [680, 980] -> [0, 800] と交差
        // Page 3: [1010, 1310] -> ビューポート外
        int total = 50;
        double viewportHeight = 800;

        var result = ViewportLayoutHelper.FindVisibleItems(total, viewportHeight, i =>
        {
            double top = 20 + i * 330;
            return new PageBounds(top, top + 300);
        });

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { 0, 1, 2 }, result.Select(r => r.Index));
    }

    [Fact]
    public void FindVisibleItems_ManyItems_ScrolledToMiddle_ReturnsAccurateVisiblePages()
    {
        // 50ページ中、スクロールオフセットにより Page 25〜27 がビューポート [0, 800] に収まっている状態
        // Page 24: [-200, 100] (交差)
        // Page 25: [130, 430] (交差)
        // Page 26: [460, 760] (交差)
        // Page 27: [790, 1090] (交差)
        // Page 28: [1120, 1420] (外側)
        int total = 50;
        double viewportHeight = 800;
        double scrollOffset = 25 * 330 - 130; // Page 25 の top が 130 になるよう設定

        var result = ViewportLayoutHelper.FindVisibleItems(total, viewportHeight, i =>
        {
            double top = i * 330 - scrollOffset;
            return new PageBounds(top, top + 300);
        });

        Assert.Equal(4, result.Count);
        Assert.Equal(new[] { 24, 25, 26, 27 }, result.Select(r => r.Index));
    }

    [Fact]
    public void FindVisibleItems_LargeCount_1000Items_PerformsMinimalCalls()
    {
        // 1,000ページの大規模ドキュメントにおいて、O(log N) による最小限のアクセス回数（15回以下）で可視ページを検出できること
        int total = 1000;
        double viewportHeight = 900;
        double scrollOffset = 750 * 500; // 750ページ目付近を表示
        int callCount = 0;

        var result = ViewportLayoutHelper.FindVisibleItems(total, viewportHeight, i =>
        {
            callCount++;
            double top = i * 500 - scrollOffset;
            return new PageBounds(top, top + 450);
        });

        // 750ページ目 [0, 450]、751ページ目 [500, 950] が可視
        Assert.Equal(2, result.Count);
        Assert.Equal(750, result[0].Index);
        Assert.Equal(751, result[1].Index);

        // 呼び出し回数は二分探索（約10回）＋前後展開（各1〜2回）で15回以下に抑えられること
        Assert.True(callCount <= 15, $"1,000ページ走査時のアクセス回数が {callCount} 回でした（期待値: 15回以下）。");
    }

    [Fact]
    public void FindVisibleItems_NullBoundsDuringBinarySearch_FallsBackToLinearScan()
    {
        // コンテナ未生成等で null を返却した場合、安全に線形フォールバックすること
        int total = 5;
        double viewportHeight = 800;

        var result = ViewportLayoutHelper.FindVisibleItems(total, viewportHeight, i =>
        {
            if (i == 2) return null; // mid (2) で null が発生
            double top = i * 250;
            return new PageBounds(top, top + 200);
        });

        // 0, 1, 3 が [0, 800] 内で取得されること（2は null のためスキップ、4は top 1000 > 800 のため範囲外）
        Assert.Equal(new[] { 0, 1, 3 }, result.Select(r => r.Index));
    }

    [Fact]
    public void FindVisibleItems_UnequalPageHeights_ReturnsAccuratePages()
    {
        // 縦長・横長など各ページで高さが異なるドキュメント
        var heights = new[] { 400.0, 200.0, 600.0, 300.0, 500.0 };
        var tops = new double[5];
        double currentTop = 0;
        for (int i = 0; i < 5; i++)
        {
            tops[i] = currentTop;
            currentTop += heights[i] + 20; // マージン20
        }

        // ビューポート [300, 1000] に相当（オフセット 300）
        double offset = 300;
        var result = ViewportLayoutHelper.FindVisibleItems(5, 700, i =>
        {
            double top = tops[i] - offset;
            return new PageBounds(top, top + heights[i]);
        });

        // Page 0: [-300, 100] (交差)
        // Page 1: [120, 320] (交差)
        // Page 2: [340, 940] (交差)
        // Page 3: [960, 1260] (外側: top > 700)
        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { 0, 1, 2 }, result.Select(r => r.Index));
    }

    [Fact]
    public void FindBestMatchingIndex_EmptyVisibleItems_ReturnsNull()
    {
        var result = ViewportLayoutHelper.FindBestMatchingIndex(Array.Empty<VisibleItemBounds>(), 400);
        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatchingIndex_MultipleVisibleItems_SelectsItemClosestToCenter()
    {
        // Viewport: 800px (Center = 400px)
        // Item 0: [0, 350], Center = 175, Distance = |175 - 400| = 225
        // Item 1: [370, 720], Center = 545, Distance = |545 - 400| = 145 -> 最も近い
        // Item 2: [740, 1090], Center = 915, Distance = |915 - 400| = 515
        var visibleItems = new List<VisibleItemBounds>
        {
            new(0, new PageBounds(0, 350)),
            new(1, new PageBounds(370, 720)),
            new(2, new PageBounds(740, 1090))
        };

        var bestIndex = ViewportLayoutHelper.FindBestMatchingIndex(visibleItems, 400);
        Assert.Equal(1, bestIndex);
    }

    [Fact]
    public void FindBestMatchingIndex_SingleItem_ReturnsItsIndex()
    {
        var visibleItems = new List<VisibleItemBounds>
        {
            new(42, new PageBounds(100, 500))
        };

        var bestIndex = ViewportLayoutHelper.FindBestMatchingIndex(visibleItems, 300);
        Assert.Equal(42, bestIndex);
    }
}
