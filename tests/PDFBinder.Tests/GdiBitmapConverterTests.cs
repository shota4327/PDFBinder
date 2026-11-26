using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// GdiBitmapConverter の物理用紙基準レイアウト計算および 24bpp 非透過 RGB 変換の単体テスト
/// </summary>
public class GdiBitmapConverterTests
{
    [Fact]
    public void CalculateSlotRect_UsesPhysicalPaperOriginAndFullSheetDimensions()
    {
        // 物理用紙 4960x7016（600 DPI A4相当）、印字可能領域オフセット (100, 120)
        var metrics = new DevicePageMetrics(4960, 7016, 100, 120, 600, 600);

        var fullSlot = GdiBitmapConverter.CalculateSlotRect(metrics, new Rect(0, 0, 1, 1));
        Assert.Equal(-100, fullSlot.Left);
        Assert.Equal(-120, fullSlot.Top);
        Assert.Equal(4960, fullSlot.Width);
        Assert.Equal(7016, fullSlot.Height);

        // 2-Up 右半分スロット (X=0.5, Width=0.5)
        var rightSlot = GdiBitmapConverter.CalculateSlotRect(metrics, new Rect(0.5, 0, 0.5, 1));
        Assert.Equal(2380, rightSlot.Left);
        Assert.Equal(-120, rightSlot.Top);
        Assert.Equal(2480, rightSlot.Width);
        Assert.Equal(7016, rightSlot.Height);
    }

    [Fact]
    public void CalculateAspectFitRect_PreservesAspectRatioForSquareAndNonSquareDpi()
    {
        // 正方DPI (600x600): 縦長スロット(1000x2000) に横長ページ (800x400)
        var slot1 = new DeviceRect(100, 200, 1000, 2000);
        var fit1 = GdiBitmapConverter.CalculateAspectFitRect(slot1, 800, 400, 600, 600);
        Assert.Equal(100, fit1.Left);
        Assert.Equal(1000, fit1.Width);
        Assert.Equal(500, fit1.Height);
        Assert.Equal(950, fit1.Top);

        // 非正方DPI (1200x600): 物理サイズ 1:1 の正方形ページはデバイス座標で 2:1 になる
        var slot2 = new DeviceRect(0, 0, 2000, 2000);
        var fit2 = GdiBitmapConverter.CalculateAspectFitRect(slot2, 500, 500, 1200, 600);
        Assert.Equal(2000, fit2.Width);
        Assert.Equal(1000, fit2.Height);
        Assert.Equal(0, fit2.Left);
        Assert.Equal(500, fit2.Top);
    }

    [Fact]
    public void ConvertToBgr24TopDown_CompositesBgra32OnWhiteAndAlignsStrideToDword()
    {
        // 幅5px（5*3=15バイト -> DWORDアラインで stride=16）
        int width = 5;
        int height = 1;
        var pixels = new byte[width * height * 4];

        // ピクセル0: 完全透明 (alpha=0) -> 白 (255, 255, 255)
        pixels[0] = 0; pixels[1] = 0; pixels[2] = 0; pixels[3] = 0;
        // ピクセル1: 半透明赤 (B=0, G=0, R=255, A=128) -> B≈127, G≈127, R=255
        pixels[4] = 0; pixels[5] = 0; pixels[6] = 255; pixels[7] = 128;
        // ピクセル2: 不透明青 (B=200, G=100, R=50, A=255)
        pixels[8] = 200; pixels[9] = 100; pixels[10] = 50; pixels[11] = 255;

        var src = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);

        byte[] bgr = GdiBitmapConverter.ConvertToBgr24TopDown(src, out int outW, out int outH, out int stride);

        Assert.Equal(5, outW);
        Assert.Equal(1, outH);
        Assert.Equal(16, stride);
        Assert.Equal(16, bgr.Length);

        // ピクセル0: 白
        Assert.Equal(255, bgr[0]);
        Assert.Equal(255, bgr[1]);
        Assert.Equal(255, bgr[2]);

        // ピクセル1: 白合成された赤
        Assert.InRange(bgr[3], (byte)126, (byte)128);
        Assert.InRange(bgr[4], (byte)126, (byte)128);
        Assert.Equal(255, bgr[5]);

        // ピクセル2: そのまま
        Assert.Equal(200, bgr[6]);
        Assert.Equal(100, bgr[7]);
        Assert.Equal(50, bgr[8]);
    }

    [Fact]
    public void ConvertToBgr24TopDown_CompositesPbgra32AndPreservesBgr24()
    {
        // Pbgra32 半透明緑（プリマルチプライ: B=0, G=128, R=0, A=128）-> 白背景合成で (127, 255, 127)
        var pbgraPixels = new byte[] { 0, 128, 0, 128 };
        var pbgra = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, pbgraPixels, 4);
        byte[] pbgraOut = GdiBitmapConverter.ConvertToBgr24TopDown(pbgra, out _, out _, out _);

        Assert.Equal(127, pbgraOut[0]);
        Assert.Equal(255, pbgraOut[1]);
        Assert.Equal(127, pbgraOut[2]);

        // Bgr24 パススルー
        var bgr24Pixels = new byte[] { 10, 20, 30 };
        var bgr24 = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, bgr24Pixels, 4);
        byte[] bgr24Out = GdiBitmapConverter.ConvertToBgr24TopDown(bgr24, out _, out _, out int stride24);

        Assert.Equal(4, stride24);
        Assert.Equal(10, bgr24Out[0]);
        Assert.Equal(20, bgr24Out[1]);
        Assert.Equal(30, bgr24Out[2]);
    }
}
