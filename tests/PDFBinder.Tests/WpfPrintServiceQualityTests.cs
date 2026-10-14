using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.Services;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// WpfPrintService の印刷品質向上・動的解像度計算・PNG圧縮機能の単体テスト
/// </summary>
public class WpfPrintServiceQualityTests
{
    /// <summary>
    /// A4およびA3の用紙ピクセル寸法（300 DPI基準）が正しく計算されることを検証します。
    /// </summary>
    [Fact]
    public void GetPaperPixelDimensions_ReturnsCorrectDimensionsForA4AndA3()
    {
        // A4 縦 (210mm x 297mm @ 300 DPI)
        var (a4PortW, a4PortH) = WpfPrintService.GetPaperPixelDimensions(PrintPaperSize.A4, PrintOrientation.Portrait);
        Assert.Equal(2480, a4PortW);
        Assert.Equal(3508, a4PortH);

        // A4 横
        var (a4LandW, a4LandH) = WpfPrintService.GetPaperPixelDimensions(PrintPaperSize.A4, PrintOrientation.Landscape);
        Assert.Equal(3508, a4LandW);
        Assert.Equal(2480, a4LandH);

        // A3 縦 (297mm x 420mm @ 300 DPI)
        var (a3PortW, a3PortH) = WpfPrintService.GetPaperPixelDimensions(PrintPaperSize.A3, PrintOrientation.Portrait);
        Assert.Equal(3508, a3PortW);
        Assert.Equal(4961, a3PortH);

        // A3 横
        var (a3LandW, a3LandH) = WpfPrintService.GetPaperPixelDimensions(PrintPaperSize.A3, PrintOrientation.Landscape);
        Assert.Equal(4961, a3LandW);
        Assert.Equal(3508, a3LandH);
    }

    /// <summary>
    /// スロットの正規化領域に応じたピクセル寸法が正しく計算されることを検証します。
    /// </summary>
    [Fact]
    public void CalculateSlotPixelDimensions_ReturnsScaledSlotSize()
    {
        // 2-Up 横向き用紙の左スロット (幅0.5, 高さ1.0)
        var bounds = new Rect(0, 0, 0.5, 1.0);
        var (slotW, slotH) = WpfPrintService.CalculateSlotPixelDimensions(PrintPaperSize.A4, PrintOrientation.Landscape, bounds);

        Assert.Equal(1754, slotW);
        Assert.Equal(2480, slotH);
    }

    /// <summary>
    /// 元ページの縦横比を維持してスロットに内接する解像度が正しく算出されることを検証します。
    /// </summary>
    [Fact]
    public void CalculatePageRenderDimensions_AspectPreservedWithinSlot()
    {
        // 縦長スロット (1000 x 2000) に横長ページ (800 x 400 = 2:1) を配置
        var (w1, h1) = WpfPrintService.CalculatePageRenderDimensions(1000, 2000, 800, 400);
        Assert.Equal(1000, w1);
        Assert.Equal(500, h1);

        // 横長スロット (2000 x 1000) に縦長ページ (400 x 800 = 1:2) を配置
        var (w2, h2) = WpfPrintService.CalculatePageRenderDimensions(2000, 1000, 400, 800);
        Assert.Equal(500, w2);
        Assert.Equal(1000, h2);

        // 異常値
        var (w0, h0) = WpfPrintService.CalculatePageRenderDimensions(1000, 1000, 0, 0);
        Assert.Equal(1000, w0);
        Assert.Equal(1000, h0);
    }

    /// <summary>
    /// 生ビットマップがインメモリでPNG圧縮されたBitmapImageへ正常に変換されることを検証します。
    /// </summary>
    [Fact]
    public void ConvertToCompressedPngBitmap_ConvertsRawBitmapToValidPngSource()
    {
        int width = 100;
        int height = 100;
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 0;
            pixels[i + 2] = 0;
            pixels[i + 3] = 255;
        }
        var rawBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);

        var compressed = WpfPrintService.ConvertToCompressedPngBitmap(rawBitmap);

        Assert.NotNull(compressed);
        Assert.Equal(100, compressed.PixelWidth);
        Assert.Equal(100, compressed.PixelHeight);
        Assert.True(compressed.IsFrozen);
    }

    /// <summary>
    /// PrepareSheetsAsync がスロットと元寸法に応じた最適解像度を計算してレンダリング関数に渡し、PNG圧縮済み画像を生成することを検証します。
    /// </summary>
    [Fact]
    public async Task PrepareSheetsAsync_WithDimensions_CalculatesCorrectResolutionAndCompresses()
    {
        var service = new WpfPrintService();

        var sheet = new PrintSheetLayout(1, PrintOrientation.Portrait, PrintPaperSize.A4);
        sheet.Placements.Add(new PrintPagePlacement(0, new Rect(0, 0, 1.0, 1.0)));

        var pageDimensions = new List<(double width, double height)>
        {
            (595, 842)
        };

        var requestedResolutions = new List<(int w, int h)>();
        BitmapSource CreateTestBitmap(int w, int h)
        {
            var pixels = new byte[w * h * 4];
            return BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        }

        var prepared = await service.PrepareSheetsAsync(
            (idx, w, h, ct) =>
            {
                requestedResolutions.Add((w, h));
                return Task.FromResult<BitmapSource?>(CreateTestBitmap(w, h));
            },
            new[] { sheet },
            pageDimensions);

        Assert.Single(prepared);
        Assert.Single(prepared[0].Placements);
        Assert.NotNull(prepared[0].Placements[0].Image);
        Assert.Single(requestedResolutions);

        var (reqW, reqH) = requestedResolutions[0];
        Assert.InRange(reqW, 2470, 2485);
        Assert.InRange(reqH, 3500, 3515);
    }
}
