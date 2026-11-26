#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PDFBinder.App.Helpers;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Controls;

/// <summary>
/// 詳細ビューの連続スクロール表示において、表示範囲周辺のページコンテナのみを実体化する専用仮想化スタックパネル。
/// </summary>
public class VirtualizingPageStackPanel : VirtualizingPanelBase
{
    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing),
        typeof(double),
        typeof(VirtualizingPageStackPanel),
        new FrameworkPropertyMetadata(30.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>ページ間の垂直余白（論理DIP）</summary>
    public double PageSpacing
    {
        get => (double)GetValue(PageSpacingProperty);
        set => SetValue(PageSpacingProperty, value);
    }

    public static readonly DependencyProperty TopMarginProperty = DependencyProperty.Register(
        nameof(TopMargin),
        typeof(double),
        typeof(VirtualizingPageStackPanel),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>親コンテナの上端マージン（ScrollViewer基準のオフセット補正用）</summary>
    public double TopMargin
    {
        get => (double)GetValue(TopMarginProperty);
        set => SetValue(TopMarginProperty, value);
    }

    public static readonly DependencyProperty BottomMarginProperty = DependencyProperty.Register(
        nameof(BottomMargin),
        typeof(double),
        typeof(VirtualizingPageStackPanel),
        new FrameworkPropertyMetadata(62.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>親コンテナの下端マージン（ステータスバー回避オフセット補正用）</summary>
    public double BottomMargin
    {
        get => (double)GetValue(BottomMarginProperty);
        set => SetValue(BottomMarginProperty, value);
    }

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(VirtualizingPageStackPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>親要素に適用されている表示ズーム倍率</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    private ContinuousPageLayoutCalculator _layout = ContinuousPageLayoutCalculator.Build(Array.Empty<PageSizeMetric>());

    /// <summary>最新の連続ページレイアウト計算結果を取得します。</summary>
    public ContinuousPageLayoutCalculator CurrentLayout => _layout;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureScrollViewerAttached();

        var itemsControl = ItemsControl.GetItemsOwner(this);
        _layout = BuildLayoutFromItems(itemsControl);

        if (_layout.Count == 0)
        {
            CleanUpAllItems();
            return new Size(0, 0);
        }

        var (first, last) = ComputeTargetRange();
        RealizeAndMeasureRange(first, last, i => new Size(_layout.GetPageWidth(i), _layout.GetPageHeight(i)));

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

            double w = _layout.GetPageWidth(itemIndex);
            double h = _layout.GetPageHeight(itemIndex);
            double x = Math.Max(0.0, (finalSize.Width - w) / 2.0);
            double y = _layout.GetPageTop(itemIndex);

            children[i].Arrange(new Rect(x, y, w, h));
        }

        return finalSize;
    }

    /// <inheritdoc/>
    protected override bool ShouldInvalidateMeasureOnScroll(ScrollChangedEventArgs e)
    {
        if (_layout.Count == 0)
        {
            return false;
        }

        var (first, last) = ComputeTargetRange();
        return first != RealizedFirstIndex || last != RealizedLastIndex;
    }

    private (int FirstIndex, int LastIndex) ComputeTargetRange()
    {
        double verticalOffset = AttachedScrollViewer?.VerticalOffset ?? 0.0;
        double viewportHeight = AttachedScrollViewer?.ViewportHeight ?? 0.0;

        return _layout.CalculateRealizationRange(
            verticalOffset,
            viewportHeight,
            Zoom,
            TopMargin,
            BottomMargin,
            bufferViewports: 1.0,
            minBufferPages: 2);
    }

    private ContinuousPageLayoutCalculator BuildLayoutFromItems(ItemsControl? itemsControl)
    {
        if (itemsControl == null || itemsControl.Items.Count == 0)
        {
            return ContinuousPageLayoutCalculator.Build(Array.Empty<PageSizeMetric>(), PageSpacing);
        }

        int count = itemsControl.Items.Count;
        var metrics = new PageSizeMetric[count];
        for (int i = 0; i < count; i++)
        {
            metrics[i] = ExtractPageMetric(itemsControl.Items[i]);
        }

        return ContinuousPageLayoutCalculator.Build(metrics, PageSpacing);
    }

    private static PageSizeMetric ExtractPageMetric(object? item)
    {
        if (item is DetailSpreadRowViewModel spreadRow)
        {
            return new PageSizeMetric(spreadRow.DisplayWidth, spreadRow.DisplayHeight);
        }

        if (item is DetailPageItemViewModel pageVm)
        {
            return new PageSizeMetric(pageVm.Page.DisplayWidth, pageVm.Page.DisplayHeight);
        }

        if (item is PdfPageModel pageModel)
        {
            return new PageSizeMetric(pageModel.DisplayWidth, pageModel.DisplayHeight);
        }

        return new PageSizeMetric(
            ContinuousPageLayoutCalculator.DefaultPageWidth,
            ContinuousPageLayoutCalculator.DefaultPageHeight);
    }
}
