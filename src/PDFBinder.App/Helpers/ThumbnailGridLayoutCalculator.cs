#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Helpers;

/// <summary>
/// グリッドビューの折り返しレイアウトにおける 1 水平行の範囲と垂直境界を表します。
/// </summary>
/// <param name="StartIndex">先頭アイテムインデックス</param>
/// <param name="Count">行内のアイテム数</param>
/// <param name="Top">行の上端Y座標</param>
/// <param name="Bottom">行の下端Y座標</param>
public readonly record struct ThumbnailGridRow(int StartIndex, int Count, double Top, double Bottom);

/// <summary>
/// グリッドビューにおけるサムネイルの折り返し行配置、実体化範囲、および全ページ境界矩形を
/// コンテナ未生成のまま数値計算で算出するレイアウト計算ヘルパー。
/// </summary>
public sealed class ThumbnailGridLayoutCalculator
{
    /// <summary>A4標準幅（pt）</summary>
    public const double DefaultPageWidth = 595.28;

    /// <summary>A4標準高さ（pt）</summary>
    public const double DefaultPageHeight = 841.89;

    /// <summary>カード外枠の水平方向装飾幅（Margin 10×2 + BorderThickness 5×2）</summary>
    public const double DefaultCardChromeWidth = 30.0;

    /// <summary>カード外枠の垂直方向装飾高さ（Margin 10×2 + BorderThickness 5×2）</summary>
    public const double DefaultCardChromeHeight = 30.0;

    private readonly Rect[] _itemRects;
    private readonly ThumbnailGridRow[] _rows;

    private ThumbnailGridLayoutCalculator(
        Rect[] itemRects,
        ThumbnailGridRow[] rows,
        double totalWidth,
        double totalHeight)
    {
        _itemRects = itemRects;
        _rows = rows;
        TotalWidth = totalWidth;
        TotalHeight = totalHeight;
    }

    /// <summary>総アイテム数</summary>
    public int Count => _itemRects.Length;

    /// <summary>全行の中で最大の論理幅</summary>
    public double TotalWidth { get; }

    /// <summary>全行の合計論理高さ</summary>
    public double TotalHeight { get; }

    /// <summary>構築された行メタデータ一覧</summary>
    public IReadOnlyList<ThumbnailGridRow> Rows => _rows;

    /// <summary>
    /// サムネイル基準幅とページ表示寸法から、アスペクト比を維持したサムネイル画像の高さを算出します。
    /// </summary>
    public static double CalculateImageHeight(double thumbnailSize, double displayWidth, double displayHeight)
    {
        if (thumbnailSize <= 0 || double.IsNaN(thumbnailSize) || double.IsInfinity(thumbnailSize))
        {
            return 1.0;
        }

        double ratio = (displayWidth > 0 && displayHeight > 0 &&
                        !double.IsNaN(displayWidth) && !double.IsNaN(displayHeight) &&
                        !double.IsInfinity(displayWidth) && !double.IsInfinity(displayHeight))
            ? (displayHeight / displayWidth)
            : (DefaultPageHeight / DefaultPageWidth);

        return Math.Max(1.0, Math.Round(thumbnailSize * ratio));
    }

    /// <summary>
    /// ページ寸法リストと利用可能幅からグリッドレイアウト計算インスタンスを構築します。
    /// </summary>
    public static ThumbnailGridLayoutCalculator Build(
        IReadOnlyList<PageSizeMetric> pageSizes,
        double availableWidth,
        double thumbnailSize,
        double cardChromeWidth = DefaultCardChromeWidth,
        double cardChromeHeight = DefaultCardChromeHeight)
    {
        if (pageSizes == null || pageSizes.Count == 0)
        {
            return new ThumbnailGridLayoutCalculator(Array.Empty<Rect>(), Array.Empty<ThumbnailGridRow>(), 0.0, 0.0);
        }

        double maxRowAllowedWidth = (!double.IsNaN(availableWidth) && !double.IsInfinity(availableWidth) && availableWidth > 0)
            ? availableWidth
            : double.PositiveInfinity;

        double safeThumbWidth = thumbnailSize > 0 ? thumbnailSize : 220.0;
        double itemWidth = safeThumbWidth + Math.Max(0.0, cardChromeWidth);
        double chromeHeight = Math.Max(0.0, cardChromeHeight);

        var rects = new Rect[pageSizes.Count];
        var rows = new List<ThumbnailGridRow>();

        double x = 0.0;
        double y = 0.0;
        double rowHeight = 0.0;
        double maxWidth = 0.0;
        int rowStart = 0;

        for (int i = 0; i < pageSizes.Count; i++)
        {
            double itemHeight = CalculateImageHeight(safeThumbWidth, pageSizes[i].Width, pageSizes[i].Height) + chromeHeight;

            if (x > 0 && x + itemWidth > maxRowAllowedWidth)
            {
                rows.Add(new ThumbnailGridRow(rowStart, i - rowStart, y, y + rowHeight));
                x = 0.0;
                y += rowHeight;
                rowHeight = 0.0;
                rowStart = i;
            }

            rects[i] = new Rect(x, y, itemWidth, itemHeight);
            x += itemWidth;
            if (itemHeight > rowHeight) rowHeight = itemHeight;
            if (x > maxWidth) maxWidth = x;
        }

        rows.Add(new ThumbnailGridRow(rowStart, pageSizes.Count - rowStart, y, y + rowHeight));
        return new ThumbnailGridLayoutCalculator(rects, rows.ToArray(), maxWidth, y + rowHeight);
    }

