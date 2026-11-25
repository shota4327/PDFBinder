#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;

namespace PDFBinder.App.Helpers;

/// <summary>
/// グリッドアイテムのインデックスおよびホスト座標系における境界矩形を表します。
/// </summary>
/// <param name="Index">ページコレクション内の 0 始まりインデックス</param>
/// <param name="Bounds">ホスト座標系における境界矩形</param>
public readonly record struct GridItemBounds(int Index, Rect Bounds);

/// <summary>
/// WrapPanel 内の 1 水平行を構成するアイテム範囲と垂直境界を表します。
/// </summary>
/// <param name="StartOffset">アイテム配列内での開始オフセット</param>
/// <param name="Count">行に含まれるアイテム数</param>
/// <param name="Top">行の上端 Y 座標</param>
/// <param name="Bottom">行の下端 Y 座標</param>
public readonly record struct GridRowSpan(int StartOffset, int Count, double Top, double Bottom);

/// <summary>
/// グリッドレイアウトキャッシュの有効性を判定するためのシグネチャ情報。
/// </summary>
/// <param name="ItemCount">ページ総数</param>
/// <param name="PanelWidth">グリッドパネルの実際の幅</param>
/// <param name="PanelHeight">グリッドパネルの実際の高さ</param>
/// <param name="ThumbnailSize">サムネイル基準幅</param>
public readonly record struct GridLayoutSignature(
    int ItemCount,
    double PanelWidth,
    double PanelHeight,
    double ThumbnailSize);

/// <summary>
/// グリッドビュー上の各サムネイル境界矩形を行単位インデックスとしてキャッシュし、
/// ドラッグ時の挿入位置判定およびラバーバンド矩形との交差判定を高速化するヘルパークラス。
/// </summary>
public sealed class GridLayoutCache
{
    /// <summary>
    /// マウスY座標と行の上下端との許容近接距離（px）
    /// </summary>
    public const double RowMatchTolerance = 8.0;

    private const double RowBreakEpsilon = 1.0;
    private const double IndicatorBarWidth = 4.0;

    /// <summary>
    /// ページが空またはコンテナ未生成時に使用するフォールバック挿入バー矩形
    /// </summary>
    public static readonly Rect EmptyDocumentBarRect = new(10, 10, IndicatorBarWidth, 150);

    private readonly GridItemBounds[] _items;
    private readonly GridRowSpan[] _rows;

    private GridLayoutCache(
        GridItemBounds[] items,
        GridRowSpan[] rows,
        GridLayoutSignature signature)
    {
        _items = items;
        _rows = rows;
        Signature = signature;
    }

    /// <summary>
    /// このキャッシュ構築時のレイアウトシグネチャを取得します。
    /// </summary>
    public GridLayoutSignature Signature { get; }

    /// <summary>
    /// キャッシュされているアイテム境界一覧（行内は左端昇順）を取得します。
    /// </summary>
    public IReadOnlyList<GridItemBounds> Items => _items;

    /// <summary>
    /// 構築された行メタデータ一覧（上端昇順）を取得します。
    /// </summary>
    public IReadOnlyList<GridRowSpan> Rows => _rows;

    /// <summary>
    /// アイテム境界リストから行インデックス付きのレイアウトキャッシュを構築します。
    /// </summary>
    /// <param name="items">ホスト座標系で収集されたアイテム境界一覧</param>
    /// <param name="signature">キャッシュ有効性検証用のレイアウトシグネチャ</param>
    /// <returns>構築されたキャッシュインスタンス</returns>
    public static GridLayoutCache Build(
        IReadOnlyList<GridItemBounds> items,
        GridLayoutSignature signature)
    {
        if (items == null || items.Count == 0)
        {
            return new GridLayoutCache(
                Array.Empty<GridItemBounds>(),
                Array.Empty<GridRowSpan>(),
                signature);
        }

        var array = new GridItemBounds[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            array[i] = items[i];
        }

        var rows = BuildRows(array);
        return new GridLayoutCache(array, rows, signature);
    }

