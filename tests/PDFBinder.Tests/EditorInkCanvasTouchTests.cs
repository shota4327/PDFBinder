using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PDFBinder.App.Controls;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="EditorInkCanvas"/> および <see cref="PenOnlyDynamicRenderer"/> のタッチ・スタイラス制御テスト
/// </summary>
public class EditorInkCanvasTouchTests
{
    [Fact]
    public void EditorInkCanvas_UsesPenOnlyDynamicRenderer()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();
            Assert.NotNull(canvas.CurrentDynamicRenderer);
            Assert.IsType<PenOnlyDynamicRenderer>(canvas.CurrentDynamicRenderer);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Fact]
    public void EditorInkCanvas_EditingMode_MatchesToolMode()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();

            // 初期ツールは通常ペン
            Assert.Equal(EditorToolMode.Pen, canvas.ToolMode);
            Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);

            // 蛍光ペン
            canvas.ToolMode = EditorToolMode.Highlighter;
            Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);

            // 全消しゴム
            canvas.ToolMode = EditorToolMode.EraserStroke;
            Assert.Equal(InkCanvasEditingMode.EraseByStroke, canvas.EditingMode);

            // 部分消しゴム
            canvas.ToolMode = EditorToolMode.EraserPoint;
            Assert.Equal(InkCanvasEditingMode.EraseByPoint, canvas.EditingMode);

            // 選択ツール
            canvas.ToolMode = EditorToolMode.Select;
            Assert.Equal(InkCanvasEditingMode.Select, canvas.EditingMode);

            // 手のひら・直線ツール
            canvas.ToolMode = EditorToolMode.Hand;
            Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);

            canvas.ToolMode = EditorToolMode.StraightLine;
            Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Fact]
    public void EditorInkCanvas_PurgesActiveTouches_WhenStylusActivityOccurs()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas { ToolMode = EditorToolMode.Pen };

            // 手指タッチが登録され、一時的にEditingModeがNoneになった状態を模倣
            canvas.AddTouchPointForTesting(1, new Point(100, 100));
            Assert.Equal(1, canvas.ActiveTouchPointCount);
            Assert.Equal(InkCanvasEditingMode.None, canvas.EditingMode);

            // スタイラス検知によるパージ処理を実行
            canvas.PurgeActiveTouches();

            // タッチが即時解除され、ペンの描画モード（Ink）に復帰していることを確認
            Assert.Equal(0, canvas.ActiveTouchPointCount);
            Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);
            Assert.False(canvas.IsPanningStarted);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Fact]
    public void EditorInkCanvas_TouchSlop_PreventsMicroMovementPan()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();
            canvas.AddTouchPointForTesting(1, new Point(50, 50));

            // 閾値（8.0px）未満の微小移動（手のひら接地時のブレなど）
            bool movedSmall = canvas.ProcessOneFingerPanForTesting(1, new Point(53, 53));
            Assert.False(movedSmall);
            Assert.False(canvas.IsPanningStarted);

            // 閾値以上の明確なスワイプ移動
            bool movedLarge = canvas.ProcessOneFingerPanForTesting(1, new Point(60, 50));
            Assert.True(movedLarge);
            Assert.True(canvas.IsPanningStarted);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Fact]
    public void EditorInkCanvas_StylusSuppression_CooldownPeriodWorks()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();
            Assert.Equal(150, EditorInkCanvas.StylusSuppressionCooldownMs);

            // スタイラス接地中・ホバー中は抑止
            canvas.SetStylusStateForTesting(isTouching: true, isInRange: false);
            Assert.True(canvas.IsStylusSuppressed());

            canvas.SetStylusStateForTesting(isTouching: false, isInRange: true);
            Assert.True(canvas.IsStylusSuppressed());

            // 離脱直後（クールダウン150ms以内）は抑止継続
            canvas.SetStylusStateForTesting(isTouching: false, isInRange: false, DateTime.UtcNow.AddMilliseconds(-50));
            Assert.True(canvas.IsStylusSuppressed());

            // クールダウン経過後（200ms経過）はタッチ受付再開
            canvas.SetStylusStateForTesting(isTouching: false, isInRange: false, DateTime.UtcNow.AddMilliseconds(-200));
            Assert.False(canvas.IsStylusSuppressed());
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Fact]
    public void EditorInkCanvas_IsTouchPromotedMouseEvent_IdentifiesTouchAccurately()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas();
            var mouseArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0);

            // タッチが存在しない初期状態では通常のマウス操作と判定
            Assert.False(canvas.IsTouchPromotedMouseEvent(mouseArgs));

            // 手指タッチが登録されている場合はタッチ昇格イベントと判定
            canvas.AddTouchPointForTesting(1, new Point(50, 50));
            Assert.True(canvas.IsTouchPromotedMouseEvent(mouseArgs));
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Fact]
    public void EditorInkCanvas_MousePan_TracksDeltaAndUpdatesStartPoint()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas { ToolMode = EditorToolMode.Hand };
            var scrollViewer = new ScrollViewer();
            canvas.SetParentScrollViewerForTesting(scrollViewer);

            // パン開始
            var startPoint = new Point(100, 100);
            canvas.StartMousePan(startPoint);
            Assert.Equal(startPoint, canvas.PanStartPoint);

            // 1回目のドラッグ移動（X方向に10px、Y方向に20px移動）
            var movePoint1 = new Point(90, 80);
            var (dx1, dy1) = canvas.ProcessMousePan(movePoint1);
            Assert.Equal(movePoint1, canvas.PanStartPoint);
            Assert.Equal(10.0, dx1);
            Assert.Equal(20.0, dy1);

            // 2回目のドラッグ移動（さらにX方向に5px移動、Yは変化なし）
            // 基準点が前回位置に更新されているため、累積加算ではなく差分5pxのみが計算される
            var movePoint2 = new Point(85, 80);
            var (dx2, dy2) = canvas.ProcessMousePan(movePoint2);
            Assert.Equal(movePoint2, canvas.PanStartPoint);
            Assert.Equal(5.0, dx2);
            Assert.Equal(0.0, dy2);

            // パン終了
            canvas.EndMousePan();
            Assert.Null(canvas.PanStartPoint);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }
}
