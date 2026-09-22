using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Helpers;

/// <summary>
/// 手書きインクストロークの幾何学的座標変換・追従回転を行うヘルパークラス
/// </summary>
public static class InkTransformHelper
{
    /// <summary>
    /// 差分回転角度と現在のページ表示寸法に基づいて、手書きストロークコレクションを一括行列演算で高速に幾何学変換します。
    /// </summary>
    /// <param name="strokes">変換対象のストロークコレクション</param>
    /// <param name="deltaRotation">差分回転角度（時計回り）</param>
    /// <param name="currentDisplayWidth">回転前のページ表示幅</param>
    /// <param name="currentDisplayHeight">回転前のページ表示高さ</param>
    public static void RotateStrokes(
        StrokeCollection? strokes,
        PageRotation deltaRotation,
        double currentDisplayWidth,
        double currentDisplayHeight)
    {
        if (strokes == null || strokes.Count == 0 || deltaRotation == PageRotation.Rotate0)
        {
            return;
        }

        if (currentDisplayWidth <= 0 || currentDisplayHeight <= 0)
        {
            return;
        }

        var matrix = CreateRotationMatrix(deltaRotation, currentDisplayWidth, currentDisplayHeight);
        strokes.Transform(matrix, false);

        if (deltaRotation is PageRotation.Rotate90 or PageRotation.Rotate270)
        {
            foreach (var stroke in strokes)
            {
                var attr = stroke.DrawingAttributes;
                if (attr.Width != attr.Height)
                {
                    (attr.Width, attr.Height) = (attr.Height, attr.Width);
                }
            }
        }
    }

    /// <summary>
    /// 差分回転角度と元の寸法に応じた座標変換行列を作成します。
    /// </summary>
    public static System.Windows.Media.Matrix CreateRotationMatrix(
        PageRotation deltaRotation,
        double currentWidth,
        double currentHeight)
    {
        return deltaRotation switch
        {
            PageRotation.Rotate90 => new System.Windows.Media.Matrix(0, 1, -1, 0, currentHeight, 0),
            PageRotation.Rotate180 => new System.Windows.Media.Matrix(-1, 0, 0, -1, currentWidth, currentHeight),
            PageRotation.Rotate270 => new System.Windows.Media.Matrix(0, -1, 1, 0, 0, currentWidth),
            _ => System.Windows.Media.Matrix.Identity
        };
    }

    /// <summary>
    /// 単一の座標点を指定された回転角度と元の寸法に基づいて変換します。
    /// </summary>
    /// <param name="x">元のX座標</param>
    /// <param name="y">元のY座標</param>
    /// <param name="deltaRotation">差分回転角度（時計回り）</param>
    /// <param name="currentWidth">元の幅</param>
    /// <param name="currentHeight">元の高さ</param>
    /// <returns>変換後の(X, Y)座標</returns>
    public static (double X, double Y) TransformPoint(
        double x,
        double y,
        PageRotation deltaRotation,
        double currentWidth,
        double currentHeight)
    {
        return deltaRotation switch
        {
            PageRotation.Rotate90 => (currentHeight - y, x),
            PageRotation.Rotate180 => (currentWidth - x, currentHeight - y),
            PageRotation.Rotate270 => (y, currentWidth - x),
            _ => (x, y)
        };
    }
}
