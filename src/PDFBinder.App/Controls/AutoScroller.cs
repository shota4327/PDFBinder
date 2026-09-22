#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PDFBinder.App.Controls;

/// <summary>
/// ドラッグ操作中にコントロールの端にカーソルが近づいた際、自動スクロールを実行するコントローラー
/// </summary>
public class AutoScroller
{
    private readonly ScrollViewer _scrollViewer;
    private readonly DispatcherTimer _timer;
    private double _currentSpeed;

    /// <summary>
    /// オートスクロールが発火する上下端の領域幅（ピクセル）
    /// </summary>
    public const double DefaultZoneHeight = 40.0;

    /// <summary>
    /// 1フレームあたりの最大スクロール量（ピクセル）
    /// </summary>
    public const double DefaultMaxSpeed = 24.0;

    /// <summary>
    /// スクロールが発生した際に呼び出されるコールバック
    /// </summary>
    public event Action? Scrolled;

    /// <summary>
    /// AutoScroller クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="scrollViewer">スクロール対象の ScrollViewer</param>
    public AutoScroller(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer ?? throw new ArgumentNullException(nameof(scrollViewer));

        _timer = new DispatcherTimer(DispatcherPriority.Normal, scrollViewer.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(20) // 約50fps
        };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// オートスクロールが現在アクティブかどうかを取得します。
    /// </summary>
    public bool IsActive => _timer.IsEnabled;

    /// <summary>
    /// ポインター位置に基づいてスクロール速度を算出し、必要に応じてタイマーを開始または停止します。
    /// </summary>
    /// <param name="positionInScrollViewer">ScrollViewer 内でのポインター座標</param>
    public void UpdatePointerPosition(Point positionInScrollViewer)
    {
        double viewportHeight = _scrollViewer.ViewportHeight;
        if (viewportHeight <= 0)
        {
            viewportHeight = _scrollViewer.ActualHeight;
        }

        _currentSpeed = CalculateScrollSpeed(positionInScrollViewer.Y, viewportHeight, DefaultZoneHeight, DefaultMaxSpeed);

        if (Math.Abs(_currentSpeed) > 0.001)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else
        {
            Stop();
        }
    }

    /// <summary>
    /// オートスクロールタイマーを停止します。
    /// </summary>
    public void Stop()
    {
        _currentSpeed = 0;
        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    /// <summary>
    /// タイマー刻みごとにスクロール位置を更新します。
    /// </summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (Math.Abs(_currentSpeed) <= 0.001)
        {
            Stop();
            return;
        }

        double newOffset = Math.Clamp(_scrollViewer.VerticalOffset + _currentSpeed, 0, _scrollViewer.ScrollableHeight);
        if (Math.Abs(newOffset - _scrollViewer.VerticalOffset) > 0.001)
        {
            _scrollViewer.ScrollToVerticalOffset(newOffset);
            Scrolled?.Invoke();
        }
    }

    /// <summary>
    /// Y座標とビューポートの高さに基づいてスクロール速度を計算します（上端は負、下端は正）。
    /// </summary>
    /// <param name="pointerY">ScrollViewer 内でのポインター Y 座標</param>
    /// <param name="viewportHeight">ビューポートの有効な高さ</param>
    /// <param name="zoneHeight">反応領域の高さ（デフォルト40px）</param>
    /// <param name="maxSpeed">最大スクロール速度（デフォルト24px）</param>
    /// <returns>1フレームあたりのスクロール移動量（負: 上スクロール, 正: 下スクロール, 0: 停止）</returns>
    public static double CalculateScrollSpeed(double pointerY, double viewportHeight, double zoneHeight = DefaultZoneHeight, double maxSpeed = DefaultMaxSpeed)
    {
        if (viewportHeight <= 0 || zoneHeight <= 0 || maxSpeed <= 0)
        {
            return 0.0;
        }

        // 上端ゾーン（上端に近いほど高速に上スクロール）
        if (pointerY >= 0 && pointerY < zoneHeight)
        {
            double ratio = (zoneHeight - pointerY) / zoneHeight;
            return -maxSpeed * Math.Clamp(ratio, 0.0, 1.0);
        }

        // 下端ゾーン（下端に近いほど高速に下スクロール）
        double bottomZoneStart = viewportHeight - zoneHeight;
        if (pointerY > bottomZoneStart && pointerY <= viewportHeight)
        {
            double ratio = (pointerY - bottomZoneStart) / zoneHeight;
            return maxSpeed * Math.Clamp(ratio, 0.0, 1.0);
        }

        // 不感帯または領域外
        return 0.0;
    }
}
