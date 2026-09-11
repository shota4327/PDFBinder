using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Views;

/// <summary>
/// 手書き編集を行う詳細ビューのコードビハインド
/// </summary>
public partial class DetailEditorView : UserControl
{
    public DetailEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        }
    }

    private void OnScrollToPageRequested(PdfPageModel page)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var itemVm = ViewModel?.Pages.FirstOrDefault(p => p.Page == page);
            if (itemVm != null)
            {
                var container = PagesItemsControl.ItemContainerGenerator.ContainerFromItem(itemVm) as FrameworkElement;
                container?.BringIntoView();
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
        if (ViewModel == null || ViewModel.Pages.Count == 0) return;

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
}
