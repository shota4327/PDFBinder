using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
}
