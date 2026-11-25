using System;
using System.Collections.Generic;
using System.Windows;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="GridLayoutCache"/> の行インデックス構築、挿入位置算出、および矩形交差判定の単体テスト
/// </summary>
public class GridLayoutCacheTests
{
    private static GridLayoutSignature CreateSignature(int count) =>
        new(count, 1000.0, 800.0, 220.0);

    private static List<GridItemBounds> CreateGridItems(
        int count,
        int columns = 4,
        double itemWidth = 220.0,
        double itemHeight = 300.0,
        double horizontalGap = 20.0,
        double verticalGap = 20.0,
        double originX = 10.0,
        double originY = 10.0)
    {
        var items = new List<GridItemBounds>(count);
        for (int i = 0; i < count; i++)
        {
            int row = i / columns;
            int col = i % columns;
            double x = originX + col * (itemWidth + horizontalGap);
            double y = originY + row * (itemHeight + verticalGap);
            items.Add(new GridItemBounds(i, new Rect(x, y, itemWidth, itemHeight)));
        }

        return items;
    }

    private static (int TargetIndex, Rect BarRect) CalculateNaiveInsertionTarget(
        IReadOnlyList<GridItemBounds> itemBounds,
        Point mousePos,
        int totalPageCount)
    {
        if (totalPageCount <= 0 || itemBounds.Count == 0)
        {
            return (0, GridLayoutCache.EmptyDocumentBarRect);
        }

        var rowItems = new List<GridItemBounds>();
        foreach (var item in itemBounds)
        {
            if (mousePos.Y >= item.Bounds.Top - 8 && mousePos.Y <= item.Bounds.Bottom + 8)
            {
                rowItems.Add(item);
            }
        }
        rowItems.Sort((a, b) => a.Bounds.Left.CompareTo(b.Bounds.Left));

        if (rowItems.Count > 0)
        {
            for (int i = 0; i < rowItems.Count; i++)
            {
                var item = rowItems[i];
                double midX = item.Bounds.Left + item.Bounds.Width / 2.0;
                if (mousePos.X < midX)
                {
                    double barX = i == 0 ? Math.Max(4, item.Bounds.Left + 3) : item.Bounds.Left - 2;
                    return (item.Index, new Rect(barX, item.Bounds.Top + 10, 4, Math.Max(20, item.Bounds.Height - 20)));
                }
            }

            var lastInRow = rowItems[^1];
            return (lastInRow.Index + 1, new Rect(lastInRow.Bounds.Right - 7, lastInRow.Bounds.Top + 10, 4, Math.Max(20, lastInRow.Bounds.Height - 20)));
        }

        if (mousePos.Y < itemBounds[0].Bounds.Top)
        {
            var first = itemBounds[0];
            return (0, new Rect(Math.Max(4, first.Bounds.Left + 3), first.Bounds.Top + 10, 4, Math.Max(20, first.Bounds.Height - 20)));
        }

        var last = itemBounds[^1];
        return (totalPageCount, new Rect(last.Bounds.Right - 7, last.Bounds.Top + 10, 4, Math.Max(20, last.Bounds.Height - 20)));
    }

    [Fact]
    public void CalculateInsertionTarget_EmptyItems_ReturnsFallbackRect()
    {
        var cache = GridLayoutCache.Build(Array.Empty<GridItemBounds>(), CreateSignature(0));

        var (targetIndex0, rect0) = cache.CalculateInsertionTarget(new Point(50, 50), 0);
        var (targetIndex1, rect1) = cache.CalculateInsertionTarget(new Point(50, 50), 3);

        Assert.Equal(0, targetIndex0);
        Assert.Equal(GridLayoutCache.EmptyDocumentBarRect, rect0);
        Assert.Equal(0, targetIndex1);
        Assert.Equal(GridLayoutCache.EmptyDocumentBarRect, rect1);
    }

