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

    /// <summary>
    /// 元のビットマップ画像からピクセルデータをメモリに抽出し、デコーダーや生成スレッドへの依存を持たない
    /// 独立したフリーズ済みのビットマップ画像を生成します。
    /// </summary>
    /// <param name="source">複製元のビットマップ画像</param>
    /// <returns>スレッド非依存でフリーズ済みの新しいビットマップ画像。sourceがnullの場合はnullを返却します。</returns>
    public static BitmapSource? CreateDetachedBitmap(BitmapSource? source)
    {
        if (source == null)
        {
            return null;
        }

        int width = source.PixelWidth;
        int height = source.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return source;
        }

        int stride = (width * source.Format.BitsPerPixel + 7) / 8;
        byte[] pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        var detached = BitmapSource.Create(
            width,
            height,
            source.DpiX,
            source.DpiY,
            source.Format,
            source.Palette,
            pixels,
            stride);

        detached.Freeze();
        return detached;
    }
}
