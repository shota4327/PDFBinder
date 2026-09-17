using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 印刷面付けおよびページ範囲計算サービスの単体テスト
/// </summary>
public class PrintLayoutCalculatorTests
{
    [Theory]
    [InlineData("1", 5, new[] { 0 })]
    [InlineData("1-3", 5, new[] { 0, 1, 2 })]
    [InlineData("1, 3, 5", 5, new[] { 0, 2, 4 })]
    [InlineData("1-3, 5", 5, new[] { 0, 1, 2, 4 })]
    [InlineData("4-2", 5, new[] { 3, 2, 1 })]
    public void TryParsePageRange_ValidInputs_ReturnsExpectedIndices(string input, int total, int[] expected)
    {
        bool result = PrintLayoutCalculator.TryParsePageRange(input, total, out var indices, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal(expected, indices);
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("   ", 5)]
    [InlineData("abc", 5)]
    [InlineData("0", 5)]
    [InlineData("6", 5)]
    [InlineData("1-6", 5)]
    [InlineData("1-", 5)]
    [InlineData("-3", 5)]
    public void TryParsePageRange_InvalidInputs_ReturnsFalseWithError(string input, int total)
    {
        bool result = PrintLayoutCalculator.TryParsePageRange(input, total, out var indices, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Empty(indices);
    }

    [Fact]
    public void CalculateSheets_FitToPage_GeneratesOneSheetPerPage()
    {
        var settings = new PrintSettings
        {
            LayoutMode = PrintLayoutMode.FitToPage,
            RangeType = PrintRangeType.AllPages
        };

        var sheets = PrintLayoutCalculator.CalculateSheets(settings, 3, 0);

        Assert.Equal(3, sheets.Count);
        Assert.Single(sheets[0].Placements);
        Assert.Equal(0, sheets[0].Placements[0].PageIndex);
        Assert.Equal(1, sheets[1].Placements[0].PageIndex);
        Assert.Equal(2, sheets[2].Placements[0].PageIndex);
    }

    [Fact]
    public void CalculateSheets_NUpTwoPages_GeneratesCorrectSheets()
    {
        var settings = new PrintSettings
        {
            LayoutMode = PrintLayoutMode.NUp,
            NUpCount = NUpPagesPerSheet.Two,
            RangeType = PrintRangeType.AllPages,
            Orientation = PrintOrientation.Landscape
        };

        var sheets = PrintLayoutCalculator.CalculateSheets(settings, 3, 0);

        // 3ページを2集約すると2シート
        Assert.Equal(2, sheets.Count);
        Assert.Equal(2, sheets[0].Placements.Count);
        Assert.Equal(0, sheets[0].Placements[0].PageIndex);
        Assert.Equal(1, sheets[0].Placements[1].PageIndex);

        // 2枚目は1ページ配置、残りは白紙（null）
        Assert.Equal(2, sheets[1].Placements[0].PageIndex);
        Assert.Null(sheets[1].Placements[1].PageIndex);
    }

    [Fact]
    public void CalculateSheets_Booklet_GeneratesCorrectImposition()
    {
        var settings = new PrintSettings
        {
            LayoutMode = PrintLayoutMode.Booklet,
            RangeType = PrintRangeType.AllPages
        };

        // 4ページ中綴じの場合
        var sheets4 = PrintLayoutCalculator.CalculateSheets(settings, 4, 0);
        Assert.Equal(2, sheets4.Count);
        // シート1 (表面): 左=P4(3), 右=P1(0)
        Assert.Equal(3, sheets4[0].Placements[0].PageIndex);
        Assert.Equal(0, sheets4[0].Placements[1].PageIndex);
        // シート2 (裏面): 左=P2(1), 右=P3(2)
        Assert.Equal(1, sheets4[1].Placements[0].PageIndex);
        Assert.Equal(2, sheets4[1].Placements[1].PageIndex);

        // 3ページの場合は4ページに切り上げられて末尾がnull（白紙）
        var sheets3 = PrintLayoutCalculator.CalculateSheets(settings, 3, 0);
        Assert.Equal(2, sheets3.Count);
        // P4は白紙(null)
        Assert.Null(sheets3[0].Placements[0].PageIndex);
        Assert.Equal(0, sheets3[0].Placements[1].PageIndex);
        Assert.Equal(1, sheets3[1].Placements[0].PageIndex);
        Assert.Equal(2, sheets3[1].Placements[1].PageIndex);
    }
}
