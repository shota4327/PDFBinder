using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.Helpers;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Views;

/// <summary>
/// 手書き編集を行う詳細ビューのコードビハインド
/// </summary>
public partial class DetailEditorView : UserControl
{
    private readonly WheelPageTurnTracker _wheelTracker = new();

    public DetailEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        DetailScrollViewer.SizeChanged += OnDetailScrollViewerSizeChanged;
        DetailScrollViewer.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnRequestBringIntoView), true);
        PagesItemsControl.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnRequestBringIntoView), true);
        SinglePageContainer.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnRequestBringIntoView), true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateViewportToViewModel();
    }

    private void OnDetailScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportToViewModel();
    }

    private void UpdateViewportToViewModel()
    {
        if (ViewModel != null && DetailScrollViewer.ActualWidth > 0 && DetailScrollViewer.ActualHeight > 0)
        {
            ViewModel.UpdateViewportSize(DetailScrollViewer.ActualWidth, DetailScrollViewer.ActualHeight);
        }
    }

    /// <summary>
    /// 子要素（InkCanvasやBorder等）のフォーカス取得やクリック時に発生する自動スクロールを完全に抑止します。
    /// </summary>
    private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private DetailEditorViewModel? ViewModel => DataContext as DetailEditorViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DetailEditorViewModel oldVm)
        {
            oldVm.ScrollToPageRequested -= OnScrollToPageRequested;
        }

        if (e.NewValue is DetailEditorViewModel newVm)
        {
            newVm.ScrollToPageRequested += OnScrollToPageRequested;
            UpdateViewportToViewModel();
        }
    }

    private void OnScrollToPageRequested(PdfPageModel page)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (ViewModel?.PageViewMode == DetailPageViewMode.SinglePage)
            {
                DetailScrollViewer.ScrollToTop();
                return;
            }

            var itemVm = ViewModel?.Pages.FirstOrDefault(p => p.Page == page);
            if (itemVm != null && PagesItemsControl.ItemContainerGenerator.ContainerFromItem(itemVm) is FrameworkElement container)
            {
                var transform = container.TransformToVisual(DetailScrollViewer);
                Point pt = transform.Transform(new Point(0, 0));
                double targetOffset = DetailScrollViewer.VerticalOffset + pt.Y - DetailScrollViewer.Padding.Top;
                DetailScrollViewer.ScrollToVerticalOffset(Math.Max(0.0, targetOffset));
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnPagePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DetailPageItemViewModel itemVm && ViewModel != null)
        {
            ViewModel.CurrentPage = itemVm.Page;
            foreach (var p in ViewModel.Pages)
            {
                p.IsCurrent = (p == itemVm);
            }
        }
    }

    private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 連続表示時のみ、スクロール位置に応じた現在ページ判定を実施
        if (ViewModel == null || ViewModel.Pages.Count == 0 || ViewModel.PageViewMode != DetailPageViewMode.Continuous) return;

        double targetCenterY = DetailScrollViewer.ViewportHeight / 2.0;
        DetailPageItemViewModel? bestMatch = null;
        double minDistance = double.MaxValue;

        foreach (var itemVm in ViewModel.Pages)
        {
            if (PagesItemsControl.ItemContainerGenerator.ContainerFromItem(itemVm) is FrameworkElement container)
            {
                var transform = container.TransformToVisual(DetailScrollViewer);
                Point pt = transform.Transform(new Point(0, container.ActualHeight / 2.0));
                double dist = Math.Abs(pt.Y - targetCenterY);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestMatch = itemVm;
                }
            }
        }

        if (bestMatch != null && ViewModel.CurrentPage != bestMatch.Page)
        {
            ViewModel.CurrentPage = bestMatch.Page;
            foreach (var p in ViewModel.Pages)
            {
                p.IsCurrent = (p == bestMatch);
            }
        }
    }

    private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel == null || ViewModel.PageViewMode != DetailPageViewMode.SinglePage) return;

        double scrollableHeight = DetailScrollViewer.ScrollableHeight;
        bool isFitInView = scrollableHeight <= 1.0;
        bool isAtTop = DetailScrollViewer.VerticalOffset <= 1.0;
        bool isAtBottom = DetailScrollViewer.VerticalOffset >= scrollableHeight - 1.0;

        bool isAtEdge = (e.Delta > 0 && (isFitInView || isAtTop)) ||
                        (e.Delta < 0 && (isFitInView || isAtBottom));

        int pageTurns = _wheelTracker.ProcessScroll(e.Delta, DateTime.UtcNow, isAtEdge);
        if (pageTurns == 0)
        {
            if (isAtEdge)
            {
                // 境界上で端数Deltaを蓄積中の場合は、親要素やビューポートの不要な揺れを防ぐためイベントを消費
                e.Handled = true;
            }
            return;
        }

        e.Handled = true;
        ExecutePageTurns(pageTurns, isFitInView, isAtTop);
    }

    private void ExecutePageTurns(int pageTurns, bool isFitInView, bool isAtTop)
    {
        if (pageTurns > 0)
        {
            for (int i = 0; i < pageTurns; i++)
            {
                if (!ViewModel!.CanGoToPreviousPage)
                {
                    _wheelTracker.Reset();
                    break;
                }
                ViewModel.GoToPreviousPageCommand.Execute(null);
            }

            Action? postScroll = isAtTop && !isFitInView ? () => DetailScrollViewer.ScrollToBottom() : () => DetailScrollViewer.ScrollToTop();
            Dispatcher.InvokeAsync(postScroll, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else if (pageTurns < 0)
        {
            int count = -pageTurns;
            for (int i = 0; i < count; i++)
            {
                if (!ViewModel!.CanGoToNextPage)
                {
                    _wheelTracker.Reset();
                    break;
                }
                ViewModel.GoToNextPageCommand.Execute(null);
            }

            Dispatcher.InvokeAsync(() => DetailScrollViewer.ScrollToTop(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
