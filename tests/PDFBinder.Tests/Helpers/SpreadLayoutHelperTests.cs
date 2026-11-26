using PDFBinder.App.Helpers;
using PDFBinder.App.Models;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="SpreadLayoutHelper"/> の見開き行構築・インデックス変換・PDFフラグ解決の単体テスト
/// </summary>
public class SpreadLayoutHelperTests
{
    [Fact]
    public void BuildRows_ZeroOrNegativePageCount_ReturnsEmpty()
    {
        Assert.Empty(SpreadLayoutHelper.BuildRows(0, true, PdfReadingDirection.LeftToRight));
        Assert.Empty(SpreadLayoutHelper.BuildRows(-2, false, PdfReadingDirection.RightToLeft));
    }

    [Theory]
    [InlineData(true, PdfReadingDirection.LeftToRight, null, 0)]
    [InlineData(true, PdfReadingDirection.RightToLeft, 0, null)]
    [InlineData(false, PdfReadingDirection.LeftToRight, 0, null)]
    [InlineData(false, PdfReadingDirection.RightToLeft, null, 0)]
    public void BuildRows_SinglePage_PlacesCoverOrTailAccordingToDirection(
        bool showCoverSeparately,
        PdfReadingDirection direction,
        int? expectedLeft,
        int? expectedRight)
    {
        var rows = SpreadLayoutHelper.BuildRows(1, showCoverSeparately, direction);

        Assert.Single(rows);
        Assert.Equal(0, rows[0].RowIndex);
        Assert.Equal(0, rows[0].PrimaryPageIndex);
        Assert.Null(rows[0].SecondaryPageIndex);
        Assert.Equal(expectedLeft, rows[0].LeftPageIndex);
        Assert.Equal(expectedRight, rows[0].RightPageIndex);
    }

