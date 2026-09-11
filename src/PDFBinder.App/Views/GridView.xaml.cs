using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Views;

/// <summary>
/// PDFページのサムネイル一覧とドラッグ＆ドロップ並び替えを行うビュー
/// </summary>
public partial class GridView : UserControl
{
    private Point? _dragStartPoint;
    private PdfPageModel? _draggedPage;

    public GridView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PdfPageModel page)
        {
            if (e.ClickCount == 2)
            {
                // ダブルクリックで手書きエディタを開く
                ViewModel?.OpenPageDetailCommand.Execute(page);
                e.Handled = true;
                return;
            }

            _dragStartPoint = e.GetPosition(this);
            _draggedPage = page;
        }
    }

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint.HasValue && _draggedPage != null && e.LeftButton == MouseButtonState.Pressed)
        {
            Point current = e.GetPosition(this);
            Vector diff = _dragStartPoint.Value - current;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var data = new DataObject("PdfPageModel", _draggedPage);
                DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);

                _dragStartPoint = null;
                _draggedPage = null;
            }
        }
    }

    private void OnCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = null;
        _draggedPage = null;
    }

    private void OnCardDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PdfPageModel"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnCardDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PdfPageModel") &&
            sender is FrameworkElement targetElem &&
            targetElem.DataContext is PdfPageModel targetPage &&
            ViewModel != null)
        {
            var sourcePage = e.Data.GetData("PdfPageModel") as PdfPageModel;
            if (sourcePage != null && sourcePage != targetPage)
            {
                int oldIdx = ViewModel.Document.Pages.IndexOf(sourcePage);
                int newIdx = ViewModel.Document.Pages.IndexOf(targetPage);
                ViewModel.MovePage(oldIdx, newIdx);
            }
            e.Handled = true;
        }
    }

    private void OnRotateCounterClockwiseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PdfPageModel page && ViewModel != null)
        {
            page.IsSelected = true;
            ViewModel.RotateCounterClockwiseCommand.Execute(null);
        }
    }

    private void OnRotateClockwiseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PdfPageModel page && ViewModel != null)
        {
            page.IsSelected = true;
            ViewModel.RotateClockwiseCommand.Execute(null);
        }
    }

    private void OnDeletePageClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PdfPageModel page && ViewModel != null)
        {
            page.IsSelected = true;
            ViewModel.DeleteSelectedPagesCommand.Execute(null);
        }
    }
}
