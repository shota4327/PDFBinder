using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Views;

/// <summary>
/// PDFページのサムネイル一覧、範囲選択、およびドラッグ＆ドロップ並び替え・挿入を行うグリッドビュー
/// </summary>
public partial class GridView : UserControl
{
    private Point? _dragStartPoint;
    private PdfPageModel? _draggedPage;
    private int _anchorPageIndex = -1;
    private DragAdorner? _dragAdorner;
    private readonly List<FrameworkElement> _draggedContainers = new();
    private int? _currentDropTargetIndex;

    private bool _isRubberBandActive;
    private Point _rubberBandStart;
    private HashSet<PdfPageModel> _initialSelection = new();

    public GridView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    #region サムネイルのマウス操作・選択処理

    /// <summary>
    /// サムネイル押下時の選択処理およびドラッグ準備を行います。
    /// </summary>
    private void OnPageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement elem || elem.DataContext is not PdfPageModel page || ViewModel == null)
            return;

        if (e.ClickCount == 2)
        {
            ViewModel.OpenPageDetailCommand.Execute(page);
            e.Handled = true;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _draggedPage = page;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (shift && _anchorPageIndex >= 0)
        {
            int targetIdx = ViewModel.Document.Pages.IndexOf(page);
            SelectRange(_anchorPageIndex, targetIdx);
        }
        else if (ctrl)
        {
            page.IsSelected = !page.IsSelected;
            _anchorPageIndex = ViewModel.Document.Pages.IndexOf(page);
        }
        else
        {
            // 既に選択済みのアイテムをクリックした場合は、複数ドラッグを妨げないよう
            // ドラッグ不成立時のMouseUpで単一選択へ切り替えます。
            if (!page.IsSelected)
            {
                ClearSelectionExcept(page);
            }
            _anchorPageIndex = ViewModel.Document.Pages.IndexOf(page);
        }

        e.Handled = true;
    }

    /// <summary>
    /// サムネイル解放時の選択クリーンアップを行います。
    /// </summary>
    private void OnPageMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PdfPageModel page && ViewModel != null)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            // ドラッグを行わずに選択済みアイテムを単一クリックした場合、そのアイテムのみを選択
            if (!ctrl && !shift && page.IsSelected)
            {
                if (ViewModel.Document.Pages.Count(p => p.IsSelected) > 1)
                {
                    ClearSelectionExcept(page);
                }
            }
        }

        _dragStartPoint = null;
        _draggedPage = null;
    }

    /// <summary>
    /// サムネイル上でのマウス移動を検知し、ドラッグを開始します。
    /// </summary>
    private void OnPageMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragStartPoint.HasValue || _draggedPage == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(this);
        Vector diff = _dragStartPoint.Value - current;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var page = _draggedPage;
            _dragStartPoint = null;
            _draggedPage = null;
            StartDrag(page, (FrameworkElement)sender);
        }
    }

    /// <summary>
    /// 指定ページ以外のすべての選択を解除します。
    /// </summary>
    private void ClearSelectionExcept(PdfPageModel targetPage)
    {
        if (ViewModel == null) return;
        foreach (var p in ViewModel.Document.Pages)
        {
            p.IsSelected = (p == targetPage);
        }
    }

    /// <summary>
    /// 指定範囲のインデックスにあるページをすべて選択状態にします。
    /// </summary>
    private void SelectRange(int fromIndex, int toIndex)
    {
        if (ViewModel == null) return;
        int min = Math.Min(fromIndex, toIndex);
        int max = Math.Max(fromIndex, toIndex);

        for (int i = 0; i < ViewModel.Document.Pages.Count; i++)
        {
            ViewModel.Document.Pages[i].IsSelected = (i >= min && i <= max);
        }
    }

    #endregion

    #region ドラッグ＆ドロップ（並び替え・外部PDF挿入）

    /// <summary>
    /// ページドラッグを開始し、浮遊アドーナーの表示とドラッグ元の半透明化を行います。
    /// </summary>
    private void StartDrag(PdfPageModel page, FrameworkElement sourceElement)
    {
        if (ViewModel == null) return;

        var pagesToMove = page.IsSelected
            ? ViewModel.Document.Pages.Where(p => p.IsSelected).ToList()
            : new List<PdfPageModel> { page };

        // ドラッグ元のサムネイル要素を半透明化
        _draggedContainers.Clear();
        foreach (var p in pagesToMove)
        {
            int idx = ViewModel.Document.Pages.IndexOf(p);
            if (ThumbnailItemsControl.ItemContainerGenerator.ContainerFromIndex(idx) is FrameworkElement container)
            {
                container.Opacity = 0.4;
                _draggedContainers.Add(container);
            }
        }

        // 浮遊プレビュー用アドーナーを生成・配置
        var preview = CreateDragPreview(page, pagesToMove.Count);
        var adornerLayer = AdornerLayer.GetAdornerLayer(this);
        if (adornerLayer != null)
        {
            _dragAdorner = new DragAdorner(this, preview, new Point(12, 12));
            adornerLayer.Add(_dragAdorner);
        }

        var data = new DataObject("PdfPages", pagesToMove);
        try
        {
            DragDrop.DoDragDrop(sourceElement, data, DragDropEffects.Move);
        }
        finally
        {
            EndDrag();
        }
    }

    /// <summary>
    /// ドラッグ操作を終了し、浮遊アドーナーと半透明表示を元に戻します。
    /// </summary>
    private void EndDrag()
    {
        if (_dragAdorner != null)
        {
            AdornerLayer.GetAdornerLayer(this)?.Remove(_dragAdorner);
            _dragAdorner = null;
        }

        foreach (var elem in _draggedContainers)
        {
            elem.Opacity = 1.0;
        }
        _draggedContainers.Clear();

        HideInsertionIndicator();
        _dragStartPoint = null;
        _draggedPage = null;
    }

    /// <summary>
    /// ドラッグ浮遊プレビュー用のUI要素を生成します。
    /// </summary>
    private FrameworkElement CreateDragPreview(PdfPageModel page, int count)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(2),
            Background = (Brush)FindResource("SurfaceBackgroundBrush"),
            Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.6,
                Color = Colors.Black
            },
            LayoutTransform = new ScaleTransform(1.04, 1.04)
        };

        var grid = new Grid();
        var img = new Image
        {
            Source = page.Thumbnail,
            Width = 130,
            Stretch = Stretch.Uniform
        };
        grid.Children.Add(img);

        if (count > 1)
        {
            var badge = new Border
            {
                Background = (Brush)FindResource("AccentBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0)
            };
            badge.Child = new TextBlock
            {
                Text = $"{count}",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            grid.Children.Add(badge);
        }

        border.Child = grid;
        return border;
    }

    /// <summary>
    /// グリッド上でのドラッグ移動中の挿入バー更新および浮遊アドーナー位置追従を行います。
    /// </summary>
    private void OnGridDragOver(object sender, DragEventArgs e)
    {
        var hostPos = e.GetPosition(ItemsHostGrid);
        _dragAdorner?.UpdatePosition(e.GetPosition(this));

        if (e.Data.GetDataPresent("PdfPages"))
        {
            e.Effects = DragDropEffects.Move;
            UpdateInsertionIndicator(hostPos);
            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Any(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            {
                e.Effects = DragDropEffects.Copy;
                UpdateInsertionIndicator(hostPos);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// ドラッグがグリッド外へ出た場合の処理を行います。
    /// </summary>
    private void OnGridDragLeave(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos.X < 0 || pos.Y < 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight)
        {
            HideInsertionIndicator();
        }
    }

    /// <summary>
    /// グリッド上へのドロップを処理します（ページ並び替えまたは外部PDF挿入）。
    /// </summary>
    private async void OnGridDrop(object sender, DragEventArgs e)
    {
        int targetIndex = _currentDropTargetIndex ?? (ViewModel?.Document.Pages.Count ?? 0);
        HideInsertionIndicator();

        if (e.Data.GetDataPresent("PdfPages") && ViewModel != null)
        {
            if (e.Data.GetData("PdfPages") is IReadOnlyList<PdfPageModel> pagesToMove)
            {
                ViewModel.MovePages(pagesToMove, targetIndex);
                e.Handled = true;
            }
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop) && ViewModel != null)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                await ViewModel.InsertPdfFilesAsync(files, targetIndex);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// 現在のマウス座標に基づいてドロップ挿入先のページインデックスと表示矩形を算出します。
    /// </summary>
    private (int TargetIndex, Rect BarRect) CalculateInsertionTarget(Point mousePos)
    {
        int count = ViewModel?.Document.Pages.Count ?? 0;
        if (count == 0)
        {
            return (0, new Rect(10, 10, 4, 150));
        }

        var itemBounds = new List<(int Index, Rect Bounds)>();
        for (int i = 0; i < count; i++)
        {
            if (ThumbnailItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
            {
                var pos = container.TranslatePoint(new Point(0, 0), ItemsHostGrid);
                itemBounds.Add((i, new Rect(pos, container.RenderSize)));
            }
        }

        if (itemBounds.Count == 0)
        {
            return (0, new Rect(10, 10, 4, 150));
        }

        var rowItems = itemBounds
            .Where(item => mousePos.Y >= item.Bounds.Top - 8 && mousePos.Y <= item.Bounds.Bottom + 8)
            .OrderBy(item => item.Bounds.Left)
            .ToList();

        if (rowItems.Count > 0)
        {
            for (int i = 0; i < rowItems.Count; i++)
            {
                var item = rowItems[i];
                double midX = item.Bounds.Left + item.Bounds.Width / 2.0;
                if (mousePos.X < midX)
                {
                    // 先頭アイテムの場合は左側余白に収まるよう配置
                    double barX = (i == 0) ? Math.Max(4, item.Bounds.Left + 3) : item.Bounds.Left - 2;
                    double barY = item.Bounds.Top + 10;
                    double barHeight = Math.Max(20, item.Bounds.Height - 20);
                    return (item.Index, new Rect(barX, barY, 4, barHeight));
                }
            }

            var lastInRow = rowItems[^1];
            double afterX = lastInRow.Bounds.Right - 7;
            double lastY = lastInRow.Bounds.Top + 10;
            double lastHeight = Math.Max(20, lastInRow.Bounds.Height - 20);
            return (lastInRow.Index + 1, new Rect(afterX, lastY, 4, lastHeight));
        }

        if (mousePos.Y < itemBounds[0].Bounds.Top)
        {
            var first = itemBounds[0];
            double barX = Math.Max(4, first.Bounds.Left + 3);
            double barY = first.Bounds.Top + 10;
            double barHeight = Math.Max(20, first.Bounds.Height - 20);
            return (0, new Rect(barX, barY, 4, barHeight));
        }

        var last = itemBounds[^1];
        double endX = last.Bounds.Right - 7;
        double endY = last.Bounds.Top + 10;
        double endHeight = Math.Max(20, last.Bounds.Height - 20);
        return (count, new Rect(endX, endY, 4, endHeight));
    }

    /// <summary>
    /// 挿入位置インジケーター（青い縦バー）の位置を更新して表示します。
    /// </summary>
    private void UpdateInsertionIndicator(Point mousePos)
    {
        var (targetIndex, barRect) = CalculateInsertionTarget(mousePos);
        _currentDropTargetIndex = targetIndex;

        InsertionIndicator.Margin = new Thickness(barRect.X, barRect.Y, 0, 0);
        InsertionIndicator.Height = barRect.Height;
        InsertionIndicator.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 挿入位置インジケーターを非表示にします。
    /// </summary>
    private void HideInsertionIndicator()
    {
        InsertionIndicator.Visibility = Visibility.Collapsed;
        _currentDropTargetIndex = null;
    }

    #endregion

    #region ラバーバンド矩形選択（余白ドラッグ複数選択）

    /// <summary>
    /// 指定された依存関係オブジェクトから祖先の PageBorder を探索します。
    /// </summary>
    private static Border? FindPageBorder(object? originalSource)
    {
        var dep = originalSource as DependencyObject;
        while (dep != null)
        {
            if (dep is Border border && border.Name == "PageBorder" && border.DataContext is PdfPageModel)
            {
                return border;
            }
            dep = VisualTreeHelper.GetParent(dep);
        }
        return null;
    }

    /// <summary>
    /// グリッド全体のプレビュー押下を検知し、サムネイル以外の余白クリックであれば全選択解除および矩形選択ドラッグを開始します。
    /// </summary>
    private void OnGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // サムネイル上のクリックであればサムネイル側の処理に委ねる
        if (FindPageBorder(e.OriginalSource) != null) return;

        if (ViewModel == null) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (!ctrl && !shift)
        {
            foreach (var p in ViewModel.Document.Pages)
            {
                p.IsSelected = false;
            }
        }

        _isRubberBandActive = true;
        _rubberBandStart = e.GetPosition(ItemsHostGrid);
        _initialSelection = new HashSet<PdfPageModel>(ViewModel.Document.Pages.Where(p => p.IsSelected));

        Canvas.SetLeft(RubberBandBorder, _rubberBandStart.X);
        Canvas.SetTop(RubberBandBorder, _rubberBandStart.Y);
        RubberBandBorder.Width = 0;
        RubberBandBorder.Height = 0;
        RubberBandBorder.Visibility = Visibility.Visible;

        GridScrollViewer.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// 矩形選択ドラッグ中の選択ボックス描画および接触アイテムの選択更新を行います。
    /// </summary>
    private void OnGridPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isRubberBandActive || ViewModel == null) return;

        var current = e.GetPosition(ItemsHostGrid);
        double x = Math.Min(_rubberBandStart.X, current.X);
        double y = Math.Min(_rubberBandStart.Y, current.Y);
        double width = Math.Abs(current.X - _rubberBandStart.X);
        double height = Math.Abs(current.Y - _rubberBandStart.Y);

        Canvas.SetLeft(RubberBandBorder, x);
        Canvas.SetTop(RubberBandBorder, y);
        RubberBandBorder.Width = width;
        RubberBandBorder.Height = height;

        var selectionRect = new Rect(x, y, width, height);
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        for (int i = 0; i < ViewModel.Document.Pages.Count; i++)
        {
            var page = ViewModel.Document.Pages[i];
            if (ThumbnailItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
            {
                var pos = container.TranslatePoint(new Point(0, 0), ItemsHostGrid);
                var itemRect = new Rect(pos, container.RenderSize);
                bool intersects = selectionRect.IntersectsWith(itemRect);

                page.IsSelected = ctrl
                    ? _initialSelection.Contains(page) || intersects
                    : intersects;
            }
        }
        e.Handled = true;
    }

    /// <summary>
    /// 矩形選択ドラッグを終了します。
    /// </summary>
    private void OnGridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isRubberBandActive)
        {
            _isRubberBandActive = false;
            RubberBandBorder.Visibility = Visibility.Collapsed;
            GridScrollViewer.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    #endregion
}
