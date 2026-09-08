using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace PDFBinder.App.Controls;

/// <summary>
/// 詳細エディタで使用する描画ツールの種類
/// </summary>
public enum EditorToolMode
{
    /// <summary>選択・移動ツール</summary>
    Select,
    /// <summary>通常ペン</summary>
    Pen,
    /// <summary>蛍光ペン（半透明）</summary>
    Highlighter,
    /// <summary>ストローク消しゴム（一筆消し）</summary>
    EraserStroke,
    /// <summary>部分消しゴム（ピクセル消し）</summary>
    EraserPoint,
    /// <summary>直線ツール</summary>
    StraightLine,
    /// <summary>手のひらツール（パン）</summary>
    Hand
}

/// <summary>
/// 直線ツールおよび各種描画モードの切り替えに対応したカスタムInkCanvas
/// </summary>
public class EditorInkCanvas : InkCanvas
{
    public static readonly DependencyProperty ToolModeProperty = DependencyProperty.Register(
        nameof(ToolMode),
        typeof(EditorToolMode),
        typeof(EditorInkCanvas),
        new PropertyMetadata(EditorToolMode.Pen, OnToolModeChanged));

    /// <summary>現在のツールモード</summary>
    public EditorToolMode ToolMode
    {
        get => (EditorToolMode)GetValue(ToolModeProperty);
        set => SetValue(ToolModeProperty, value);
    }

    private Point? _lineStartPoint;
    private Point? _currentLinePoint;
    private bool _isDrawingLine;

    // 手のひら（パン）用
    private Point? _panStartPoint;
    private ScrollViewer? _parentScrollViewer;

    public EditorInkCanvas()
    {
        UpdateEditingMode();
    }

    private static void OnToolModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditorInkCanvas canvas)
        {
            canvas.UpdateEditingMode();
        }
    }

    /// <summary>
    /// 現在のToolModeに合わせてInkCanvasのEditingModeやカーソルを更新します。
    /// </summary>
    public void UpdateEditingMode()
    {
        switch (ToolMode)
        {
            case EditorToolMode.Select:
                EditingMode = InkCanvasEditingMode.Select;
                Cursor = Cursors.Arrow;
                break;
            case EditorToolMode.Pen:
                EditingMode = InkCanvasEditingMode.Ink;
                DefaultDrawingAttributes.IsHighlighter = false;
                Cursor = Cursors.Pen;
                break;
            case EditorToolMode.Highlighter:
                EditingMode = InkCanvasEditingMode.Ink;
                DefaultDrawingAttributes.IsHighlighter = true;
                Cursor = Cursors.Pen;
                break;
            case EditorToolMode.EraserStroke:
                EditingMode = InkCanvasEditingMode.EraseByStroke;
                Cursor = Cursors.Cross;
                break;
            case EditorToolMode.EraserPoint:
                EditingMode = InkCanvasEditingMode.EraseByPoint;
                Cursor = Cursors.Cross;
                break;
            case EditorToolMode.StraightLine:
                EditingMode = InkCanvasEditingMode.None;
                Cursor = Cursors.Cross;
                break;
            case EditorToolMode.Hand:
                EditingMode = InkCanvasEditingMode.None;
                Cursor = Cursors.Hand;
                break;
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (ToolMode == EditorToolMode.StraightLine)
            {
                _lineStartPoint = e.GetPosition(this);
                _currentLinePoint = _lineStartPoint;
                _isDrawingLine = true;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (ToolMode == EditorToolMode.Hand)
            {
                _panStartPoint = e.GetPosition(this);
                _parentScrollViewer ??= FindParentScrollViewer(this);
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_isDrawingLine && _lineStartPoint.HasValue)
        {
            _currentLinePoint = e.GetPosition(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (ToolMode == EditorToolMode.Hand && _panStartPoint.HasValue && _parentScrollViewer != null)
        {
            Point current = e.GetPosition(this);
            double deltaX = _panStartPoint.Value.X - current.X;
            double deltaY = _panStartPoint.Value.Y - current.Y;

            _parentScrollViewer.ScrollToHorizontalOffset(_parentScrollViewer.HorizontalOffset + deltaX);
            _parentScrollViewer.ScrollToVerticalOffset(_parentScrollViewer.VerticalOffset + deltaY);
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (_isDrawingLine && _lineStartPoint.HasValue && _currentLinePoint.HasValue)
        {
            ReleaseMouseCapture();
            _isDrawingLine = false;

            CommitStraightLine(_lineStartPoint.Value, _currentLinePoint.Value);

            _lineStartPoint = null;
            _currentLinePoint = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (ToolMode == EditorToolMode.Hand)
        {
            ReleaseMouseCapture();
            _panStartPoint = null;
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseUp(e);
    }

    /// <summary>
    /// 直線プレビューを描画します。
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_isDrawingLine && _lineStartPoint.HasValue && _currentLinePoint.HasValue)
        {
            var attr = DefaultDrawingAttributes;
            var color = attr.Color;
            if (attr.IsHighlighter)
            {
                color = Color.FromArgb(120, color.R, color.G, color.B);
            }

            var pen = new Pen(new SolidColorBrush(color), attr.Width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            dc.DrawLine(pen, _lineStartPoint.Value, _currentLinePoint.Value);
        }
    }

    /// <summary>
    /// 直線をStrokeとしてStrokesコレクションにコミットします。
    /// </summary>
    private void CommitStraightLine(Point start, Point end)
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(start.X, start.Y),
            new StylusPoint(end.X, end.Y)
        };

        var stroke = new Stroke(points, DefaultDrawingAttributes.Clone());
        Strokes.Add(stroke);
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject? current)
    {
        while (current != null)
        {
            if (current is ScrollViewer sv) return sv;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
