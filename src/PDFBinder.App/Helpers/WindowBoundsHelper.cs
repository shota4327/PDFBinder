using System;

namespace PDFBinder.App.Helpers;

/// <summary>
/// ウィンドウサイズおよび表示領域の境界調整を行うヘルパークラス
/// </summary>
public static class WindowBoundsHelper
{
    /// <summary>
    /// デフォルトのウィンドウ幅
    /// </summary>
    public const double DefaultWidth = 1100;

    /// <summary>
    /// デフォルトのウィンドウ高さ
    /// </summary>
    public const double DefaultHeight = 760;

    /// <summary>
    /// 最小ウィンドウ幅
    /// </summary>
    public const double DefaultMinWidth = 750;

    /// <summary>
    /// 最小ウィンドウ高さ
    /// </summary>
    public const double DefaultMinHeight = 500;

    /// <summary>
    /// 保存されたウィンドウサイズを作業領域（画面解像度・タスクバー除外領域）および最小サイズに基づき調整します。
    /// </summary>
    /// <param name="savedWidth">前回保存されたウィンドウ幅</param>
    /// <param name="savedHeight">前回保存されたウィンドウ高さ</param>
    /// <param name="workAreaWidth">画面作業領域の幅</param>
    /// <param name="workAreaHeight">画面作業領域の高さ</param>
    /// <param name="minWidth">許容される最小幅（既定: 750）</param>
    /// <param name="minHeight">許容される最小高さ（既定: 500）</param>
    /// <returns>調整後の幅と高さ</returns>
    public static (double Width, double Height) AdjustBounds(
        double savedWidth,
        double savedHeight,
        double workAreaWidth,
        double workAreaHeight,
        double minWidth = DefaultMinWidth,
        double minHeight = DefaultMinHeight)
    {
        // 1. 無効値（0以下、NaN、無限大）の検証とデフォルト値適用
        double targetWidth = IsValidDimension(savedWidth) ? savedWidth : DefaultWidth;
        double targetHeight = IsValidDimension(savedHeight) ? savedHeight : DefaultHeight;

        // 2. 作業領域（画面サイズ）におさまるよう上限調整
        if (workAreaWidth > 0)
        {
            targetWidth = Math.Min(targetWidth, workAreaWidth);
        }

        if (workAreaHeight > 0)
        {
            targetHeight = Math.Min(targetHeight, workAreaHeight);
        }

        // 3. 最小サイズ未満の場合は下限調整（画面作業領域自体が最小サイズ以上のときのみ）
        if (workAreaWidth >= minWidth)
        {
            targetWidth = Math.Max(targetWidth, minWidth);
        }

        if (workAreaHeight >= minHeight)
        {
            targetHeight = Math.Max(targetHeight, minHeight);
        }

        return (targetWidth, targetHeight);
    }

    /// <summary>
    /// 寸法値が正の有限数値であるかを検証します。
    /// </summary>
    private static bool IsValidDimension(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}