    /// <summary>
    /// ページモデルリストと利用可能幅からグリッドレイアウト計算インスタンスを構築します。
    /// </summary>
    public static ThumbnailGridLayoutCalculator Build(
        IReadOnlyList<PdfPageModel> pages,
        double availableWidth,
        double thumbnailSize,
        double cardChromeWidth = DefaultCardChromeWidth,
        double cardChromeHeight = DefaultCardChromeHeight)
    {
        if (pages == null || pages.Count == 0)
        {
            return Build(Array.Empty<PageSizeMetric>(), availableWidth, thumbnailSize, cardChromeWidth, cardChromeHeight);
        }

        var metrics = new PageSizeMetric[pages.Count];
        for (int i = 0; i < pages.Count; i++)
        {
            metrics[i] = new PageSizeMetric(pages[i].DisplayWidth, pages[i].DisplayHeight);
        }

        return Build(metrics, availableWidth, thumbnailSize, cardChromeWidth, cardChromeHeight);
    }

    /// <summary>指定インデックスのアイテム配置矩形を取得します。</summary>
    public Rect GetItemRect(int index) => _itemRects[index];

    /// <summary>
    /// 現在のスクロール位置およびビューポート高さに基づき、実体化すべきアイテム範囲を二分探索で算出します。
    /// </summary>
    public (int FirstIndex, int LastIndex) CalculateRealizationRange(
        double verticalOffset,
        double viewportHeight,
        double bufferHeight = 400.0,
        int minBufferRows = 1)
    {
        if (Count == 0 || _rows.Length == 0)
        {
            return (-1, -1);
        }

        int safeMinRows = Math.Max(0, minBufferRows);
        if (viewportHeight <= 0)
        {
            int fallbackLastRow = Math.Min(_rows.Length - 1, Math.Max(2, safeMinRows * 2));
            var lastRowInfo = _rows[fallbackLastRow];
            return (0, lastRowInfo.StartIndex + lastRowInfo.Count - 1);
        }

        double effectiveBuffer = Math.Max(0.0, bufferHeight);
        double visTop = Math.Max(0.0, verticalOffset - effectiveBuffer);
        double visBottom = Math.Max(0.0, verticalOffset + viewportHeight + effectiveBuffer);

        int firstRow = FindFirstRowEndingAtOrAfter(visTop);
        int lastRow = FindLastRowStartingAtOrBefore(visBottom);
        if (lastRow < firstRow)
        {
            lastRow = firstRow;
        }

        firstRow = Math.Max(0, firstRow - safeMinRows);
        lastRow = Math.Min(_rows.Length - 1, lastRow + safeMinRows);

        int firstIndex = _rows[firstRow].StartIndex;
        int lastIndex = _rows[lastRow].StartIndex + _rows[lastRow].Count - 1;
        return (firstIndex, lastIndex);
    }

    /// <summary>
    /// 画面外の未実体化ページを含む全ページの境界情報を指定オフセット付きで構築します。
    /// </summary>
    public List<GridItemBounds> BuildItemBounds(Vector offset)
    {
        var result = new List<GridItemBounds>(_itemRects.Length);
        for (int i = 0; i < _itemRects.Length; i++)
        {
            var r = _itemRects[i];
            result.Add(new GridItemBounds(i, new Rect(r.X + offset.X, r.Y + offset.Y, r.Width, r.Height)));
        }

        return result;
    }

    private int FindFirstRowEndingAtOrAfter(double targetY)
    {
        int low = 0;
        int high = _rows.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (_rows[mid].Bottom < targetY)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return Math.Clamp(low, 0, _rows.Length - 1);
    }

    private int FindLastRowStartingAtOrBefore(double targetY)
    {
        int low = 0;
        int high = _rows.Length - 1;
        int ans = 0;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (_rows[mid].Top <= targetY)
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
}