    /// <summary>
    /// キャッシュ済みレイアウトに対して、マウス座標からドロップ挿入先インデックスと表示バー矩形を算出します。
    /// </summary>
    /// <param name="mousePos">ホスト座標系におけるマウス座標</param>
    /// <param name="totalPageCount">ドキュメントの総ページ数</param>
    /// <returns>挿入先インデックスおよび挿入インジケーター矩形</returns>
    public (int TargetIndex, Rect BarRect) CalculateInsertionTarget(Point mousePos, int totalPageCount)
    {
        if (totalPageCount <= 0 || _items.Length == 0 || _rows.Length == 0)
        {
            return (0, EmptyDocumentBarRect);
        }

        int rowIndex = FindTargetRowIndex(mousePos.Y);
        if (rowIndex >= 0)
        {
            return CalculateRowInsertionTarget(_rows[rowIndex], mousePos.X);
        }

        if (mousePos.Y < _items[0].Bounds.Top)
        {
            return (0, CreateBarRectBefore(_items[0].Bounds, isFirstInRow: true));
        }

        return (totalPageCount, CreateBarRectAfter(_items[^1].Bounds));
    }

    /// <summary>
    /// 指定された選択矩形と交差するアイテムのインデックスを、割り当てなしで結果セットへ格納します。
    /// </summary>
    /// <param name="selectionRect">ホスト座標系における選択矩形</param>
    /// <param name="destination">交差したアイテムのインデックスを格納するセット</param>
    public void FindIntersectingIndices(Rect selectionRect, HashSet<int> destination)
    {
        destination.Clear();
        if (selectionRect.IsEmpty || _rows.Length == 0)
        {
            return;
        }

        int startRow = FindFirstRowEndingAtOrAfter(selectionRect.Top);
        for (int r = startRow; r < _rows.Length; r++)
        {
            var row = _rows[r];
            if (row.Top > selectionRect.Bottom)
            {
                break;
            }

            int endOffset = row.StartOffset + row.Count;
            for (int i = row.StartOffset; i < endOffset; i++)
            {
                var item = _items[i];
                if (item.Bounds.Left > selectionRect.Right)
                {
                    break;
                }

                if (item.Bounds.IntersectsWith(selectionRect))
                {
                    destination.Add(item.Index);
                }
            }
        }
    }

    /// <summary>
    /// 収集済みアイテム配列を WrapPanel の水平行単位に分割し、各行内の左端順を保証します。
    /// </summary>
    private static GridRowSpan[] BuildRows(GridItemBounds[] items)
    {
        var rows = new List<GridRowSpan>();
        int rowStart = 0;
        double rowTop = items[0].Bounds.Top;
        double rowBottom = items[0].Bounds.Bottom;
        double prevLeft = items[0].Bounds.Left;
        bool needsSort = false;

        for (int i = 1; i < items.Length; i++)
        {
            var b = items[i].Bounds;
            bool isNewRow = (b.Top >= rowBottom - RowBreakEpsilon && Math.Abs(b.Top - rowTop) >= RowBreakEpsilon)
                            || (b.Left < prevLeft && b.Top > rowTop);

            if (isNewRow)
            {
                FinalizeRow(items, rowStart, i - rowStart, rowTop, rowBottom, needsSort, rows);
                rowStart = i;
                rowTop = b.Top;
                rowBottom = b.Bottom;
                prevLeft = b.Left;
                needsSort = false;
                continue;
            }

            if (b.Left < prevLeft)
            {
                needsSort = true;
            }
            if (b.Top < rowTop) rowTop = b.Top;
            if (b.Bottom > rowBottom) rowBottom = b.Bottom;
            prevLeft = b.Left;
        }

        FinalizeRow(items, rowStart, items.Length - rowStart, rowTop, rowBottom, needsSort, rows);
        return rows.ToArray();
    }

