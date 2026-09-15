using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="WheelPageTurnTracker"/> のホイール累積およびページ送り判定ロジックの単体テスト
/// </summary>
public class WheelPageTurnTrackerTests
{
    [Fact]
    public void ProcessScroll_SingleNotch_ReturnsSinglePageTurn()
    {
        // Arrange
        var tracker = new WheelPageTurnTracker();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act (上回転 120 -> 前ページ +1)
        int turnsUp = tracker.ProcessScroll(120, now, isAtEdge: true);

        // Assert
        Assert.Equal(1, turnsUp);
        Assert.Equal(0, tracker.AccumulatedDelta);

        // Act (下回転 -120 -> 次ページ -1)
        int turnsDown = tracker.ProcessScroll(-120, now.AddMilliseconds(50), isAtEdge: true);

        // Assert
        Assert.Equal(-1, turnsDown);
        Assert.Equal(0, tracker.AccumulatedDelta);
    }

    [Fact]
    public void ProcessScroll_FastContinuousScroll_ReturnsTurnsImmediately()
    {
        // Arrange
        var tracker = new WheelPageTurnTracker();
        var baseTime = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act & Assert: 短時間に素早く3ノッチ回転させた場合、それぞれ即座に1ページ送り判定される
        int turn1 = tracker.ProcessScroll(-120, baseTime, isAtEdge: true);
        int turn2 = tracker.ProcessScroll(-120, baseTime.AddMilliseconds(15), isAtEdge: true);
        int turn3 = tracker.ProcessScroll(-120, baseTime.AddMilliseconds(30), isAtEdge: true);

        Assert.Equal(-1, turn1);
        Assert.Equal(-1, turn2);
        Assert.Equal(-1, turn3);
        Assert.Equal(0, tracker.AccumulatedDelta);
    }

    [Fact]
    public void ProcessScroll_MultipleNotchesInSingleEvent_ReturnsMultipleTurns()
    {
        // Arrange
        var tracker = new WheelPageTurnTracker();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act (一括で240回転量が発生した場合)
        int turns = tracker.ProcessScroll(-240, now, isAtEdge: true);

        // Assert
        Assert.Equal(-2, turns);
        Assert.Equal(0, tracker.AccumulatedDelta);
    }

    [Fact]
    public void ProcessScroll_FractionalDeltas_AccumulatesUntilThreshold()
    {
        // Arrange (トラックパッド等の微細Delta 40刻み)
        var tracker = new WheelPageTurnTracker();
        var baseTime = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act 1回目 (40) -> まだ閾値120未満
        int turn1 = tracker.ProcessScroll(40, baseTime, isAtEdge: true);
        Assert.Equal(0, turn1);
        Assert.Equal(40, tracker.AccumulatedDelta);

        // Act 2回目 (40累計80) -> まだ閾値120未満
        int turn2 = tracker.ProcessScroll(40, baseTime.AddMilliseconds(20), isAtEdge: true);
        Assert.Equal(0, turn2);
        Assert.Equal(80, tracker.AccumulatedDelta);

        // Act 3回目 (40累計120) -> 閾値到達で1ページ送り
        int turn3 = tracker.ProcessScroll(40, baseTime.AddMilliseconds(40), isAtEdge: true);
        Assert.Equal(1, turn3);
        Assert.Equal(0, tracker.AccumulatedDelta);
    }

    [Fact]
    public void ProcessScroll_IdleTimeout_ResetsAccumulatedFractionalDelta()
    {
        // Arrange
        var tracker = new WheelPageTurnTracker();
        var baseTime = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        // 60だけ回転
        int turn1 = tracker.ProcessScroll(60, baseTime, isAtEdge: true);
        Assert.Equal(0, turn1);
        Assert.Equal(60, tracker.AccumulatedDelta);

        // 300ms経過（デフォルト250msタイムアウト超過）して再度60回転
        int turn2 = tracker.ProcessScroll(60, baseTime.AddMilliseconds(300), isAtEdge: true);

        // 前回の60は破棄され、今回の60のみ保持されるためページ送りは発火しない
        Assert.Equal(0, turn2);
        Assert.Equal(60, tracker.AccumulatedDelta);
    }

    [Fact]
    public void ProcessScroll_DirectionReversal_ResetsAccumulatedDelta()
    {
        // Arrange
        var tracker = new WheelPageTurnTracker();
        var baseTime = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        // 上方向に60回転
        tracker.ProcessScroll(60, baseTime, isAtEdge: true);
        Assert.Equal(60, tracker.AccumulatedDelta);

        // 逆の下方向に-60回転
        int turn = tracker.ProcessScroll(-60, baseTime.AddMilliseconds(20), isAtEdge: true);

        // 逆回転により前回の正Deltaがリセットされ、-60のみが蓄積される
        Assert.Equal(0, turn);
        Assert.Equal(-60, tracker.AccumulatedDelta);
    }

    [Fact]
    public void ProcessScroll_NotAtEdge_ResetsAndReturnsZero()
    {
        // Arrange
        var tracker = new WheelPageTurnTracker();
        var baseTime = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        // 境界上で60蓄積
        tracker.ProcessScroll(60, baseTime, isAtEdge: true);
        Assert.Equal(60, tracker.AccumulatedDelta);

        // ページ内スクロール中（isAtEdge: false）でホイール回転が発生
        int turn = tracker.ProcessScroll(120, baseTime.AddMilliseconds(20), isAtEdge: false);

        // ページ内スクロール時はトラッカーがリセットされ、ページ送りは発生しない
        Assert.Equal(0, turn);
        Assert.Equal(0, tracker.AccumulatedDelta);
    }

    [Fact]
    public void Reset_ClearsAccumulatedDeltaAndTimestamp()
    {
        // Arrange
        var tracker = new WheelPageTurnTracker();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        tracker.ProcessScroll(80, now, isAtEdge: true);
        Assert.Equal(80, tracker.AccumulatedDelta);
        Assert.Equal(now, tracker.LastEventTime);

        // Act
        tracker.Reset();

        // Assert
        Assert.Equal(0, tracker.AccumulatedDelta);
        Assert.Equal(DateTime.MinValue, tracker.LastEventTime);
    }
}
