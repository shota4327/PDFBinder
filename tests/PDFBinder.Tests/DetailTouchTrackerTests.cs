using System;
using System.Windows;
using System.Windows.Input;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="DetailTouchTracker"/> による余白エリア・ページ全域でのマルチタッチ（パン・ピンチズーム）およびパームリジェクションの単体テスト
/// </summary>
public class DetailTouchTrackerTests
{
    [Fact]
    public void OneFingerPan_SuppressesMicroMovement_UntilTouchSlopExceeded()
    {
        var tracker = new DetailTouchTracker();
        var startPos = new Point(100, 100);

        Assert.True(tracker.OnTouchDown(1, startPos));
        Assert.Equal(1, tracker.ActiveTouchCount);
        Assert.False(tracker.IsPanningStarted);

        // 閾値（8.0px）未満の微小移動（手の震え・接地ノイズ）
        var smallMoveResult = tracker.OnTouchMove(1, new Point(103, 104), currentZoom: 1.0, minZoom: 0.1, maxZoom: 5.0, horizontalOffset: 0, verticalOffset: 0);
        Assert.Equal(TouchGestureType.None, smallMoveResult.GestureType);
        Assert.False(tracker.IsPanningStarted);

        // 閾値以上の明確なスワイプ移動（距離10px）
        var thresholdMoveResult = tracker.OnTouchMove(1, new Point(110, 100), currentZoom: 1.0, minZoom: 0.1, maxZoom: 5.0, horizontalOffset: 0, verticalOffset: 0);
        Assert.Equal(TouchGestureType.None, thresholdMoveResult.GestureType);
        Assert.True(tracker.IsPanningStarted);

        // パン開始後のドラッグ移動（差分スクロール変位が計算される）
        var panResult = tracker.OnTouchMove(1, new Point(125, 110), currentZoom: 1.0, minZoom: 0.1, maxZoom: 5.0, horizontalOffset: 0, verticalOffset: 0);
        Assert.Equal(TouchGestureType.Pan, panResult.GestureType);
        Assert.Equal(-15.0, panResult.DeltaX);
        Assert.Equal(-10.0, panResult.DeltaY);
    }

    [Fact]
    public void TwoFingerPinchZoom_CalculatesNewZoomAndOffset()
    {
        var tracker = new DetailTouchTracker();

        // 2本指のタッチ開始（初期距離100px）
        tracker.OnTouchDown(1, new Point(100, 200));
        tracker.OnTouchDown(2, new Point(200, 200));
        Assert.Equal(2, tracker.ActiveTouchCount);

        // 最初の移動で初期ピンチ基準距離（100px）と中心点を記録
        var initResult = tracker.OnTouchMove(2, new Point(200, 200), currentZoom: 1.0, minZoom: 0.1, maxZoom: 5.0, horizontalOffset: 50, verticalOffset: 50);
        Assert.Equal(TouchGestureType.None, initResult.GestureType);

        // 指を広げて距離を200pxに拡大（倍率2.0倍）
        var zoomResult = tracker.OnTouchMove(2, new Point(300, 200), currentZoom: 1.0, minZoom: 0.1, maxZoom: 5.0, horizontalOffset: 50, verticalOffset: 50);
        Assert.Equal(TouchGestureType.Zoom, zoomResult.GestureType);
        Assert.True(zoomResult.NewZoom > 1.0);
    }

    [Fact]
    public void CrossBoundaryPinch_OperatesSeamlesslyAcrossPageAndMargin()
    {
        var tracker = new DetailTouchTracker();

        // 指1を余白座標（20, 20）、指2をページ内座標（300, 300）に接地
        tracker.OnTouchDown(1, new Point(20, 20));
        tracker.OnTouchDown(2, new Point(300, 300));
        Assert.Equal(2, tracker.ActiveTouchCount);

        // 初期基準の確立
        tracker.OnTouchMove(1, new Point(20, 20), currentZoom: 1.0, minZoom: 0.05, maxZoom: 32.0, horizontalOffset: 0, verticalOffset: 0);

        // 余白側の指を外側へ移動させてピンチ拡大
        var result = tracker.OnTouchMove(1, new Point(10, 10), currentZoom: 1.0, minZoom: 0.05, maxZoom: 32.0, horizontalOffset: 0, verticalOffset: 0);
        Assert.Equal(TouchGestureType.Zoom, result.GestureType);
        Assert.True(result.NewZoom > 1.0);
    }

    [Fact]
    public void TouchUp_ClearsActiveStateAndResetsPinch()
    {
        var tracker = new DetailTouchTracker();
        tracker.OnTouchDown(1, new Point(50, 50));
        tracker.OnTouchDown(2, new Point(150, 50));
        Assert.Equal(2, tracker.ActiveTouchCount);

        // 1本離脱
        tracker.OnTouchUp(1);
        Assert.Equal(1, tracker.ActiveTouchCount);
        Assert.False(tracker.IsActiveTouch(1));
        Assert.True(tracker.IsActiveTouch(2));

        // 2本目離脱
        tracker.OnTouchUp(2);
        Assert.Equal(0, tracker.ActiveTouchCount);
        Assert.False(tracker.IsPanningStarted);
    }

    [Fact]
    public void StylusSuppression_RejectsTouchesDuringStylusActivity()
    {
        var tracker = new DetailTouchTracker();

        // スタイラス接地中
        tracker.SetStylusStateForTesting(isTouching: true, isInRange: true);
        Assert.True(tracker.IsStylusSuppressed());

        // スタイラス使用中はタッチ受付を拒否
        bool accepted = tracker.OnTouchDown(1, new Point(100, 100));
        Assert.False(accepted);
        Assert.Equal(0, tracker.ActiveTouchCount);
    }

    [Fact]
    public void StylusActivity_PurgesExistingActiveTouches()
    {
        var tracker = new DetailTouchTracker();

        // タッチ操作中
        tracker.OnTouchDown(1, new Point(50, 50));
        Assert.Equal(1, tracker.ActiveTouchCount);

        // スタイラス接地通知によりタッチをパージ
        tracker.NotifyStylusActivity(isTouching: true, isInRange: true);
        Assert.Equal(0, tracker.ActiveTouchCount);
        Assert.False(tracker.IsPanningStarted);
    }

    [Fact]
    public void StylusSuppression_CooldownPeriodWorks()
    {
        var tracker = new DetailTouchTracker();

        // スタイラス離脱後50ms（150msクールダウン以内）
        tracker.SetStylusStateForTesting(isTouching: false, isInRange: false, DateTime.UtcNow.AddMilliseconds(-50));
        Assert.True(tracker.IsStylusSuppressed());
        Assert.False(tracker.OnTouchDown(1, new Point(50, 50)));

        // スタイラス離脱後200ms（クールダウン経過）
        tracker.SetStylusStateForTesting(isTouching: false, isInRange: false, DateTime.UtcNow.AddMilliseconds(-200));
        Assert.False(tracker.IsStylusSuppressed());
        Assert.True(tracker.OnTouchDown(1, new Point(50, 50)));
        Assert.Equal(1, tracker.ActiveTouchCount);
    }

    [Fact]
    public void IsTouchPromotedMouseEvent_DetectsActiveTouches()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var tracker = new DetailTouchTracker();
            var mouseArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0);

            Assert.False(tracker.IsTouchPromotedMouseEvent(mouseArgs));

            tracker.OnTouchDown(1, new Point(50, 50));
            Assert.True(tracker.IsTouchPromotedMouseEvent(mouseArgs));

            tracker.OnTouchUp(1);
            Assert.False(tracker.IsTouchPromotedMouseEvent(mouseArgs));
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }
}
