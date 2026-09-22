using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDFBinder.Core.Helpers;

/// <summary>
/// ビットマップ画像の幾何学的変換（回転など）を高速に行うヘルパークラス
/// </summary>
public static class BitmapTransformHelper
{
    /// <summary>
    /// 指定されたビットマップ画像を指定角度（時計回り）で幾何学的に回転変換した新しいビットマップを生成します。
    /// </summary>
    /// <param name="source">回転元のビットマップ画像</param>
    /// <param name="deltaDegrees">回転角度（度単位、例: 90, 180, 270）</param>
    /// <returns>回転後のフリーズ済みビットマップ画像。sourceがnullまたはdeltaDegreesが0の場合は元の画像をそのまま返却します。</returns>
    public static BitmapSource? CreateRotatedBitmap(BitmapSource? source, int deltaDegrees)
    {
        if (source == null)
        {
            return null;
        }

        int normalizedDeg = ((deltaDegrees % 360) + 360) % 360;
        if (normalizedDeg == 0)
        {
            return source;
        }

        var rotated = new TransformedBitmap(source, new RotateTransform(normalizedDeg));
        rotated.Freeze();
        return rotated;
    }
}
