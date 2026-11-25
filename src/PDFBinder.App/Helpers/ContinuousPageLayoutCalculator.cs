#nullable enable
using System;
using System.Collections.Generic;
using PDFBinder.App.ViewModels;

namespace PDFBinder.App.Helpers;

/// <summary>
/// ページの表示論理寸法（幅・高さ）を表す不変構造体。
/// </summary>
/// <param name="Width">表示幅（pt/DIP）</param>
/// <param name="Height">表示高さ（pt/DIP）</param>
public readonly record struct PageSizeMetric(double Width, double Height);

/// <summary>
/// 詳細ビュー連続スクロールモードにおける全ページの累積座標、実体化範囲、およびスクロールオフセットを
/// コンテナ未生成のまま高速算出するレイアウト計算ヘルパー。
/// </summary>
public sealed class ContinuousPageLayoutCalculator
{
    /// <summary>A4標準幅（pt）</summary>
    public const double DefaultPageWidth = 595.28;

    /// <summary>A4標準高さ（pt）</summary>
    public const double DefaultPageHeight = 841.89;

    private readonly double[] _pageTops;
    private readonly double[] _pageWidths;
    private readonly double[] _pageHeights;

    private ContinuousPageLayoutCalculator(
        double[] pageTops,
        double[] pageWidths,
        double[] pageHeights,
        double pageSpacing,
        double totalWidth,
        double totalHeight)
    {
        _pageTops = pageTops;
        _pageWidths = pageWidths;
        _pageHeights = pageHeights;
        PageSpacing = pageSpacing;
        TotalWidth = totalWidth;
        TotalHeight = totalHeight;
    }

    /// <summary>総ページ数</summary>
    public int Count => _pageTops.Length;

    /// <summary>ページ間余白（DIP）</summary>
    public double PageSpacing { get; }

    /// <summary>全ページ中の最大幅（DIP）</summary>
    public double TotalWidth { get; }

    /// <summary>全ページおよびページ間余白を含む合計論理高さ（DIP）</summary>
    public double TotalHeight { get; }

    /// <summary>
    /// ページ寸法リストから連続レイアウト計算インスタンスを構築します。
    /// </summary>
    public static ContinuousPageLayoutCalculator Build(
        IReadOnlyList<PageSizeMetric> pageSizes,
        double pageSpacing = 30.0)
    {
        if (pageSizes == null || pageSizes.Count == 0)
        {
            return new ContinuousPageLayoutCalculator(
                Array.Empty<double>(),
                Array.Empty<double>(),
                Array.Empty<double>(),
                Math.Max(0.0, pageSpacing),
                0.0,
                0.0);
        }

        int count = pageSizes.Count;
        double spacing = Math.Max(0.0, pageSpacing);
        var tops = new double[count];
        var widths = new double[count];
        var heights = new double[count];

        double currentY = 0.0;
        double maxWidth = 0.0;

        for (int i = 0; i < count; i++)
        {
            double w = SanitizeDimension(pageSizes[i].Width, DefaultPageWidth);
            double h = SanitizeDimension(pageSizes[i].Height, DefaultPageHeight);

            tops[i] = currentY;
            widths[i] = w;
            heights[i] = h;

            if (w > maxWidth)
            {
                maxWidth = w;
            }

            currentY += h + (i < count - 1 ? spacing : 0.0);
        }

        return new ContinuousPageLayoutCalculator(tops, widths, heights, spacing, maxWidth, currentY);
    }

