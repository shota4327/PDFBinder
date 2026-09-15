namespace PDFBinder.App.Helpers;

/// <summary>
/// マウスホイール回転量（Delta）の累積、アイドルタイムアウト判定、およびページ送り要求数を算出するトラッカークラス
/// </summary>
public class WheelPageTurnTracker
{
    /// <summary>1ページめくりに必要な標準ホイールDelta閾値（Windowsの標準1ノッチ = 120）</summary>
    public const int DefaultDeltaThreshold = 120;

    /// <summary>ホイール回転が停止したと判定するアイドルタイムアウト時間（250ms）</summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// 1ページめくりに必要なDelta閾値を取得します。
    /// </summary>
    public int DeltaThreshold { get; }

    /// <summary>
    /// 端数Deltaをリセットするアイドルタイムアウト時間を取得します。
    /// </summary>
    public TimeSpan IdleTimeout { get; }

    private int _accumulatedDelta;
    private DateTime _lastEventTime = DateTime.MinValue;

    /// <summary>
    /// 現在累積されているDelta値を取得します。
    /// </summary>
    public int AccumulatedDelta => _accumulatedDelta;

    /// <summary>
    /// 直近のホイールイベント日時を取得します。
    /// </summary>
    public DateTime LastEventTime => _lastEventTime;

    /// <summary>
    /// デフォルトの閾値およびタイムアウト時間で初期化します。
    /// </summary>
    /// <param name="deltaThreshold">1ページ送りに必要なDelta閾値（正の整数）</param>
    /// <param name="idleTimeout">アイドルタイムアウト時間</param>
    public WheelPageTurnTracker(int deltaThreshold = DefaultDeltaThreshold, TimeSpan? idleTimeout = null)
    {
        DeltaThreshold = deltaThreshold > 0 ? deltaThreshold : DefaultDeltaThreshold;
        IdleTimeout = idleTimeout ?? DefaultIdleTimeout;
    }

    /// <summary>
    /// ホイールスクロールイベントを処理し、めくるべきページ数（正: 前ページ方向、負: 次ページ方向）を算出します。
    /// </summary>
    /// <param name="delta">ホイールイベントの回転量（正: 上回転、負: 下回転）</param>
    /// <param name="now">イベント発生時刻</param>
    /// <param name="isAtEdge">ページ送り可能な境界（上端または下端、あるいは全体表示）にあるかどうか</param>
    /// <returns>めくるべきページ数（0の場合はページめくり不要）</returns>
    public int ProcessScroll(int delta, DateTime now, bool isAtEdge)
    {
        if (!isAtEdge || delta == 0)
        {
            Reset();
            return 0;
        }

        // アイドルタイムアウト判定: 一定時間操作が途切れた場合は端数Deltaをリセット
        if (_lastEventTime != DateTime.MinValue && now - _lastEventTime > IdleTimeout)
        {
            _accumulatedDelta = 0;
        }

        _lastEventTime = now;

        // 逆回転検知: 反対方向へのスクロール開始時は累積値をリセット
        if ((delta > 0 && _accumulatedDelta < 0) || (delta < 0 && _accumulatedDelta > 0))
        {
            _accumulatedDelta = 0;
        }

        _accumulatedDelta += delta;

        int turns = _accumulatedDelta / DeltaThreshold;
        if (turns != 0)
        {
            _accumulatedDelta -= turns * DeltaThreshold;
        }

        return turns;
    }

    /// <summary>
    /// 累積Deltaおよびイベント記録をリセットします。
    /// </summary>
    public void Reset()
    {
        _accumulatedDelta = 0;
        _lastEventTime = DateTime.MinValue;
    }
}
