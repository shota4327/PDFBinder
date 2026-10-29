using System;
using System.Collections.Generic;

namespace PDFBinder.App.Helpers;

/// <summary>
/// ビューポート座標系における要素の垂直境界情報（上端および下端）を表します。
/// </summary>
/// <param name="Top">ビューポート上端からの相対Y座標（上端）</param>
/// <param name="Bottom">ビューポート上端からの相対Y座標（下端）</param>
public readonly record struct PageBounds(double Top, double Bottom)
{
    /// <summary>要素の垂直方向の高さ</summary>
    public double Height => Bottom - Top;

    /// <summary>要素の垂直方向の中心Y座標</summary>
    public double CenterY => (Top + Bottom) / 2.0;
}

/// <summary>
/// ビューポートと交差している可視アイテムのインデックスおよび境界情報。
/// </summary>
/// <param name="Index">コレクション内における 0 始まりのアイテムインデックス</param>
/// <param name="Bounds">アイテムの境界情報</param>
public readonly record struct VisibleItemBounds(int Index, PageBounds Bounds);

/// <summary>
/// 垂直スクロールパネル内のアイテム群に対する高速な可視判定およびカレントアイテム判定を提供するヘルパークラス。
/// 狭義単調増加する垂直配置の特性を活かし、O(log N) の二分探索で可視範囲を特定します。
/// </summary>
public static class ViewportLayoutHelper
{
    /// <summary>
    /// ビューポート高さとアイテム境界情報取得デリゲートを用いて、ビューポート内に交差しているアイテム群を O(log N) で取得します。
    /// </summary>
    /// <param name="totalCount">全アイテム数</param>
    /// <param name="viewportHeight">ビューポートの有効な高さ（px）</param>
    /// <param name="boundsProvider">指定インデックスのアイテム境界を取得するデリゲート（未生成時は null）</param>
    /// <returns>ビューポートと交差しているアイテムのインデックスと境界の一覧（インデックス昇順）</returns>
    public static List<VisibleItemBounds> FindVisibleItems(
        int totalCount,
        double viewportHeight,
        Func<int, PageBounds?> boundsProvider)
    {
        if (totalCount <= 0 || viewportHeight <= 0)
        {
            return new List<VisibleItemBounds>();
        }

        // 二分探索でビューポートと交差する代表アイテムを特定
        int hitIndex = BinarySearchVisibleItem(totalCount, viewportHeight, boundsProvider, out PageBounds hitBounds, out bool needsFallback);
        if (needsFallback)
        {
            return FindVisibleItemsLinear(totalCount, viewportHeight, boundsProvider);
        }

        if (hitIndex < 0)
        {
            return new List<VisibleItemBounds>();
        }

        return ExpandVisibleRange(hitIndex, hitBounds, totalCount, viewportHeight, boundsProvider);
    }

    /// <summary>
    /// 二分探索によりビューポートと交差するいずれか 1 つのアイテムインデックスを特定します。
    /// </summary>
    private static int BinarySearchVisibleItem(
        int totalCount,
        double viewportHeight,
        Func<int, PageBounds?> boundsProvider,
        out PageBounds hitBounds,
        out bool needsFallback)
    {
        hitBounds = default;
        needsFallback = false;

        int low = 0;
        int high = totalCount - 1;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            var bounds = boundsProvider(mid);
            if (!bounds.HasValue)
            {
                needsFallback = true;
                return -1;
            }

            var b = bounds.Value;
            if (b.Bottom < 0)
            {
                // ビューポートより完全に上にあるため下方を探索
                low = mid + 1;
            }
            else if (b.Top > viewportHeight)
            {
                // ビューポートより完全に下にあるため上方を探索
                high = mid - 1;
            }
            else
            {
                // ビューポートと交差するアイテムを発見
                hitBounds = b;
                return mid;
            }
        }

        return -1;
    }

    /// <summary>
    /// 特定した代表アイテムから前後へ走査を広げ、交差するすべての可視アイテムを昇順リストで構築します。
    /// </summary>
    private static List<VisibleItemBounds> ExpandVisibleRange(
        int hitIndex,
        PageBounds hitBounds,
        int totalCount,
        double viewportHeight,
        Func<int, PageBounds?> boundsProvider)
    {
        var result = new List<VisibleItemBounds>();
        var backwardItems = new List<VisibleItemBounds>();

        // 上方向へ走査（hitIndex より前）
        for (int i = hitIndex - 1; i >= 0; i--)
        {
            var bounds = boundsProvider(i);
            if (!bounds.HasValue || bounds.Value.Bottom < 0)
            {
                break;
            }
            if (bounds.Value.Top <= viewportHeight)
            {
                backwardItems.Add(new VisibleItemBounds(i, bounds.Value));
            }
        }

        for (int i = backwardItems.Count - 1; i >= 0; i--)
        {
            result.Add(backwardItems[i]);
        }

        // 代表アイテム自身を追加
        result.Add(new VisibleItemBounds(hitIndex, hitBounds));

        // 下方向へ走査（hitIndex より後）
        for (int i = hitIndex + 1; i < totalCount; i++)
        {
            var bounds = boundsProvider(i);
            if (!bounds.HasValue || bounds.Value.Top > viewportHeight)
            {
                break;
            }
            if (bounds.Value.Bottom >= 0)
            {
                result.Add(new VisibleItemBounds(i, bounds.Value));
            }
        }

        return result;
    }

    /// <summary>
    /// コンテナ未生成時等、二分探索が継続不能な場合の安全な線形走査フォールバック。
    /// </summary>
    private static List<VisibleItemBounds> FindVisibleItemsLinear(
        int totalCount,
        double viewportHeight,
        Func<int, PageBounds?> boundsProvider)
    {
        var result = new List<VisibleItemBounds>();
        for (int i = 0; i < totalCount; i++)
        {
            var bounds = boundsProvider(i);
            if (bounds.HasValue)
            {
                var b = bounds.Value;
                if (b.Bottom >= 0 && b.Top <= viewportHeight)
                {
                    result.Add(new VisibleItemBounds(i, b));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 可視アイテム群の中から、ビューポート中央位置に最も近いアイテムのインデックスを取得します。
    /// </summary>
    /// <param name="visibleItems">可視アイテム一覧</param>
    /// <param name="targetCenterY">判定対象とするビューポート中央のY座標</param>
    /// <returns>中央に最も近いアイテムのインデックス。可視アイテムが空の場合は null</returns>
    public static int? FindBestMatchingIndex(IReadOnlyList<VisibleItemBounds> visibleItems, double targetCenterY)
    {
        if (visibleItems == null || visibleItems.Count == 0)
        {
            return null;
        }

        int? bestIndex = null;
        double minDistance = double.MaxValue;

        foreach (var item in visibleItems)
        {
            double dist = Math.Abs(item.Bounds.CenterY - targetCenterY);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestIndex = item.Index;
            }
        }

        return bestIndex;
    }
}
