using System.Windows.Ink;
using System.Windows.Input;

namespace PDFBinder.Core.Helpers;

/// <summary>
/// 手書きインクストロークを指定された境界線で切断・分割し、それぞれのページに割り当てるヘルパークラス
/// </summary>
public static class StrokeSplitHelper
{
    /// <summary>
    /// ストロークコレクションを垂直または水平の境界線で2つに分割します。
    /// </summary>
    /// <param name="strokes">分割対象のストロークコレクション</param>
    /// <param name="splitHorizontally">左右に2分割する場合は true（X軸で切断）、上下に2分割する場合は false（Y軸で切断）</param>
    /// <param name="splitOffset">境界線のオフセット位置（幅の半分または高さの半分）</param>
    /// <returns>前半（左または上）と後半（右または下）のストロークコレクションのタプル</returns>
    public static (StrokeCollection FirstPart, StrokeCollection SecondPart) SplitStrokes(
        StrokeCollection? strokes,
        bool splitHorizontally,
        double splitOffset)
    {
        var firstPart = new StrokeCollection();
        var secondPart = new StrokeCollection();

        if (strokes == null || strokes.Count == 0 || splitOffset <= 0)
        {
            return (firstPart, secondPart);
        }

        foreach (var stroke in strokes)
        {
            SplitSingleStroke(stroke, splitHorizontally, splitOffset, firstPart, secondPart);
        }

        ShiftSecondPartCoordinates(secondPart, splitHorizontally, splitOffset);
        return (firstPart, secondPart);
    }

    /// <summary>
    /// 単一ストロークを境界線で切断し、前半と後半のリストへ追加します。
    /// </summary>
    private static void SplitSingleStroke(
        Stroke stroke,
        bool splitHorizontally,
        double splitOffset,
        StrokeCollection firstPart,
        StrokeCollection secondPart)
    {
        if (stroke.StylusPoints.Count == 0) return;

        var currentPoints = new StylusPointCollection();
        bool? currentIsFirst = null;

        for (int i = 0; i < stroke.StylusPoints.Count; i++)
        {
            var pt = stroke.StylusPoints[i];
            bool isFirst = IsInFirstPart(pt, splitHorizontally, splitOffset);

            if (currentIsFirst == null)
            {
                currentIsFirst = isFirst;
                currentPoints.Add(pt);
                continue;
            }

            if (currentIsFirst == isFirst)
            {
                currentPoints.Add(pt);
            }
            else
            {
                var prevPt = stroke.StylusPoints[i - 1];
                var cutPt = CalculateCutPoint(prevPt, pt, splitHorizontally, splitOffset);
                currentPoints.Add(cutPt);
                AddSegment(stroke, currentPoints, currentIsFirst.Value, firstPart, secondPart);

                currentPoints = new StylusPointCollection { cutPt, pt };
                currentIsFirst = isFirst;
            }
        }

        if (currentPoints.Count > 0 && currentIsFirst.HasValue)
        {
            AddSegment(stroke, currentPoints, currentIsFirst.Value, firstPart, secondPart);
        }
    }

    /// <summary>
    /// 指定された点が前半（左または上）に属するかどうかを判定します。
    /// </summary>
    private static bool IsInFirstPart(StylusPoint pt, bool splitHorizontally, double splitOffset)
    {
        double val = splitHorizontally ? pt.X : pt.Y;
        return val <= splitOffset;
    }

    /// <summary>
    /// 境界線との交点を線形補間によって算出します。
    /// </summary>
    private static StylusPoint CalculateCutPoint(
        StylusPoint p1,
        StylusPoint p2,
        bool splitHorizontally,
        double splitOffset)
    {
        double v1 = splitHorizontally ? p1.X : p1.Y;
        double v2 = splitHorizontally ? p2.X : p2.Y;
        double t = Math.Abs(v2 - v1) < 1e-6 ? 0.5 : (splitOffset - v1) / (v2 - v1);
        t = Math.Clamp(t, 0.0, 1.0);

        double cutX = splitHorizontally ? splitOffset : p1.X + t * (p2.X - p1.X);
        double cutY = splitHorizontally ? p1.Y + t * (p2.Y - p1.Y) : splitOffset;
        float pressure = (float)(p1.PressureFactor + t * (p2.PressureFactor - p1.PressureFactor));

        return new StylusPoint(cutX, cutY, Math.Clamp(pressure, 0.0f, 1.0f));
    }

    /// <summary>
    /// ストロークセグメントを新規ストロークとして対応するコレクションに追加します。
    /// </summary>
    private static void AddSegment(
        Stroke originalStroke,
        StylusPointCollection points,
        bool isFirst,
        StrokeCollection firstPart,
        StrokeCollection secondPart)
    {
        if (points.Count == 0) return;
        var newStroke = new Stroke(points, originalStroke.DrawingAttributes.Clone());
        if (isFirst)
        {
            firstPart.Add(newStroke);
        }
        else
        {
            secondPart.Add(newStroke);
        }
    }

    /// <summary>
    /// 後半パート（右または下）の全ストロークの座標からオフセットを減算し、ローカル座標系にシフトします。
    /// </summary>
    private static void ShiftSecondPartCoordinates(
        StrokeCollection strokes,
        bool splitHorizontally,
        double splitOffset)
    {
        for (int i = 0; i < strokes.Count; i++)
        {
            var oldStroke = strokes[i];
            var shiftedPoints = new StylusPointCollection(oldStroke.StylusPoints.Count);
            foreach (var pt in oldStroke.StylusPoints)
            {
                double newX = splitHorizontally ? Math.Max(0, pt.X - splitOffset) : pt.X;
                double newY = splitHorizontally ? pt.Y : Math.Max(0, pt.Y - splitOffset);
                shiftedPoints.Add(new StylusPoint(newX, newY, pt.PressureFactor));
            }

            strokes[i] = new Stroke(shiftedPoints, oldStroke.DrawingAttributes.Clone());
        }
    }
}
