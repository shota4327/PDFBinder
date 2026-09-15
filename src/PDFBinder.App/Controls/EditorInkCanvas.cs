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
    public static readonly DependencyProperty PageItemProperty = DependencyProperty.Register(
        nameof(PageItem),
        typeof(DetailPageItemViewModel),
        typeof(EditorInkCanvas),
        new PropertyMetadata(null, OnPageItemChanged));

    /// <summary>バインドされている個別ページのViewModel</summary>
    public DetailPageItemViewModel? PageItem
    {
        get => (DetailPageItemViewModel?)GetValue(PageItemProperty);
        set => SetValue(PageItemProperty, value);
    }

    private static void OnPageItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditorInkCanvas canvas)
        {
            if (e.OldValue is DetailPageItemViewModel oldItem)
            {
                oldItem.Page.InkStrokes.StrokesChanged -= canvas.OnMasterStrokesChanged;
            }
            if (e.NewValue is DetailPageItemViewModel newItem)
            {
                newItem.Page.InkStrokes.StrokesChanged += canvas.OnMasterStrokesChanged;
            }
            canvas.SyncStrokesWithCurrentMode();
        }
    }

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

    public static readonly DependencyProperty IsPenPressureEnabledProperty = DependencyProperty.Register(
        nameof(IsPenPressureEnabled),
        typeof(bool),
        typeof(EditorInkCanvas),
        new PropertyMetadata(false, OnIsPenPressureEnabledChanged));

    /// <summary>筆圧感知モードが有効かどうか</summary>
    public bool IsPenPressureEnabled
    {
        get => (bool)GetValue(IsPenPressureEnabledProperty);
        set => SetValue(IsPenPressureEnabledProperty, value);
    }

    public static readonly DependencyProperty DrawingColorProperty = DependencyProperty.Register(
        nameof(DrawingColor),
        typeof(Color),
        typeof(EditorInkCanvas),
        new PropertyMetadata(Colors.Black, OnDrawingColorChanged));

    /// <summary>ペンの描画色</summary>
    public Color DrawingColor
    {
        get => (Color)GetValue(DrawingColorProperty);
        set => SetValue(DrawingColorProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(EditorInkCanvas),
        new PropertyMetadata(1.0, OnStrokeThicknessChanged));

    /// <summary>ペンの描画太さ</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// 現在直線描画モードがアクティブであるか（直線ツール選択時、またはペン/蛍光ペン選択中に直線トグルが有効な場合）
    /// </summary>
    public bool IsStraightLineActive =>
        ToolMode == EditorToolMode.StraightLine ||
        (IsStraightLine && (ToolMode == EditorToolMode.Pen || ToolMode == EditorToolMode.Highlighter));

    /// <summary>
    /// 現在筆圧感知がアクティブであるか（ペンツール選択中かつ直線モードが無効、筆圧が有効な場合）
    /// </summary>
    public bool IsPenPressureActive =>
        IsPenPressureEnabled && ToolMode == EditorToolMode.Pen && !IsStraightLine;

    private Point? _lineStartPoint;
    private Point? _currentLinePoint;
    private bool _isDrawingLine;

    // 手のひら（パン）用
    private Point? _panStartPoint;
    private ScrollViewer? _parentScrollViewer;

    /// <summary>スタイラス離脱後のパーム抑制クールダウン時間（ミリ秒）</summary>
    internal const int StylusSuppressionCooldownMs = 150;

    /// <summary>1本指スクロール（パン）開始と判定する移動距離閾値（タッチスロップ）</summary>
    internal const double TouchSlopThreshold = 8.0;

    // スタイラスペン状態およびパームリジェクション管理
    private bool _isStylusTouching;
    private bool _isStylusInRange;
    private DateTime _lastStylusActivityTime = DateTime.MinValue;

    // マルチタッチ（パン・ピンチズーム）管理
    private readonly Dictionary<int, Point> _activeTouchPoints = new();
    private readonly Dictionary<int, Point> _touchStartPoints = new();
    private readonly HashSet<TouchDevice> _capturedTouchDevices = new();
    private bool _isPanningStarted;
    private double? _initialPinchDistance;
    private Point? _lastPinchCenter;

    /// <summary>現在のDynamicRendererを取得します（テスト・検証用）。</summary>
    public DynamicRenderer? CurrentDynamicRenderer => DynamicRenderer;

    public EditorInkCanvas()
    {
        DynamicRenderer = new PenOnlyDynamicRenderer();
        UpdateEditingMode();
        AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, e) => e.Handled = true), true);
        Strokes.StrokesChanged += OnCanvasStrokesChanged;
    }

    private static void OnToolModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditorInkCanvas canvas)
        {
            canvas.UpdateEditingMode();
            canvas.SyncStrokesWithCurrentMode();
        }
    }

    private static void OnIsStraightLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditorInkCanvas canvas)
        {
            canvas.UpdateEditingMode();
        }
    }

    private static void OnDrawingColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditorInkCanvas canvas)
        {
            canvas.ApplyDrawingAttributes();
        }
    }

    private static void OnStrokeThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditorInkCanvas canvas)
        {
            canvas.ApplyDrawingAttributes();
        }
    }

    private static void OnIsPenPressureEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditorInkCanvas canvas)
        {
            canvas.ApplyDrawingAttributes();
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

        ApplyDrawingAttributes();
    }

    /// <summary>
    /// 現在の描画色、太さ、およびツールに応じた描画属性と消しゴム形状を設定します。
    /// </summary>
    public void ApplyDrawingAttributes()
    {
        var attr = new DrawingAttributes
        {
            Color = DrawingColor,
            Width = StrokeThickness,
            Height = StrokeThickness,
            FitToCurve = true,
            IsHighlighter = ToolMode == EditorToolMode.Highlighter,
            IgnorePressure = !IsPenPressureActive
        };

        DefaultDrawingAttributes = attr;

        if (ToolMode == EditorToolMode.EraserPoint)
        {
            EraserShape = new EllipseStylusShape(StrokeThickness, StrokeThickness);
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
            PurgeActiveTouches();
            base.OnPreviewStylusDown(e);
        }
        // タッチデバイス（TabletDeviceType.Touch）時はbaseを呼ばず、InkCanvasによる誤描画を根本防止する
    }

    protected override void OnPreviewStylusMove(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _lastStylusActivityTime = DateTime.UtcNow;
            base.OnPreviewStylusMove(e);
        }
    }

    protected override void OnPreviewStylusUp(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _isStylusTouching = false;
            _lastStylusActivityTime = DateTime.UtcNow;
            base.OnPreviewStylusUp(e);
        }
    }

    protected override void OnStylusInAirMove(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _isStylusInRange = true;
            _lastStylusActivityTime = DateTime.UtcNow;
            PurgeActiveTouches();
        }
        base.OnStylusInAirMove(e);
    }

    protected override void OnStylusInRange(StylusEventArgs e)
    {
        if (IsStylusDevice(e))
        {
            _isStylusInRange = true;
            _lastStylusActivityTime = DateTime.UtcNow;
            PurgeActiveTouches();
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
    internal bool IsStylusSuppressed()
    {
        if (_isStylusTouching || _isStylusInRange) return true;
        return (DateTime.UtcNow - _lastStylusActivityTime).TotalMilliseconds < StylusSuppressionCooldownMs;
    }

    /// <summary>
    /// スタイラス検知時などに、残存する手指タッチおよびキャプチャを即時破棄し、描画モードを復元します。
    /// </summary>
    internal void PurgeActiveTouches()
    {
        if (_activeTouchPoints.Count > 0 || _capturedTouchDevices.Count > 0)
        {
            foreach (var device in _capturedTouchDevices.ToList())
            {
                ReleaseTouchCapture(device);
            }
            _capturedTouchDevices.Clear();
            _activeTouchPoints.Clear();
            _touchStartPoints.Clear();
            _isPanningStarted = false;
            _initialPinchDistance = null;
            _lastPinchCenter = null;
            UpdateEditingMode();
        }
    }

    internal static bool IsStylusDevice(StylusEventArgs e) =>
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

        if (CaptureTouch(e.TouchDevice))
        {
            _capturedTouchDevices.Add(e.TouchDevice);
        }
        _parentScrollViewer ??= FindParentScrollViewer(this);

        if (_parentScrollViewer != null)
        {
            Point pos = e.GetTouchPoint(_parentScrollViewer).Position;
            _activeTouchPoints[e.TouchDevice.Id] = pos;
            _touchStartPoints[e.TouchDevice.Id] = pos;

            // タッチ点数が変化した時はピンチズームの基準点およびパン開始状態をリセット
            _isPanningStarted = false;
            _initialPinchDistance = null;
            _lastPinchCenter = null;
        }

        e.Handled = true;
    }

    protected override void OnPreviewTouchMove(TouchEventArgs e)
    {
        if (IsStylusSuppressed())
        {
            e.Handled = true;
            return;
        }

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

        // タッチスロップ判定（一定以上の移動があるまでスクロールを開始しない）
        if (!_isPanningStarted)
        {
            if (_touchStartPoints.TryGetValue(touchId, out Point startPos))
            {
                Vector diff = newPos - startPos;
                if (diff.Length < TouchSlopThreshold)
                {
                    return;
                }

                _isPanningStarted = true;
                _activeTouchPoints[touchId] = newPos;
            }
            return;
        }

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
        var vm = (DataContext as DetailEditorViewModel) ?? FindParentViewModel<DetailEditorViewModel>(this);
        if (vm == null) return;

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
        _capturedTouchDevices.Remove(device);
        _activeTouchPoints.Remove(device.Id);
        _touchStartPoints.Remove(device.Id);
        _initialPinchDistance = null;
        _lastPinchCenter = null;

        if (_activeTouchPoints.Count == 0)
        {
            _isPanningStarted = false;
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

    #region テスト用ヘルパー

    internal int ActiveTouchPointCount => _activeTouchPoints.Count;
    internal bool IsPanningStarted => _isPanningStarted;

    internal void SetStylusStateForTesting(bool isTouching, bool isInRange, DateTime? lastActivity = null)
    {
        _isStylusTouching = isTouching;
        _isStylusInRange = isInRange;
        _lastStylusActivityTime = lastActivity ?? DateTime.UtcNow;
    }

    internal void AddTouchPointForTesting(int touchId, Point pos)
    {
        _activeTouchPoints[touchId] = pos;
        _touchStartPoints[touchId] = pos;
        EditingMode = InkCanvasEditingMode.None;
    }

    internal bool ProcessOneFingerPanForTesting(int touchId, Point newPos)
    {
        if (!_isPanningStarted)
        {
            if (_touchStartPoints.TryGetValue(touchId, out Point startPos))
            {
                Vector diff = newPos - startPos;
                if (diff.Length < TouchSlopThreshold)
                {
                    return false;
                }

                _isPanningStarted = true;
                _activeTouchPoints[touchId] = newPos;
                return true;
            }
            return false;
        }

        _activeTouchPoints[touchId] = newPos;
        return true;
    }

    #endregion

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

    #region ストロークキャッシュおよびマスターコレクション連携

    private bool _isInternalStrokeSync;

    /// <summary>
    /// 現在のツールモードに合わせて、InkCanvas内部のStrokesコレクションとページマスター・キャッシュ状態を同期します。
    /// </summary>
    public void SyncStrokesWithCurrentMode()
    {
        if (PageItem == null || _isInternalStrokeSync) return;

        _isInternalStrokeSync = true;
        try
        {
            if (IsEraserOrSelectMode(ToolMode))
            {
                // 消しゴム・選択モード: InkCanvasにストロークを展開し直接編集可能にする
                Strokes.Clear();
                foreach (var s in PageItem.Page.InkStrokes)
                {
                    Strokes.Add(s);
                }
                PageItem.StrokeCache = null; // 重複描画を防止
            }
            else
            {
                // ペン・蛍光ペン・直線モード: InkCanvas内部は空にし、確定ストロークは背面キャッシュ画像に任せる
                Strokes.Clear();
                RequestCacheUpdate();
            }
        }
        finally
        {
            _isInternalStrokeSync = false;
        }
    }

    /// <summary>
    /// 対象のツールが消しゴムまたは選択モードであるかを判定します。
    /// </summary>
    internal static bool IsEraserOrSelectMode(EditorToolMode tool) =>
        tool is EditorToolMode.EraserStroke or EditorToolMode.EraserPoint or EditorToolMode.Select;

    /// <summary>
    /// ペン描画完了時のストローク確定処理。キャッシュ分離モード時はInkCanvasを空に保ちマスターとキャッシュを即時更新します。
    /// </summary>
    protected override void OnStrokeCollected(InkCanvasStrokeCollectedEventArgs e)
    {
        if (PageItem != null && !IsEraserOrSelectMode(ToolMode))
        {
            _isInternalStrokeSync = true;
            try
            {
                Strokes.Remove(e.Stroke);
            }
            finally
            {
                _isInternalStrokeSync = false;
            }

            CommitNewStroke(e.Stroke);
            base.OnStrokeCollected(e);
            return;
        }

        base.OnStrokeCollected(e);
    }

    /// <summary>
    /// 新規ストロークをページマスターにコミットし、キャッシュの更新を要求します。
    /// </summary>
    private void CommitNewStroke(Stroke stroke)
    {
        if (PageItem == null) return;

        _isInternalStrokeSync = true;
        try
        {
            PageItem.Page.InkStrokes.Add(stroke);
        }
        finally
        {
            _isInternalStrokeSync = false;
        }

        RequestCacheUpdate();
    }

    /// <summary>
    /// 親ViewModelに対して現在のページのストロークキャッシュ更新を要求します。
    /// </summary>
    public void RequestCacheUpdate()
    {
        if (PageItem == null) return;
        var vm = (DataContext as DetailEditorViewModel) ?? FindParentViewModel<DetailEditorViewModel>(this);
        vm?.UpdatePageStrokeCache(PageItem);
    }

    /// <summary>
    /// InkCanvas内部のStrokes変更時イベントハンドラー（消しゴム操作時等のマスター同期）。
    /// </summary>
    private void OnCanvasStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_isInternalStrokeSync || PageItem == null) return;

        if (IsEraserOrSelectMode(ToolMode))
        {
            _isInternalStrokeSync = true;
            try
            {
                if (e.Removed.Count > 0)
                {
                    foreach (var s in e.Removed)
                    {
                        PageItem.Page.InkStrokes.Remove(s);
                    }
                }
                if (e.Added.Count > 0)
                {
                    foreach (var s in e.Added)
                    {
                        if (!PageItem.Page.InkStrokes.Contains(s))
                        {
                            PageItem.Page.InkStrokes.Add(s);
                        }
                    }
                }
            }
            finally
            {
                _isInternalStrokeSync = false;
            }
        }
    }

    /// <summary>
    /// 外部（Undo/Redoなど）によるページマスターストローク変更時のイベントハンドラー。
    /// </summary>
    private void OnMasterStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_isInternalStrokeSync || PageItem == null) return;

        if (IsEraserOrSelectMode(ToolMode))
        {
            SyncStrokesWithCurrentMode();
        }
        else
        {
            RequestCacheUpdate();
        }
    }

    #endregion

    /// <summary>
    /// 直線をStrokeとしてコミットします。
    /// </summary>
    private void CommitStraightLine(Point start, Point end)
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(start.X, start.Y),
            new StylusPoint(end.X, end.Y)
        };

        var stroke = new Stroke(points, DefaultDrawingAttributes.Clone());
        if (PageItem != null && !IsEraserOrSelectMode(ToolMode))
        {
            CommitNewStroke(stroke);
        }
        else
        {
            Strokes.Add(stroke);
        }
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

    private static T? FindParentViewModel<T>(DependencyObject? current) where T : class
    {
        while (current != null)
        {
            if (current is FrameworkElement fe && fe.DataContext is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
