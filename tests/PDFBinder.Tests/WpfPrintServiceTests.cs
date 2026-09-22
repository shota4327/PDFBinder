using PDFBinder.App.Services;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// WpfPrintService の用紙寸法計算および印刷関連ロジックの単体テスト
/// </summary>
public class WpfPrintServiceTests
{
    [Fact]
    public void GetPaperDimensionsInDips_A4Portrait_ReturnsAccurate96DpiDimensions()
    {
        // 実行: A4 縦向きの寸法取得
        var (width, height) = WpfPrintService.GetPaperDimensionsInDips(PrintPaperSize.A4, PrintOrientation.Portrait);

        // 検証: 210mm x 297mm を 96 DPI 換算した値 (約 793.70 x 1122.52 px)
        Assert.InRange(width, 793.6, 793.8);
        Assert.InRange(height, 1122.4, 1122.6);
    }

    [Fact]
    public void GetPaperDimensionsInDips_A4Landscape_ReturnsSwappedDimensions()
    {
        // 実行: A4 横向きの寸法取得
        var (width, height) = WpfPrintService.GetPaperDimensionsInDips(PrintPaperSize.A4, PrintOrientation.Landscape);

        // 検証: 横向きのため長辺が幅、短辺が高さ
        Assert.InRange(width, 1122.4, 1122.6);
        Assert.InRange(height, 793.6, 793.8);
    }

    [Fact]
    public void GetPaperDimensionsInDips_A3Portrait_ReturnsAccurate96DpiDimensions()
    {
        // 実行: A3 縦向きの寸法取得
        var (width, height) = WpfPrintService.GetPaperDimensionsInDips(PrintPaperSize.A3, PrintOrientation.Portrait);

        // 検証: 297mm x 420mm を 96 DPI 換算した値 (約 1122.52 x 1587.40 px)
        Assert.InRange(width, 1122.4, 1122.6);
        Assert.InRange(height, 1587.3, 1587.5);
    }

    [Fact]
    public void GetPaperDimensionsInDips_A3Landscape_ReturnsAccurate96DpiDimensions()
    {
        // 実行: A3 横向きの寸法取得
        var (width, height) = WpfPrintService.GetPaperDimensionsInDips(PrintPaperSize.A3, PrintOrientation.Landscape);

        // 検証: 横向きのため幅が 420mm、高さが 297mm
        Assert.InRange(width, 1587.3, 1587.5);
        Assert.InRange(height, 1122.4, 1122.6);
    }

    [Fact]
    public void HalfOfA3Landscape_ExactlyMatchesA4Portrait_EnsuringBooklet100PercentScale()
    {
        // A3横向きの寸法と A4縦向きの寸法を取得
        var (a3Width, a3Height) = WpfPrintService.GetPaperDimensionsInDips(PrintPaperSize.A3, PrintOrientation.Landscape);
        var (a4Width, a4Height) = WpfPrintService.GetPaperDimensionsInDips(PrintPaperSize.A4, PrintOrientation.Portrait);

        // 冊子印刷における左右1スロットの寸法（A3横の幅の半分、高さはそのまま）
        double slotWidth = a3Width * 0.5;
        double slotHeight = a3Height;

        // A4原寸（100%等倍）と完全に一致することを検証
        Assert.Equal(a4Width, slotWidth, precision: 2);
        Assert.Equal(a4Height, slotHeight, precision: 2);
    }
}
