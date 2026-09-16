using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Controls;

/// <summary>
/// PDFページの文字選択・ハイライト表示およびリンク（URL/ページ内ジャンプ）の操作を担うオーバーレイコントロール
/// </summary>
public class InteractiveOverlayCanvas : FrameworkElement
{
    private static readonly Brush SelectionFillBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0x00, 0x78, 0xD7));
    private static readonly Pen SelectionBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x78, 0xD7)), 1.0);
    private static readonly Pen LinkHoverBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x78, 0xD7)), 1.0);

    private Point? _dragStartPoint;
    private bool _isDraggingSelection;
    private IReadOnlyList<PdfTextCharacter> _selectedCharacters = Array.Empty<PdfTextCharacter>();
    private PdfLinkAnnotation? _hoveredLink;
    private readonly ToolTip _linkToolTip = new();

    static InteractiveOverlayCanvas()
    {
        SelectionFillBrush.Freeze();
        SelectionBorderPen.Freeze();
        LinkHoverBorderPen.Freeze();
    }

    public InteractiveOverlayCanvas()
    {
        Focusable = true;
        ToolTip = _linkToolTip;
        ToolTipService.SetIsEnabled(this, false);

        ContextMenu = CreateContextMenu();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty PageItemProperty = DependencyProperty.Register(
        nameof(PageItem),
        typeof(DetailPageItemViewModel),
        typeof(InteractiveOverlayCanvas),
        new PropertyMetadata(null, OnPageItemChanged));

    /// <summary>バインドされている個別ページのViewModel</summary>
    public DetailPageItemViewModel? PageItem
    {
        get => (DetailPageItemViewModel?)GetValue(PageItemProperty);
        set => SetValue(PageItemProperty, value);
    }

    public static readonly DependencyProperty ToolModeProperty = DependencyProperty.Register(
        nameof(ToolMode),
        typeof(EditorToolMode),
        typeof(InteractiveOverlayCanvas),
        new PropertyMetadata(EditorToolMode.Pen, OnToolModeChanged));

    /// <summary>現在のツールモード</summary>
    public EditorToolMode ToolMode
    {
        get => (EditorToolMode)GetValue(ToolModeProperty);
        set => SetValue(ToolModeProperty, value);
    }

    private static void OnPageItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InteractiveOverlayCanvas canvas)
        {
            if (e.OldValue is DetailPageItemViewModel oldVm)
            {
                oldVm.PropertyChanged -= canvas.OnPageItemPropertyChanged;
            }
            if (e.NewValue is DetailPageItemViewModel newVm)
            {
                newVm.PropertyChanged += canvas.OnPageItemPropertyChanged;
            }
            canvas.ClearSelection();
            canvas.InvalidateVisual();
        }
    }

    private static void OnToolModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InteractiveOverlayCanvas canvas)
        {
            canvas.ClearSelection();
            canvas.UpdateCursor();
            canvas.InvalidateVisual();
        }
    }

    private void OnPageItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailPageItemViewModel.InteractiveData))
        {
            ClearSelection();
            InvalidateVisual();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (PageItem != null)
        {
            PageItem.PropertyChanged += OnPageItemPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (PageItem != null)
        {
            PageItem.PropertyChanged -= OnPageItemPropertyChanged;
        }
    }

    /// <inheritdoc/>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var pt = hitTestParameters.HitPoint;
        if (ToolMode == EditorToolMode.TextSelect)
        {
            return new PointHitTestResult(this, pt);
        }

        if (ToolMode == EditorToolMode.Hand && FindLinkAtPoint(pt) != null)
        {
            return new PointHitTestResult(this, pt);
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (PageItem?.HasSelectedText == true)
            {
                PageItem.CopySelectedText();
                e.Handled = true;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left) return;

        Focus();
        var pos = e.GetPosition(this);
        _dragStartPoint = pos;
        _isDraggingSelection = false;

        var link = FindLinkAtPoint(pos);
        if (link == null && ToolMode == EditorToolMode.TextSelect)
        {
            ClearSelection();
        }

        CaptureMouse();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);

        if (IsMouseCaptured && _dragStartPoint.HasValue && ToolMode == EditorToolMode.TextSelect)
        {
            HandleTextDragSelection(pos);
            e.Handled = true;
            return;
        }

        UpdateHoverState(pos);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != MouseButton.Left) return;

        var pos = e.GetPosition(this);
        bool wasClick = !_isDraggingSelection && _dragStartPoint.HasValue &&
                        (pos - _dragStartPoint.Value).Length < 4.0;

        _dragStartPoint = null;
        _isDraggingSelection = false;
        ReleaseMouseCapture();

        if (wasClick)
        {
            var link = FindLinkAtPoint(pos);
            if (link != null)
            {
                HandleLinkClick(link);
                e.Handled = true;
                return;
            }
        }

        e.Handled = true;
    }

    /// <summary>
    /// マウスドラッグによる文字範囲選択を処理します。
    /// </summary>
    private void HandleTextDragSelection(Point currentPos)
    {
        if (!_dragStartPoint.HasValue) return;

        var diff = currentPos - _dragStartPoint.Value;
        if (!_isDraggingSelection && diff.Length < 4.0) return;

        _isDraggingSelection = true;
        var rect = new Rect(_dragStartPoint.Value, currentPos);

        if (PageItem?.InteractiveData != null)
        {
            var (text, characters) = PageItem.InteractiveData.GetTextInRect(rect);
            _selectedCharacters = characters;
            PageItem.SelectedText = text;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// ホバー状態およびカーソル・ツールチップを更新します。
    /// </summary>
    private void UpdateHoverState(Point pos)
    {
        var link = FindLinkAtPoint(pos);
        if (_hoveredLink != link)
        {
            _hoveredLink = link;
            UpdateCursorAndToolTip(link);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 指定されたリンクに応じたカーソルおよびツールチップを設定します。
    /// </summary>
    private void UpdateCursorAndToolTip(PdfLinkAnnotation? link)
    {
        if (link != null)
        {
            Cursor = Cursors.Hand;
            string tipText = link.LinkType == PdfLinkType.Uri
                ? link.Uri ?? string.Empty
                : $"ページ {link.TargetPageIndex + 1} へジャンプ";

            _linkToolTip.Content = tipText;
            ToolTipService.SetIsEnabled(this, true);
        }
        else
        {
            UpdateCursor();
            ToolTipService.SetIsEnabled(this, false);
        }
    }

    /// <summary>
    /// 現在のツールに応じた標準カーソルを設定します。
    /// </summary>
    private void UpdateCursor()
    {
        Cursor = ToolMode switch
        {
            EditorToolMode.TextSelect => Cursors.IBeam,
            EditorToolMode.Hand => Cursors.Hand,
            _ => Cursors.Arrow
        };
    }

    /// <summary>
    /// リンククリック時の遷移処理（ブラウザ起動またはページジャンプ）を実行します。
    /// </summary>
    private void HandleLinkClick(PdfLinkAnnotation link)
    {
        if (link.LinkType == PdfLinkType.Uri && !string.IsNullOrEmpty(link.Uri))
        {
            try
            {
                Process.Start(new ProcessStartInfo(link.Uri) { UseShellExecute = true });
            }
            catch
            {
                // URL起動例外の防止
            }
        }
        else if (link.LinkType == PdfLinkType.PageJump && link.TargetPageIndex >= 0)
        {
            PageItem?.RequestPageJump(link.TargetPageIndex);
        }
    }

    /// <summary>
    /// 指定座標に存在するリンク注釈を検索します。
    /// </summary>
    private PdfLinkAnnotation? FindLinkAtPoint(Point pt)
    {
        var links = PageItem?.InteractiveData?.Links;
        if (links == null || links.Count == 0) return null;

        foreach (var link in links)
        {
            if (link.BoundingBox.Contains(pt))
            {
                return link;
            }
        }
        return null;
    }

    /// <summary>
    /// 選択状態を解除します。
    /// </summary>
    public void ClearSelection()
    {
        _selectedCharacters = Array.Empty<PdfTextCharacter>();
        if (PageItem != null)
        {
            PageItem.SelectedText = string.Empty;
        }
        InvalidateVisual();
    }

    /// <summary>
    /// 右クリック用コンテキストメニューを生成します。
    /// </summary>
    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        var copyItem = new MenuItem { Header = "コピー (_C)", InputGestureText = "Ctrl+C" };
        copyItem.Click += (_, _) => PageItem?.CopySelectedText();
        menu.Items.Add(copyItem);

        menu.Opened += (_, _) =>
        {
            copyItem.IsEnabled = PageItem?.HasSelectedText == true;
        };

        return menu;
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_selectedCharacters.Count > 0)
        {
            RenderSelectionHighlights(dc);
        }

        if (_hoveredLink != null)
        {
            RenderLinkHover(dc, _hoveredLink);
        }
    }

    /// <summary>
    /// 選択された文字のバウンディングボックス群をハイライト描画します。
    /// </summary>
    private void RenderSelectionHighlights(DrawingContext dc)
    {
        foreach (var c in _selectedCharacters)
        {
            dc.DrawRectangle(SelectionFillBrush, SelectionBorderPen, c.BoundingBox);
        }
    }

    /// <summary>
    /// ホバー中のリンク領域の境界線を描画します。
    /// </summary>
    private void RenderLinkHover(DrawingContext dc, PdfLinkAnnotation link)
    {
        dc.DrawRectangle(null, LinkHoverBorderPen, link.BoundingBox);
    }
}
