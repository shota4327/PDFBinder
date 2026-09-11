using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Windows.Media;
using PDFBinder.App.Helpers;
using PDFBinder.App.ViewModels;

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

    public static readonly DependencyProperty IsStraightLineProperty = DependencyProperty.Register(
        nameof(IsStraightLine),
        typeof(bool),
        typeof(EditorInkCanvas),
        new PropertyMetadata(false, OnIsStraightLineChanged));

    /// <summary>直線描画モードが有効かどうか</summary>
    public bool IsStraightLine
    {
        get => (bool)GetValue(IsStraightLineProperty);
        set => SetValue(IsStraightLineProperty, value);
    }

    /// <summary>
    /// 現在直線描画モードがアクティブであるか（直線ツール選択時、またはペン/蛍光ペン選択中に直線トグルが有効な場合）
    /// </summary>
    public bool IsStraightLineActive =>
        ToolMode == EditorToolMode.StraightLine ||
        (IsStraightLine && (ToolMode == EditorToolMode.Pen || ToolMode == EditorToolMode.Highlighter));

    private Point? _lineStartPoint;
    private Point? _currentLinePoint;
    private bool _isDrawingLine;

    // 手のひら（パン）用
    private Point? _panStartPoint;
    private ScrollViewer? _parentScrollViewer;

    // スタイラスペン状態およびパームリジェクション管理
    private bool _isStylusTouching;
    private bool _isStylusInRange;
    private DateTime _lastStylusActivityTime = DateTime.MinValue;

    // マルチタッチ（パン・ピンチズーム）管理
    private readonly Dictionary<int, Point> _activeTouchPoints = new();
    private double? _initialPinchDistance;
    private Point? _lastPinchCenter;

    /// <summary>現在のDynamicRendererを取得します（テスト・検証用）。</summary>
    public DynamicRenderer? CurrentDynamicRenderer => DynamicRenderer;

    public EditorInkCanvas()
    {
        DynamicRenderer = new PenOnlyDynamicRenderer();
        UpdateEditingMode();
    }

    private static void OnToolModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditorInkCanvas canvas)
        {
            canvas.UpdateEditingMode();
        }
    }

    private static void OnIsStraightLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
        if (_activeTouchPoints.Count > 0)
        {
            EditingMode = InkCanvasEditingMode.None;
            return;
        }

        switch (ToolMode)
        {
            case EditorToolMode.Select:
                EditingMode = InkCanvasEditingMode.Select;
                Cursor = Cursors.Arrow;
                break;
            case EditorToolMode.Pen:
                DefaultDrawingAttributes.IsHighlighter = false;
                ApplyDrawingOrStraightLineMode();
                break;
            case EditorToolMode.Highlighter:
                DefaultDrawingAttributes.IsHighlighter = true;
                ApplyDrawingOrStraightLineMode();
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

    /// <summary>
    /// ペンまたは蛍光ペンにおいて、直線トグル状態に応じたEditingModeとCursorを設定します。
    /// </summary>
    private void ApplyDrawingOrStraightLineMode()
    {
        if (IsStraightLine)
        {
            EditingMode = InkCanvasEditingMode.None;
            Cursor = Cursors.Cross;
        }
        else
        {
            EditingMode = InkCanvasEditingMode.Ink;
            Cursor = Cursors.Pen;
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (IsStraightLineActive)
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

    #region スタイラスペン・パームリジェクション処理

    protected override void OnPreviewStylusDown(StylusDownEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _isStylusTouching = true;
            _lastStylusActivityTime = DateTime.UtcNow;
        }

        base.OnPreviewStylusDown(e);
    }

    protected override void OnPreviewStylusMove(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _lastStylusActivityTime = DateTime.UtcNow;
        }

        base.OnPreviewStylusMove(e);
    }

    protected override void OnPreviewStylusUp(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _isStylusTouching = false;
            _lastStylusActivityTime = DateTime.UtcNow;
        }

        base.OnPreviewStylusUp(e);
    }

    protected override void OnStylusInAirMove(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _isStylusInRange = true;
            _lastStylusActivityTime = DateTime.UtcNow;
        }
        base.OnStylusInAirMove(e);
    }

    protected override void OnStylusInRange(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _isStylusInRange = true;
            _lastStylusActivityTime = DateTime.UtcNow;
        }
        base.OnStylusInRange(e);
    }

    protected override void OnStylusOutOfRange(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _isStylusInRange = false;
            _lastStylusActivityTime = DateTime.UtcNow;
        }
        base.OnStylusOutOfRange(e);
    }

    /// <summary>
    /// スタイラスペンが接地・近接（ホバー）中か判定し、タッチ操作を抑止すべきか返します。
    /// </summary>
    private bool IsStylusSuppressed()
    {
        if (_isStylusTouching || _isStylusInRange) return true;
        return (DateTime.UtcNow - _lastStylusActivityTime).TotalMilliseconds < 350;
    }

    private static bool IsStylusDevice(StylusEventArgs e) =>
        e.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Stylus;

    #endregion

    #region マルチタッチ（パン・ピンチズーム）処理

    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        if (IsStylusSuppressed())
        {
            // ペン使用中の手のひら接地を完全に抑止
            e.Handled = true;
            return;
        }

        // タッチ操作中はインク収集モードを一時停止してパン・ズームに専念
        EditingMode = InkCanvasEditingMode.None;

        CaptureTouch(e.TouchDevice);
        _parentScrollViewer ??= FindParentScrollViewer(this);

        if (_parentScrollViewer != null)
        {
            Point pos = e.GetTouchPoint(_parentScrollViewer).Position;
            _activeTouchPoints[e.TouchDevice.Id] = pos;

            // タッチ点数が変化した時はピンチズームの基準点をリセット
            _initialPinchDistance = null;
            _lastPinchCenter = null;
        }

        e.Handled = true;
    }

    protected override void OnPreviewTouchMove(TouchEventArgs e)
    {
        if (!_activeTouchPoints.ContainsKey(e.TouchDevice.Id)) return;

        _parentScrollViewer ??= FindParentScrollViewer(this);
        if (_parentScrollViewer == null) return;

        Point newPos = e.GetTouchPoint(_parentScrollViewer).Position;

        if (_activeTouchPoints.Count == 1)
        {
            HandleOneFingerPan(e.TouchDevice.Id, newPos);
        }
        else if (_activeTouchPoints.Count == 2)
        {
            _activeTouchPoints[e.TouchDevice.Id] = newPos;
            HandleTwoFingerPinchZoom();
        }

        e.Handled = true;
    }

    protected override void OnPreviewTouchUp(TouchEventArgs e)
    {
        HandleTouchRelease(e.TouchDevice);
        e.Handled = true;
    }

    protected override void OnTouchLeave(TouchEventArgs e)
    {
        HandleTouchRelease(e.TouchDevice);
        base.OnTouchLeave(e);
    }

    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        HandleTouchRelease(e.TouchDevice);
        base.OnLostTouchCapture(e);
    }

    private void HandleOneFingerPan(int touchId, Point newPos)
    {
        if (_parentScrollViewer == null) return;

        if (_activeTouchPoints.TryGetValue(touchId, out Point oldPos))
        {
            double deltaX = oldPos.X - newPos.X;
            double deltaY = oldPos.Y - newPos.Y;

            _parentScrollViewer.ScrollToHorizontalOffset(_parentScrollViewer.HorizontalOffset + deltaX);
            _parentScrollViewer.ScrollToVerticalOffset(_parentScrollViewer.VerticalOffset + deltaY);

            _activeTouchPoints[touchId] = newPos;
        }
    }

    private void HandleTwoFingerPinchZoom()
    {
        if (_parentScrollViewer == null || _activeTouchPoints.Count < 2) return;
        if (DataContext is not DetailEditorViewModel vm) return;

        var points = _activeTouchPoints.Values.Take(2).ToArray();
        Point p1 = points[0];
        Point p2 = points[1];

        double currentDistance = (p1 - p2).Length;
        Point currentCenter = new((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);

        if (_initialPinchDistance == null || _lastPinchCenter == null)
        {
            _initialPinchDistance = currentDistance;
            _lastPinchCenter = currentCenter;
            return;
        }

        var result = PinchZoomHelper.Calculate(
            vm.Zoom,
            _initialPinchDistance.Value,
            currentDistance,
            _lastPinchCenter.Value,
            currentCenter,
            _parentScrollViewer.HorizontalOffset,
            _parentScrollViewer.VerticalOffset,
            DetailEditorViewModel.MinZoom,
            DetailEditorViewModel.MaxZoom);

        vm.SetZoom(result.NewZoom);
        _parentScrollViewer.UpdateLayout();
        _parentScrollViewer.ScrollToHorizontalOffset(result.TargetHorizontalOffset);
        _parentScrollViewer.ScrollToVerticalOffset(result.TargetVerticalOffset);

        _initialPinchDistance = currentDistance;
        _lastPinchCenter = currentCenter;
    }

    private void HandleTouchRelease(TouchDevice device)
    {
        ReleaseTouchCapture(device);
        _activeTouchPoints.Remove(device.Id);
        _initialPinchDistance = null;
        _lastPinchCenter = null;

        if (_activeTouchPoints.Count == 0)
        {
            UpdateEditingMode();
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
            if (IsStylusCaptured)
            {
                ReleaseStylusCapture();
            }
        }
    }

    #endregion

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