    [Fact]
    public void BuildRows_FivePages_CoverSeparately_LeftToRight_BuildsExpectedSpread()
    {
        var rows = SpreadLayoutHelper.BuildRows(5, showCoverPageSeparately: true, PdfReadingDirection.LeftToRight);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new SpreadRowDefinition(0, 0, null, null, 0), rows[0]);
        Assert.Equal(new SpreadRowDefinition(1, 1, 2, 1, 2), rows[1]);
        Assert.Equal(new SpreadRowDefinition(2, 3, 4, 3, 4), rows[2]);
    }

    [Fact]
    public void BuildRows_SixPages_CoverSeparately_LeftToRight_PlacesLastPageOnLeft()
    {
        var rows = SpreadLayoutHelper.BuildRows(6, showCoverPageSeparately: true, PdfReadingDirection.LeftToRight);

        Assert.Equal(4, rows.Count);
        Assert.Equal(new SpreadRowDefinition(0, 0, null, null, 0), rows[0]);
        Assert.Equal(new SpreadRowDefinition(1, 1, 2, 1, 2), rows[1]);
        Assert.Equal(new SpreadRowDefinition(2, 3, 4, 3, 4), rows[2]);
        Assert.Equal(new SpreadRowDefinition(3, 5, null, 5, null), rows[3]);
    }

    [Fact]
    public void BuildRows_SixPages_CoverSeparately_RightToLeft_ReversesLeftRightAndAlignsEnds()
    {
        var rows = SpreadLayoutHelper.BuildRows(6, showCoverPageSeparately: true, PdfReadingDirection.RightToLeft);

        Assert.Equal(4, rows.Count);
        Assert.Equal(new SpreadRowDefinition(0, 0, null, 0, null), rows[0]);
        Assert.Equal(new SpreadRowDefinition(1, 1, 2, 2, 1), rows[1]);
        Assert.Equal(new SpreadRowDefinition(2, 3, 4, 4, 3), rows[2]);
        Assert.Equal(new SpreadRowDefinition(3, 5, null, null, 5), rows[3]);
    }

    [Fact]
    public void BuildRows_FivePages_NoCoverSeparately_LeftToRight_StartsWithPairAndEndsLeft()
    {
        var rows = SpreadLayoutHelper.BuildRows(5, showCoverPageSeparately: false, PdfReadingDirection.LeftToRight);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new SpreadRowDefinition(0, 0, 1, 0, 1), rows[0]);
        Assert.Equal(new SpreadRowDefinition(1, 2, 3, 2, 3), rows[1]);
        Assert.Equal(new SpreadRowDefinition(2, 4, null, 4, null), rows[2]);
    }

    [Fact]
    public void BuildRows_FivePages_NoCoverSeparately_RightToLeft_ReversesPairsAndPlacesTailOnRight()
    {
        var rows = SpreadLayoutHelper.BuildRows(5, showCoverPageSeparately: false, PdfReadingDirection.RightToLeft);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new SpreadRowDefinition(0, 0, 1, 1, 0), rows[0]);
        Assert.Equal(new SpreadRowDefinition(1, 2, 3, 3, 2), rows[1]);
        Assert.Equal(new SpreadRowDefinition(2, 4, null, null, 4), rows[2]);
    }

    [Theory]
    [InlineData(0, 6, true, 0)]
    [InlineData(1, 6, true, 1)]
    [InlineData(2, 6, true, 1)]
    [InlineData(5, 6, true, 3)]
    [InlineData(0, 6, false, 0)]
    [InlineData(1, 6, false, 0)]
    [InlineData(2, 6, false, 1)]
    [InlineData(-1, 6, true, -1)]
    [InlineData(6, 6, true, -1)]
    public void GetRowIndexForPage_ReturnsExpectedRowIndex(
        int pageIndex,
        int pageCount,
        bool showCoverSeparately,
        int expectedRow)
    {
        Assert.Equal(expectedRow, SpreadLayoutHelper.GetRowIndexForPage(pageIndex, pageCount, showCoverSeparately));
    }

    [Theory]
    [InlineData(0, 6, true, -1, 1)]
    [InlineData(1, 6, true, 0, 3)]
    [InlineData(2, 6, true, 0, 3)]
    [InlineData(5, 6, true, 3, -1)]
    [InlineData(0, 5, false, -1, 2)]
    [InlineData(1, 5, false, -1, 2)]
    [InlineData(2, 5, false, 0, 4)]
    [InlineData(4, 5, false, 2, -1)]
    public void GetPreviousAndNextSpreadPageIndex_ReturnsPrimaryPageOfAdjacentSpread(
        int currentIndex,
        int pageCount,
        bool showCoverSeparately,
        int expectedPrev,
        int expectedNext)
    {
        Assert.Equal(expectedPrev, SpreadLayoutHelper.GetPreviousSpreadPageIndex(currentIndex, pageCount, showCoverSeparately));
        Assert.Equal(expectedNext, SpreadLayoutHelper.GetNextSpreadPageIndex(currentIndex, pageCount, showCoverSeparately));
    }

    [Theory]
    [InlineData(PdfPageLayout.Default, PdfReadingDirection.LeftToRight, DetailPageViewMode.SinglePage, true, PdfReadingDirection.LeftToRight)]
    [InlineData(PdfPageLayout.SinglePage, PdfReadingDirection.RightToLeft, DetailPageViewMode.SinglePage, true, PdfReadingDirection.RightToLeft)]
    [InlineData(PdfPageLayout.OneColumn, PdfReadingDirection.LeftToRight, DetailPageViewMode.Continuous, true, PdfReadingDirection.LeftToRight)]
    [InlineData(PdfPageLayout.TwoPageLeft, PdfReadingDirection.LeftToRight, DetailPageViewMode.TwoPage, false, PdfReadingDirection.LeftToRight)]
    [InlineData(PdfPageLayout.TwoPageRight, PdfReadingDirection.LeftToRight, DetailPageViewMode.TwoPage, true, PdfReadingDirection.LeftToRight)]
    [InlineData(PdfPageLayout.TwoColumnLeft, PdfReadingDirection.RightToLeft, DetailPageViewMode.TwoPageContinuous, false, PdfReadingDirection.RightToLeft)]
    [InlineData(PdfPageLayout.TwoColumnRight, PdfReadingDirection.RightToLeft, DetailPageViewMode.TwoPageContinuous, true, PdfReadingDirection.RightToLeft)]
    public void ResolveInitialDisplaySettings_MapsAllPdfPageLayouts(
        PdfPageLayout layout,
        PdfReadingDirection direction,
        DetailPageViewMode expectedMode,
        bool expectedCoverSeparately,
        PdfReadingDirection expectedDirection)
    {
        var (mode, showCover, resolvedDir) = SpreadLayoutHelper.ResolveInitialDisplaySettings(layout, direction);

        Assert.Equal(expectedMode, mode);
        Assert.Equal(expectedCoverSeparately, showCover);
        Assert.Equal(expectedDirection, resolvedDir);
    }
}