    [Fact]
    public void CalculateInsertionTarget_SingleItem_HandlesLeftRightAboveBelowAndTolerance()
    {
        var items = new[] { new GridItemBounds(0, new Rect(10, 20, 200, 300)) };
        var cache = GridLayoutCache.Build(items, CreateSignature(1));

        // 左半分 → インデックス 0（先頭余白考慮）
        var (idxLeft, rectLeft) = cache.CalculateInsertionTarget(new Point(50, 100), 1);
        Assert.Equal(0, idxLeft);
        Assert.Equal(new Rect(13, 30, 4, 280), rectLeft);

        // 右半分 → インデックス 1
        var (idxRight, rectRight) = cache.CalculateInsertionTarget(new Point(150, 100), 1);
        Assert.Equal(1, idxRight);
        Assert.Equal(new Rect(203, 30, 4, 280), rectRight);

        // 上側 ±8px 許容範囲内 → 行ヒット扱い（右半分ならインデックス 1）
        var (idxTopTolerance, _) = cache.CalculateInsertionTarget(new Point(150, 12), 1);
        Assert.Equal(1, idxTopTolerance);

        // 上側 許容範囲外 → 強制先頭（インデックス 0）
        var (idxAbove, rectAbove) = cache.CalculateInsertionTarget(new Point(150, 5), 1);
        Assert.Equal(0, idxAbove);
        Assert.Equal(new Rect(13, 30, 4, 280), rectAbove);

        // 下側 ±8px 許容範囲内 → 行ヒット扱い（左半分ならインデックス 0）
        var (idxBottomTolerance, _) = cache.CalculateInsertionTarget(new Point(50, 328), 1);
        Assert.Equal(0, idxBottomTolerance);

        // 下側 許容範囲外 → 強制末尾（インデックス 1）
        var (idxBelow, rectBelow) = cache.CalculateInsertionTarget(new Point(50, 335), 1);
        Assert.Equal(1, idxBelow);
        Assert.Equal(new Rect(203, 30, 4, 280), rectBelow);
    }

    [Fact]
    public void CalculateInsertionTarget_MultiRowWithPartialLastRow_MatchesExpectedPositions()
    {
        // 4列 × 2行 + 端数2件（計10件）
        var items = CreateGridItems(count: 10, columns: 4);
        var cache = GridLayoutCache.Build(items, CreateSignature(10));

        // 2行目先頭アイテム（index 4, Left=10）の左半分 → Math.Max(4, 10 + 3) = 13
        var (idxRowStart, rectRowStart) = cache.CalculateInsertionTarget(new Point(50, 400), 10);
        Assert.Equal(4, idxRowStart);
        Assert.Equal(new Rect(13, 340, 4, 280), rectRowStart);

        // 1行目2番目アイテム（index 1, Left=250）の左半分 → 250 - 2 = 248
        var (idxMid, rectMid) = cache.CalculateInsertionTarget(new Point(280, 100), 10);
        Assert.Equal(1, idxMid);
        Assert.Equal(new Rect(248, 20, 4, 280), rectMid);

        // 1行目末尾（index 3, Right=950）の右半分 → index 4, 950 - 7 = 943
        var (idxRowEnd, rectRowEnd) = cache.CalculateInsertionTarget(new Point(920, 100), 10);
        Assert.Equal(4, idxRowEnd);
        Assert.Equal(new Rect(943, 20, 4, 280), rectRowEnd);

        // 端数行（3行目: index 8, 9）の右側余白 → index 10（末尾）
        var (idxPartialEnd, rectPartialEnd) = cache.CalculateInsertionTarget(new Point(800, 750), 10);
        Assert.Equal(10, idxPartialEnd);
        Assert.Equal(new Rect(463, 660, 4, 280), rectPartialEnd);

        // 最上部より上 → index 0
        var (idxTop, _) = cache.CalculateInsertionTarget(new Point(500, 0), 10);
        Assert.Equal(0, idxTop);

        // 最下部より下 → index 10（末尾アイテム基準）
        var (idxBottom, rectBottom) = cache.CalculateInsertionTarget(new Point(50, 1050), 10);
        Assert.Equal(10, idxBottom);
        Assert.Equal(new Rect(463, 660, 4, 280), rectBottom);
    }

