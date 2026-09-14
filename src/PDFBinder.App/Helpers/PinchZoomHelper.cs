using System.Windows;

namespace PDFBinder.App.Helpers;

/// <summary>
/// ピンチズーム計算結果を保持するレコード
/// </summary>
/// <param name="NewZoom">更新後のズーム倍率</param>
/// <param name="TargetHorizontalOffset">追従先の水平スクロールオフセット</param>
/// <param name="TargetVerticalOffset">追従先の垂直スクロールオフセット</param>
public readonly record struct PinchZoomResult(
    double NewZoom,
    double TargetHorizontalOffset,
    double TargetVerticalOffset);

/// <summary>
/// ピンチ操作によるズーム倍率および中心点追従スクロールオフセットを計算するヘルパークラス
/// </summary>
public static class PinchZoomHelper
{
    /// <summary>最小ズーム倍率（50%）</summary>
    public const double DefaultMinZoom = 0.5;

    /// <summary>最大ズーム倍率（3200%）</summary>
    public const double DefaultMaxZoom = 32.0;

    /// <summary>
    /// ピンチ操作による新しいズーム倍率および中心点追従スクロールオフセットを算出します。
    /// </summary>
    /// <param name="currentZoom">現在のズーム倍率</param>
    /// <param name="previousDistance">前回の2点間距離</param>
    /// <param name="currentDistance">現在の2点間距離</param>
    /// <param name="previousCenter">前回のピンチ中心座標（ビューポート基準）</param>
    /// <param name="currentCenter">現在のピンチ中心座標（ビューポート基準）</param>
    /// <param name="horizontalOffset">現在の水平スクロールオフセット</param>
    /// <param name="verticalOffset">現在の垂直スクロールオフセット</param>
    /// <param name="minZoom">最小許容ズーム倍率</param>
    /// <param name="maxZoom">最大許容ズーム倍率</param>
    /// <returns>計算後のズーム倍率および目標スクロール位置</returns>
    public static PinchZoomResult Calculate(
        double currentZoom,
        double previousDistance,
        double currentDistance,
        Point previousCenter,
        Point currentCenter,
        double horizontalOffset,
        double verticalOffset,
        double minZoom = DefaultMinZoom,
        double maxZoom = DefaultMaxZoom)
    {
        if (previousDistance <= 1.0 || currentDistance <= 1.0 || currentZoom <= 0.0)
        {
            return new PinchZoomResult(currentZoom, horizontalOffset, verticalOffset);
        }

        double scaleFactor = currentDistance / previousDistance;
        double targetZoom = Math.Clamp(currentZoom * scaleFactor, minZoom, maxZoom);
        double actualScale = targetZoom / currentZoom;

        // ピンチ中心点（画面上の指の中間）を基準とした幾何学的オフセット補正
        double newHorizontalOffset = (horizontalOffset + previousCenter.X) * actualScale - currentCenter.X;
        double newVerticalOffset = (verticalOffset + previousCenter.Y) * actualScale - currentCenter.Y;

        return new PinchZoomResult(
            targetZoom,
            Math.Max(0.0, newHorizontalOffset),
            Math.Max(0.0, newVerticalOffset));
    }
}