    /// <summary>
    /// ページアイテムViewModelリストから連続レイアウト計算インスタンスを構築します。
    /// </summary>
    public static ContinuousPageLayoutCalculator Build(
        IReadOnlyList<DetailPageItemViewModel> pages,
        double pageSpacing = 30.0)
    {
        if (pages == null || pages.Count == 0)
        {
            return Build(Array.Empty<PageSizeMetric>(), pageSpacing);
        }

        var metrics = new PageSizeMetric[pages.Count];
        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i].Page;
            metrics[i] = new PageSizeMetric(page.DisplayWidth, page.DisplayHeight);
        }

        return Build(metrics, pageSpacing);
    }

    /// <summary>指定インデックスのページ上端論理Y座標を取得します。</summary>
    public double GetPageTop(int index) => _pageTops[index];

    /// <summary>指定インデックスのページ論理幅を取得します。</summary>
    public double GetPageWidth(int index) => _pageWidths[index];

    /// <summary>指定インデックスのページ論理高さを取得します。</summary>
    public double GetPageHeight(int index) => _pageHeights[index];

    /// <summary>指定インデックスのページ下端論理Y座標を取得します。</summary>
    public double GetPageBottom(int index) => _pageTops[index] + _pageHeights[index];

    /// <summary>
    /// 現在のスクロール位置・ビューポート高さ・ズーム倍率に基づき、実体化すべきページインデックス範囲を二分探索で算出します。
    /// </summary>
    public (int FirstIndex, int LastIndex) CalculateRealizationRange(
        double verticalOffset,
        double viewportHeight,
        double zoom,
        double topMargin = 20.0,
        double bottomMargin = 62.0,
        double bufferViewports = 1.0,
        int minBufferPages = 2)
    {
        if (Count == 0)
        {
            return (-1, -1);
        }

        double effectiveZoom = zoom > 0.001 ? zoom : 1.0;
        int safeMinBuffer = Math.Max(0, minBufferPages);

        if (viewportHeight <= 0)
        {
            int fallbackLast = Math.Min(Count - 1, Math.Max(3, safeMinBuffer * 2));
            return (0, fallbackLast);
        }

        double contentTotalHeight = TotalHeight * effectiveZoom + topMargin + bottomMargin;
        if (contentTotalHeight <= viewportHeight)
        {
            return (0, Count - 1);
        }

        double bufferPx = viewportHeight * Math.Max(0.0, bufferViewports);
        double visTopLogical = Math.Max(0.0, (verticalOffset - bufferPx - topMargin) / effectiveZoom);
        double visBottomLogical = Math.Max(0.0, (verticalOffset + viewportHeight + bufferPx - topMargin) / effectiveZoom);

        int first = FindFirstPageEndingAtOrAfter(visTopLogical);
        int last = FindLastPageStartingAtOrBefore(visBottomLogical);
        if (last < first)
        {
            last = first;
        }

        first = Math.Max(0, first - safeMinBuffer);
        last = Math.Min(Count - 1, last + safeMinBuffer);
        return (first, last);
    }

    /// <summary>
    /// 指定ページへジャンプする際の目標垂直スクロールオフセット（px）を算出します。
    /// </summary>
    public double GetPageScrollOffset(int pageIndex, double zoom, double topMargin = 20.0)
    {
        if (Count == 0 || pageIndex <= 0)
        {
            return 0.0;
        }

        int clamped = Math.Clamp(pageIndex, 0, Count - 1);
        double effectiveZoom = zoom > 0.001 ? zoom : 1.0;
        return topMargin + _pageTops[clamped] * effectiveZoom;
    }

    /// <summary>
    /// 指定ページのビューポート相対垂直境界を O(1) で算出します。
    /// </summary>
    public PageBounds? GetViewportRelativeBounds(
        int pageIndex,
        double zoom,
        double verticalOffset,
        double viewportHeight,
        double topMargin = 20.0,
        double bottomMargin = 62.0)
    {
        if ((uint)pageIndex >= (uint)Count)
        {
            return null;
        }

        double effectiveZoom = zoom > 0.001 ? zoom : 1.0;
        double contentTotalHeight = TotalHeight * effectiveZoom + topMargin + bottomMargin;
        double contentOriginY = (viewportHeight > 0 && contentTotalHeight < viewportHeight)
            ? ((viewportHeight - contentTotalHeight) / 2.0 + topMargin)
            : (topMargin - verticalOffset);

        double top = contentOriginY + _pageTops[pageIndex] * effectiveZoom;
        double bottom = top + _pageHeights[pageIndex] * effectiveZoom;
        return new PageBounds(top, bottom);
    }

    private int FindFirstPageEndingAtOrAfter(double targetY)
    {
        int low = 0;
        int high = _pageTops.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (GetPageBottom(mid) < targetY)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return Math.Clamp(low, 0, _pageTops.Length - 1);
    }

    private int FindLastPageStartingAtOrBefore(double targetY)
    {
        int low = 0;
        int high = _pageTops.Length - 1;
        int ans = 0;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (_pageTops[mid] <= targetY)
            {
                ans = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return ans;
    }

    private static double SanitizeDimension(double value, double fallback)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : fallback;
    }
}
