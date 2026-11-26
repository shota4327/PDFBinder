using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDFBinder.App.Services;

/// <summary>
/// プリンターデバイスコンテキストから取得した物理用紙・解像度メトリクス
/// </summary>
internal readonly record struct DevicePageMetrics(
    int PhysicalWidth,
    int PhysicalHeight,
    int PhysicalOffsetX,
    int PhysicalOffsetY,
    int DpiX,
    int DpiY);

/// <summary>
/// デバイス座標系における整数矩形
/// </summary>
internal readonly record struct DeviceRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>
/// GDI 印刷向けの物理用紙基準座標計算および 24bpp 非透過 RGB ビットマップ変換ヘルパー
/// </summary>
internal static class GdiBitmapConverter
{
    /// <summary>
    /// プリンターデバイスコンテキスト（HDC）から物理用紙および解像度メトリクスを読み取ります。
    /// </summary>
    public static DevicePageMetrics ReadDevicePageMetrics(nint hdc)
    {
        int physW = Math.Max(1, GdiNativeMethods.GetDeviceCaps(hdc, GdiNativeMethods.PHYSICALWIDTH));
        int physH = Math.Max(1, GdiNativeMethods.GetDeviceCaps(hdc, GdiNativeMethods.PHYSICALHEIGHT));
        int offX = Math.Max(0, GdiNativeMethods.GetDeviceCaps(hdc, GdiNativeMethods.PHYSICALOFFSETX));
        int offY = Math.Max(0, GdiNativeMethods.GetDeviceCaps(hdc, GdiNativeMethods.PHYSICALOFFSETY));
        int dpiX = Math.Max(1, GdiNativeMethods.GetDeviceCaps(hdc, GdiNativeMethods.LOGPIXELSX));
        int dpiY = Math.Max(1, GdiNativeMethods.GetDeviceCaps(hdc, GdiNativeMethods.LOGPIXELSY));

        return new DevicePageMetrics(physW, physH, offX, offY, dpiX, dpiY);
    }

    /// <summary>
    /// 物理用紙全体（印刷可能領域原点に対して -PhysicalOffsetX, -PhysicalOffsetY）を基準として
    /// 正規化スロット領域に対応するデバイス座標矩形を算出します。
    /// </summary>
    public static DeviceRect CalculateSlotRect(DevicePageMetrics metrics, Rect normalizedBounds)
    {
        int left = (int)Math.Round(-metrics.PhysicalOffsetX + normalizedBounds.X * metrics.PhysicalWidth);
        int top = (int)Math.Round(-metrics.PhysicalOffsetY + normalizedBounds.Y * metrics.PhysicalHeight);
        int right = (int)Math.Round(-metrics.PhysicalOffsetX + (normalizedBounds.X + normalizedBounds.Width) * metrics.PhysicalWidth);
        int bottom = (int)Math.Round(-metrics.PhysicalOffsetY + (normalizedBounds.Y + normalizedBounds.Height) * metrics.PhysicalHeight);

        int width = Math.Max(1, right - left);
        int height = Math.Max(1, bottom - top);
        return new DeviceRect(left, top, width, height);
    }

    /// <summary>
    /// スロット矩形と元ページ寸法・デバイス解像度から、アスペクト比を維持して中央内接する描画矩形を算出します。
    /// </summary>
    public static DeviceRect CalculateAspectFitRect(
        DeviceRect slot,
        double pageWidth,
        double pageHeight,
        int dpiX,
        int dpiY)
    {
        if (pageWidth <= 0 || pageHeight <= 0 || dpiX <= 0 || dpiY <= 0 || slot.Width <= 0 || slot.Height <= 0)
        {
            return slot;
        }

        double scaleX = slot.Width / (pageWidth * dpiX);
        double scaleY = slot.Height / (pageHeight * dpiY);
        double scale = Math.Min(scaleX, scaleY);

        int drawW = Math.Max(1, (int)Math.Round(pageWidth * scale * dpiX));
        int drawH = Math.Max(1, (int)Math.Round(pageHeight * scale * dpiY));
        int drawLeft = slot.Left + (slot.Width - drawW) / 2;
        int drawTop = slot.Top + (slot.Height - drawH) / 2;

        return new DeviceRect(drawLeft, drawTop, drawW, drawH);
    }

    /// <summary>
    /// 任意の <see cref="BitmapSource"/> を白背景と合成した DWORD アライン済み 24bpp トップダウン BGR 配列へ変換します。
    /// </summary>
    public static byte[] ConvertToBgr24TopDown(
        BitmapSource source,
        out int width,
        out int height,
        out int stride)
    {
        ArgumentNullException.ThrowIfNull(source);

        width = Math.Max(1, source.PixelWidth);
        height = Math.Max(1, source.PixelHeight);
        stride = ((width * 3) + 3) & ~3;
        byte[] output = new byte[stride * height];

        if (source.Format == PixelFormats.Bgr24)
        {
            source.CopyPixels(output, stride, 0);
            return output;
        }

        bool isPremultiplied = source.Format == PixelFormats.Pbgra32;
        BitmapSource normalized = (source.Format == PixelFormats.Bgra32 || isPremultiplied)
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int srcStride = width * 4;
        byte[] srcBytes = new byte[srcStride * height];
        normalized.CopyPixels(srcBytes, srcStride, 0);

        CompositeOnWhiteToBgr24(srcBytes, srcStride, output, stride, width, height, isPremultiplied);
        return output;
    }

    /// <summary>
    /// 32bpp BGRA/PBGRA ピクセル配列を白背景へ合成し、24bpp BGR 出力バッファへ書き込みます。
    /// </summary>
    private static void CompositeOnWhiteToBgr24(
        byte[] srcBytes,
        int srcStride,
        byte[] dstBytes,
        int dstStride,
        int width,
        int height,
        bool isPremultiplied)
    {
        for (int y = 0; y < height; y++)
        {
            int srcRow = y * srcStride;
            int dstRow = y * dstStride;

            for (int x = 0; x < width; x++)
            {
                int si = srcRow + x * 4;
                int di = dstRow + x * 3;
                byte a = srcBytes[si + 3];

                if (a == 255)
                {
                    dstBytes[di] = srcBytes[si];
                    dstBytes[di + 1] = srcBytes[si + 1];
                    dstBytes[di + 2] = srcBytes[si + 2];
                }
                else if (a == 0)
                {
                    dstBytes[di] = 255;
                    dstBytes[di + 1] = 255;
                    dstBytes[di + 2] = 255;
                }
                else if (isPremultiplied)
                {
                    int invA = 255 - a;
                    dstBytes[di] = (byte)Math.Min(255, srcBytes[si] + invA);
                    dstBytes[di + 1] = (byte)Math.Min(255, srcBytes[si + 1] + invA);
                    dstBytes[di + 2] = (byte)Math.Min(255, srcBytes[si + 2] + invA);
                }
                else
                {
                    int invA = 255 - a;
                    dstBytes[di] = (byte)((srcBytes[si] * a + 255 * invA + 127) / 255);
                    dstBytes[di + 1] = (byte)((srcBytes[si + 1] * a + 255 * invA + 127) / 255);
                    dstBytes[di + 2] = (byte)((srcBytes[si + 2] * a + 255 * invA + 127) / 255);
                }
            }
        }
    }
}