    /// <summary>
    /// 1行分のアイテム群を必要に応じて左端順に整列し、行メタデータを追加します。
    /// </summary>
    private static void FinalizeRow(
        GridItemBounds[] items,
        int start,
        int count,
        double rowTop,
        double rowBottom,
        bool needsSort,
        List<GridRowSpan> rows)
    {
        if (needsSort && count > 1)
        {
            Array.Sort(items, start, count, LeftThenIndexComparer.Instance);
        }

        rows.Add(new GridRowSpan(start, count, rowTop, rowBottom));
    }

    /// <summary>
    /// 指定行内での水平方向位置から挿入先インデックスおよびバー矩形を算出します。
    /// </summary>
    private (int TargetIndex, Rect BarRect) CalculateRowInsertionTarget(GridRowSpan row, double mouseX)
    {
        int endOffset = row.StartOffset + row.Count;
        for (int i = row.StartOffset; i < endOffset; i++)
        {
            var item = _items[i];
            double midX = item.Bounds.Left + item.Bounds.Width / 2.0;
            if (mouseX < midX)
            {
                bool isFirstInRow = i == row.StartOffset;
                return (item.Index, CreateBarRectBefore(item.Bounds, isFirstInRow));
            }
        }

        var lastInRow = _items[endOffset - 1];
        return (lastInRow.Index + 1, CreateBarRectAfter(lastInRow.Bounds));
    }

    /// <summary>
    /// 二分探索を用いて、指定された Y 座標に対応する行インデックス（許容範囲含む）を特定します。
    /// </summary>
    private int FindTargetRowIndex(double mouseY)
    {
        int candidate = FindFirstRowEndingAtOrAfter(mouseY);
        if (candidate < _rows.Length && _rows[candidate].Top <= mouseY)
        {
            return candidate;
        }

        double distPrev = candidate > 0 ? mouseY - _rows[candidate - 1].Bottom : double.MaxValue;
        double distNext = candidate < _rows.Length ? _rows[candidate].Top - mouseY : double.MaxValue;

        bool prevInRange = distPrev <= RowMatchTolerance;
        bool nextInRange = distNext <= RowMatchTolerance;

        if (prevInRange && nextInRange)
        {
            return distPrev <= distNext ? candidate - 1 : candidate;
        }

        if (prevInRange) return candidate - 1;
        if (nextInRange) return candidate;
        return -1;
    }

    /// <summary>
    /// 下端が <paramref name="targetY"/> 以上となる最初の行インデックスを二分探索で返します。
    /// </summary>
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

        return low;
    }

    /// <summary>
    /// アイテム直前に表示する挿入インジケーター矩形を生成します。
    /// </summary>
    private static Rect CreateBarRectBefore(Rect bounds, bool isFirstInRow)
    {
        // 先頭アイテムの場合は左側余白に収まるよう配置
        double barX = isFirstInRow ? Math.Max(4, bounds.Left + 3) : bounds.Left - 2;
        double barY = bounds.Top + 10;
        double barHeight = Math.Max(20, bounds.Height - 20);
        return new Rect(barX, barY, IndicatorBarWidth, barHeight);
    }

    /// <summary>
    /// アイテム直後に表示する挿入インジケーター矩形を生成します。
    /// </summary>
    private static Rect CreateBarRectAfter(Rect bounds)
    {
        double barX = bounds.Right - 7;
        double barY = bounds.Top + 10;
        double barHeight = Math.Max(20, bounds.Height - 20);
        return new Rect(barX, barY, IndicatorBarWidth, barHeight);
    }

    private sealed class LeftThenIndexComparer : IComparer<GridItemBounds>
    {
        public static readonly LeftThenIndexComparer Instance = new();

        public int Compare(GridItemBounds x, GridItemBounds y)
        {
            int leftCmp = x.Bounds.Left.CompareTo(y.Bounds.Left);
            return leftCmp != 0 ? leftCmp : x.Index.CompareTo(y.Index);
        }
    }
}
