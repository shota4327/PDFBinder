namespace PDFBinder.Core.Models;

/// <summary>
/// PDFページの回転角度を表す列挙型
/// </summary>
public enum PageRotation
{
    /// <summary>回転なし（0度）</summary>
    Rotate0 = 0,

    /// <summary>時計回りに90度</summary>
    Rotate90 = 90,

    /// <summary>180度</summary>
    Rotate180 = 180,

    /// <summary>反時計回りに90度（270度）</summary>
    Rotate270 = 270
}

/// <summary>
/// <see cref="PageRotation"/> の拡張メソッド群
/// </summary>
public static class PageRotationExtensions
{
    /// <summary>
    /// 時計回りに90度回転した角度を取得します。
    /// </summary>
    public static PageRotation RotateClockwise(this PageRotation current) => current switch
    {
        PageRotation.Rotate0 => PageRotation.Rotate90,
        PageRotation.Rotate90 => PageRotation.Rotate180,
        PageRotation.Rotate180 => PageRotation.Rotate270,
        PageRotation.Rotate270 => PageRotation.Rotate0,
        _ => PageRotation.Rotate0
    };

    /// <summary>
    /// 反時計回りに90度回転した角度を取得します。
    /// </summary>
    public static PageRotation RotateCounterClockwise(this PageRotation current) => current switch
    {
        PageRotation.Rotate0 => PageRotation.Rotate270,
        PageRotation.Rotate90 => PageRotation.Rotate0,
        PageRotation.Rotate180 => PageRotation.Rotate90,
        PageRotation.Rotate270 => PageRotation.Rotate180,
        _ => PageRotation.Rotate0
    };

    /// <summary>
    /// 180度回転した角度を取得します。
    /// </summary>
    public static PageRotation Rotate180(this PageRotation current) => current switch
    {
        PageRotation.Rotate0 => PageRotation.Rotate180,
        PageRotation.Rotate90 => PageRotation.Rotate270,
        PageRotation.Rotate180 => PageRotation.Rotate0,
        PageRotation.Rotate270 => PageRotation.Rotate90,
        _ => PageRotation.Rotate0
    };

    /// <summary>
    /// 角度（度数法）から <see cref="PageRotation"/> に変換します。
    /// </summary>
    public static PageRotation FromDegrees(int degrees)
    {
        int normalized = ((degrees % 360) + 360) % 360;
        return normalized switch
        {
            90 => PageRotation.Rotate90,
            180 => PageRotation.Rotate180,
            270 => PageRotation.Rotate270,
            _ => PageRotation.Rotate0
        };
    }
}
