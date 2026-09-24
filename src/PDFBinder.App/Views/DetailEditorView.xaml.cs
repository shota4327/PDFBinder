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

        if (ViewModel != null)
        {
            ViewModel.VisiblePagesProvider = GetVisiblePagesInViewport;
        }
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
            oldVm.VisiblePagesProvider = null;
        }

        if (e.NewValue is DetailEditorViewModel newVm)
        {
            newVm.ScrollToPageRequested += OnScrollToPageRequested;
            newVm.VisiblePagesProvider = GetVisiblePagesInViewport;
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

        // スクロール停止時に新しく可視領域に入ったページをデバウンス動的レンダリング
        ViewModel.ScheduleContinuousScrollRender();
    }

    /// <summary>
    /// 連続スクロール表示時に現在スクロールビューアの表示領域内に交差している可視ページ一覧を取得します。
    /// </summary>
    private IEnumerable<DetailPageItemViewModel> GetVisiblePagesInViewport()
    {
        if (ViewModel == null || DetailScrollViewer.ViewportHeight <= 0)
        {
            return Enumerable.Empty<DetailPageItemViewModel>();
        }

        var visible = new List<DetailPageItemViewModel>();
        double viewportHeight = DetailScrollViewer.ViewportHeight;

        foreach (var itemVm in ViewModel.Pages)
        {
            if (PagesItemsControl.ItemContainerGenerator.ContainerFromItem(itemVm) is FrameworkElement container)
            {
                var transform = container.TransformToVisual(DetailScrollViewer);
                Point pt = transform.Transform(new Point(0, 0));
                // コンテナがビューポート範囲 [0, viewportHeight] と交差しているかを判定
                if (pt.Y + container.ActualHeight >= 0 && pt.Y <= viewportHeight)
                {
                    visible.Add(itemVm);
                }
            }
        }

        return visible;
    }

    private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel == null) return;

        // Ctrlキー押下時は表示モードを問わずズームイン・ズームアウトを実行
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            HandleZoomWheel(e);
            return;
        }

        // Shiftキー押下時は表示モードを問わず横スクロールを実行
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            HandleHorizontalScrollWheel(e);
            return;
        }

        if (ViewModel.PageViewMode == DetailPageViewMode.SinglePage)
        {
            HandleSinglePageWheelTurn(e);
        }
    }

    /// <summary>
    /// Ctrl + マウスホイールによるズームイン・ズームアウト処理
    /// </summary>
    private void HandleZoomWheel(MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
        {
            ViewModel?.ZoomInCommand.Execute(null);
        }
        else if (e.Delta < 0)
        {
            ViewModel?.ZoomOutCommand.Execute(null);
        }
        e.Handled = true;
    }

    /// <summary>
    /// Shift + マウスホイールによる水平スクロール処理
    /// </summary>
    private void HandleHorizontalScrollWheel(MouseWheelEventArgs e)
    {
        if (DetailScrollViewer.ScrollableWidth > 0)
        {
            // 標準的な1ノッチ(Delta 120)あたり48pxスクロール
            double scrollDelta = -(e.Delta / 120.0) * 48.0;
            double targetOffset = Math.Clamp(DetailScrollViewer.HorizontalOffset + scrollDelta, 0.0, DetailScrollViewer.ScrollableWidth);
            DetailScrollViewer.ScrollToHorizontalOffset(targetOffset);
        }
        e.Handled = true;
    }

    /// <summary>
    /// 単一ページ表示時におけるマウスホイール端到達でのページ送り処理
    /// </summary>
    private void HandleSinglePageWheelTurn(MouseWheelEventArgs e)
    {
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