    [Fact]
    public void CalculateInsertionTarget_RowBoundaryAndGapTolerance_SelectsNearestRow()
    {
        // 行0: [0..100], 行1: [110..210]（10px ギャップ）
        var items = new[]
        {
            new GridItemBounds(0, new Rect(10, 0, 100, 100)),
            new GridItemBounds(1, new Rect(10, 110, 100, 100))
        };
        var cache = GridLayoutCache.Build(items, CreateSignature(2));

        // ギャップ内 Y=103（行0から3px、行1から7px）→ 行0 が選ばれる
        var (idxNearRow0, _) = cache.CalculateInsertionTarget(new Point(15, 103), 2);
        Assert.Equal(0, idxNearRow0);

        // ギャップ内 Y=107（行0から7px、行1から3px）→ 行1 が選ばれる
        var (idxNearRow1, _) = cache.CalculateInsertionTarget(new Point(15, 107), 2);
        Assert.Equal(1, idxNearRow1);

        // 境界ちょうど（ギャップ0のケース）: Y=100 は行0に属する
        var adjacentItems = new[]
        {
            new GridItemBounds(0, new Rect(10, 0, 100, 100)),
            new GridItemBounds(1, new Rect(10, 100, 100, 100))
        };
        var adjacentCache = GridLayoutCache.Build(adjacentItems, CreateSignature(2));
        var (idxExactBoundary, _) = adjacentCache.CalculateInsertionTarget(new Point(15, 100), 2);
        Assert.Equal(0, idxExactBoundary);
    }

    [Fact]
    public void CalculateInsertionTarget_LargeGrid_MatchesNaiveLinearCalculation()
    {
        // 1,000ページ（5列 × 200行）
        var items = CreateGridItems(count: 1000, columns: 5, verticalGap: 20.0);
        var cache = GridLayoutCache.Build(items, CreateSignature(1000));

        // 行内・行外・先頭・末尾の複数サンプリング点を検証（ギャップ中央から離れた確定位置）
        var probePoints = new[]
        {
            new Point(0, 0),
            new Point(15, 150),
            new Point(500, 150),
            new Point(1300, 150),
            new Point(350, 32200),
            new Point(1100, 32200),
            new Point(200, 63800),
            new Point(200, 70000)
        };

        foreach (var pt in probePoints)
        {
            var expected = CalculateNaiveInsertionTarget(items, pt, 1000);
            var actual = cache.CalculateInsertionTarget(pt, 1000);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void FindIntersectingIndices_BoundaryCases_ReturnsExpectedIndices()
    {
        var items = CreateGridItems(count: 6, columns: 3, itemWidth: 100, itemHeight: 150, horizontalGap: 20, verticalGap: 20);
        var cache = GridLayoutCache.Build(items, CreateSignature(6));
        var result = new HashSet<int>();

        // Rect.Empty → 0件
        cache.FindIntersectingIndices(Rect.Empty, result);
        Assert.Empty(result);

        // 0×0 点矩形（アイテム0の内部）→ アイテム0 のみ交差
        cache.FindIntersectingIndices(new Rect(50, 50, 0, 0), result);
        Assert.Equal(new[] { 0 }, result);

        // 0×0 点矩形（余白上）→ 0件
        cache.FindIntersectingIndices(new Rect(120, 50, 0, 0), result);
        Assert.Empty(result);

        // 複数行跨ぎ（col 1..2, row 0..1 → index 1, 2, 4, 5）
        cache.FindIntersectingIndices(new Rect(180, 100, 150, 150), result);
        Assert.Equal(new HashSet<int> { 1, 2, 4, 5 }, result);

        // 完全にグリッド範囲外 → 0件
        cache.FindIntersectingIndices(new Rect(1000, 1000, 50, 50), result);
        Assert.Empty(result);
    }

    [Fact]
    public void FindIntersectingIndices_LargeGrid_MatchesNaiveRectIntersection()
    {
        var items = CreateGridItems(count: 1000, columns: 5);
        var cache = GridLayoutCache.Build(items, CreateSignature(1000));
        var result = new HashSet<int>();

        var selectionRects = new[]
        {
            new Rect(0, 0, 500, 500),
            new Rect(260, 15500, 600, 1200),
            new Rect(10, 63000, 1200, 1000),
            new Rect(2000, 2000, 100, 100)
        };

        foreach (var rect in selectionRects)
        {
            var expected = new HashSet<int>();
            foreach (var item in items)
            {
                if (rect.IntersectsWith(item.Bounds))
                {
                    expected.Add(item.Index);
                }
            }

            cache.FindIntersectingIndices(rect, result);
            Assert.Equal(expected, result);
        }
    }
}
