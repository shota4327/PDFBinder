using System;

namespace PDFBinder.App.Helpers;

/// <summary>
/// アプリケーション全体のズーム倍率計算およびスナップ目盛り遷移を管理するヘルパークラス
/// </summary>
public static class ZoomHelper
{
    /// <summary>
    /// 最小ズーム倍率（50%）
    /// </summary>
    public const double MinZoom = 0.5;

    /// <summary>
    /// 最大ズーム倍率（3200%）
    /// </summary>
    public const double MaxZoom = 32.0;

    /// <summary>
    /// ズームイン・ズームアウトで使用する標準スナップ目盛り倍率一覧（50%〜3200%）
    /// </summary>
    public static readonly double[] ZoomSnapSteps =
    [
        0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0,
        2.5, 3.0, 3.5, 4.0,
        5.0, 6.0, 7.0, 8.0,
        10.0, 12.0, 14.0, 16.0,
        20.0, 24.0, 28.0, 32.0
    ];

    /// <summary>
    /// 指定されたズーム倍率から1段階拡大した次の標準目盛り倍率を取得します。
    /// </summary>
    /// <param name="currentZoom">現在のズーム倍率</param>
    /// <returns>1段階拡大した目標ズーム倍率（最大 MaxZoom）</returns>
    public static double GetNextZoomIn(double currentZoom)
    {
        foreach (double step in ZoomSnapSteps)
        {
            if (step > currentZoom + 0.001)
            {
                return step;
            }
        }
        return MaxZoom;
    }

    /// <summary>
    /// 指定されたズーム倍率から1段階縮小した前の標準目盛り倍率を取得します。
    /// </summary>
    /// <param name="currentZoom">現在のズーム倍率</param>
    /// <returns>1段階縮小した目標ズーム倍率（最小 MinZoom）</returns>
    public static double GetNextZoomOut(double currentZoom)
    {
        for (int i = ZoomSnapSteps.Length - 1; i >= 0; i--)
        {
            if (ZoomSnapSteps[i] < currentZoom - 0.001)
            {
                return ZoomSnapSteps[i];
            }
        }
        return MinZoom;
    }
}
