#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PDFBinder.App.Helpers;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Controls;

/// <summary>
/// グリッドビューのサムネイル一覧において、表示範囲＋上下バッファ行のサムネイルカードのみを
/// 実体化・リサイクルする専用仮想化折り返しパネル。
/// </summary>
public class VirtualizingThumbnailWrapPanel : VirtualizingPanelBase
{
    public static readonly DependencyProperty ThumbnailSizeProperty = DependencyProperty.Register(
        nameof(ThumbnailSize),
        typeof(double),
        typeof(VirtualizingThumbnailWrapPanel),
        new FrameworkPropertyMetadata(220.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>サムネイル画像の基準幅（DIP）</summary>
    public double ThumbnailSize
    {
        get => (double)GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    public static readonly DependencyProperty CardChromeWidthProperty = DependencyProperty.Register(
        nameof(CardChromeWidth),
        typeof(double),
        typeof(VirtualizingThumbnailWrapPanel),
        new FrameworkPropertyMetadata(ThumbnailGridLayoutCalculator.DefaultCardChromeWidth, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>カード外枠の水平方向装飾幅（Margin + BorderThickness）</summary>
    public double CardChromeWidth
    {
        get => (double)GetValue(CardChromeWidthProperty);
        set => SetValue(CardChromeWidthProperty, value);
    }

    public static readonly DependencyProperty CardChromeHeightProperty = DependencyProperty.Register(
        nameof(CardChromeHeight),
        typeof(double),
        typeof(VirtualizingThumbnailWrapPanel),
        new FrameworkPropertyMetadata(ThumbnailGridLayoutCalculator.DefaultCardChromeHeight, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>カード外枠の垂直方向装飾高さ（Margin + BorderThickness）</summary>
    public double CardChromeHeight
    {
        get => (double)GetValue(CardChromeHeightProperty);
        set => SetValue(CardChromeHeightProperty, value);
    }

    private ThumbnailGridLayoutCalculator _layout = ThumbnailGridLayoutCalculator.Build(
        Array.Empty<PageSizeMetric>(), double.PositiveInfinity, 220.0);

    private readonly HashSet<PdfPageModel> _subscribedPages = new();
    private bool _subscriptionsDirty = true;

    /// <summary>最新のサムネイルグリッドレイアウト計算結果を取得します。</summary>
    public ThumbnailGridLayoutCalculator CurrentLayout => _layout;

    /// <summary>
    /// 仮想化により未実体化のページも含め、指定ビジュアル基準の全アイテム境界座標を構築します。
    /// </summary>
    public List<GridItemBounds> BuildAllItemBounds(UIElement relativeTo)
    {
        Vector offset = default;
        if (IsLoaded &&
            PresentationSource.FromVisual(this) != null &&
            PresentationSource.FromVisual(relativeTo) != null)
        {
            Point origin = TranslatePoint(new Point(0, 0), relativeTo);
            offset = new Vector(origin.X, origin.Y);
        }

        return _layout.BuildItemBounds(offset);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureScrollViewerAttached();

        var itemsControl = ItemsControl.GetItemsOwner(this);
        if (_subscriptionsDirty)
        {
            SyncPageSubscriptions(itemsControl);
        }

        double effectiveWidth = ResolveAvailableWidth(availableSize.Width);
        _layout = BuildLayoutFromItems(itemsControl, effectiveWidth);

        if (_layout.Count == 0)
        {
            CleanUpAllItems();
            return new Size(0, 0);
        }

        var (first, last) = ComputeTargetRange();
        RealizeAndMeasureRange(first, last, i => _layout.GetItemRect(i).Size);

        return new Size(_layout.TotalWidth, _layout.TotalHeight);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        UIElementCollection children = InternalChildren;
        for (int i = 0; i < children.Count; i++)
        {
            int itemIndex = GetItemIndexForChild(i);
            if ((uint)itemIndex >= (uint)_layout.Count)
            {
                continue;
            }

            children[i].Arrange(_layout.GetItemRect(itemIndex));
        }

        return finalSize;
    }

    /// <inheritdoc/>
    protected override bool ShouldInvalidateMeasureOnScroll(ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ViewportWidthChange) >= 0.5)
        {
            return true;
        }

        if (_layout.Count == 0)
        {
            return false;
        }

        var (first, last) = ComputeTargetRange();
        return first != RealizedFirstIndex || last != RealizedLastIndex;
    }

    /// <inheritdoc/>
    protected override void OnItemsCollectionMutated()
    {
        _subscriptionsDirty = true;
    }

    /// <inheritdoc/>
    protected override void OnPanelUnloadedCore()
    {
        ClearPageSubscriptions();
        _subscriptionsDirty = true;
    }

    private (int FirstIndex, int LastIndex) ComputeTargetRange()
    {
        double verticalOffset = 0.0;
        double viewportHeight = 0.0;

        if (AttachedScrollViewer != null)
        {
            verticalOffset = Math.Max(0.0, AttachedScrollViewer.VerticalOffset);
            viewportHeight = AttachedScrollViewer.ViewportHeight;
        }

        double bufferHeight = Math.Max(300.0, viewportHeight * 0.75);
        return _layout.CalculateRealizationRange(verticalOffset, viewportHeight, bufferHeight, minBufferRows: 1);
    }

    private double ResolveAvailableWidth(double availableWidth)
    {
        if (!double.IsInfinity(availableWidth) && !double.IsNaN(availableWidth) && availableWidth > 0)
        {
            return availableWidth;
        }

        if (AttachedScrollViewer != null && AttachedScrollViewer.ViewportWidth > 0)
        {
            return Math.Max(1.0, AttachedScrollViewer.ViewportWidth);
        }

        return double.PositiveInfinity;
    }

    private ThumbnailGridLayoutCalculator BuildLayoutFromItems(ItemsControl? itemsControl, double availableWidth)
    {
        if (itemsControl == null || itemsControl.Items.Count == 0)
        {
            return ThumbnailGridLayoutCalculator.Build(
                Array.Empty<PageSizeMetric>(), availableWidth, ThumbnailSize, CardChromeWidth, CardChromeHeight);
        }

        int count = itemsControl.Items.Count;
        var metrics = new PageSizeMetric[count];
        for (int i = 0; i < count; i++)
        {
            if (itemsControl.Items[i] is PdfPageModel page)
            {
                metrics[i] = new PageSizeMetric(page.DisplayWidth, page.DisplayHeight);
            }
            else
            {
                metrics[i] = new PageSizeMetric(
                    ThumbnailGridLayoutCalculator.DefaultPageWidth,
                    ThumbnailGridLayoutCalculator.DefaultPageHeight);
            }
        }

        return ThumbnailGridLayoutCalculator.Build(
            metrics, availableWidth, ThumbnailSize, CardChromeWidth, CardChromeHeight);
    }

    private void SyncPageSubscriptions(ItemsControl? itemsControl)
    {
        var desired = new HashSet<PdfPageModel>();
        if (itemsControl != null)
        {
            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                if (itemsControl.Items[i] is PdfPageModel page)
                {
                    desired.Add(page);
                }
            }
        }

        var toRemove = new List<PdfPageModel>();
        foreach (var existing in _subscribedPages)
        {
            if (!desired.Contains(existing))
            {
                existing.PropertyChanged -= OnPageModelPropertyChanged;
                toRemove.Add(existing);
            }
        }
        foreach (var item in toRemove)
        {
            _subscribedPages.Remove(item);
        }

        foreach (var page in desired)
        {
            if (_subscribedPages.Add(page))
            {
                page.PropertyChanged += OnPageModelPropertyChanged;
            }
        }

        _subscriptionsDirty = false;
    }

    private void ClearPageSubscriptions()
    {
        foreach (var page in _subscribedPages)
        {
            page.PropertyChanged -= OnPageModelPropertyChanged;
        }
        _subscribedPages.Clear();
    }

    private void OnPageModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PdfPageModel.DisplayWidth) or nameof(PdfPageModel.DisplayHeight))
        {
            InvalidateMeasure();
        }
    }
}
