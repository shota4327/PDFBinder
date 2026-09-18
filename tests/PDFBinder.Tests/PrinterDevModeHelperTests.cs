using PDFBinder.App.Services;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// PrinterDevModeHelper の単体テスト
/// </summary>
public class PrinterDevModeHelperTests
{
    private const uint DM_ORIENTATION = 0x00000001;
    private const uint DM_PAPERSIZE = 0x00000002;
    private const uint DM_COPIES = 0x00000100;
    private const uint DM_DUPLEX = 0x00001000;

    private const short DMORIENT_PORTRAIT = 1;
    private const short DMORIENT_LANDSCAPE = 2;
    private const short DMPAPER_A3 = 8;
    private const short DMPAPER_A4 = 9;
    private const short DMDUP_SIMPLEX = 1;
    private const short DMDUP_VERTICAL = 2; // TwoSidedLongEdge
    private const short DMDUP_HORIZONTAL = 3; // TwoSidedShortEdge

    [Fact]
    public void ParseDevMode_ShortBuffer_ReturnsNullFields()
    {
        var buffer = new byte[50];
        var result = PrinterDevModeHelper.ParseDevMode(buffer);

        Assert.NotNull(result);
        Assert.Same(buffer, result.DevModeData);
        Assert.Null(result.PaperSize);
        Assert.Null(result.Orientation);
        Assert.Null(result.Copies);
        Assert.Null(result.DuplexMode);
    }

    [Fact]
    public void ParseDevMode_LandscapeA3Copies5TwoSidedLongEdge_ParsesCorrectly()
    {
        var buffer = new byte[220];
        uint fields = DM_ORIENTATION | DM_PAPERSIZE | DM_COPIES | DM_DUPLEX;
        BitConverter.GetBytes(fields).CopyTo(buffer, 72);
        BitConverter.GetBytes(DMORIENT_LANDSCAPE).CopyTo(buffer, 76);
        BitConverter.GetBytes(DMPAPER_A3).CopyTo(buffer, 78);
        BitConverter.GetBytes((short)5).CopyTo(buffer, 86);
        BitConverter.GetBytes(DMDUP_VERTICAL).CopyTo(buffer, 94);

        var result = PrinterDevModeHelper.ParseDevMode(buffer);

        Assert.Equal(PrintOrientation.Landscape, result.Orientation);
        Assert.Equal(PrintPaperSize.A3, result.PaperSize);
        Assert.Equal(5, result.Copies);
        Assert.Equal(PrintDuplexMode.TwoSidedLongEdge, result.DuplexMode);
    }

    [Fact]
    public void ParseDevMode_PortraitA4Copies1TwoSidedShortEdge_ParsesCorrectly()
    {
        var buffer = new byte[220];
        uint fields = DM_ORIENTATION | DM_PAPERSIZE | DM_COPIES | DM_DUPLEX;
        BitConverter.GetBytes(fields).CopyTo(buffer, 72);
        BitConverter.GetBytes(DMORIENT_PORTRAIT).CopyTo(buffer, 76);
        BitConverter.GetBytes(DMPAPER_A4).CopyTo(buffer, 78);
        BitConverter.GetBytes((short)1).CopyTo(buffer, 86);
        BitConverter.GetBytes(DMDUP_HORIZONTAL).CopyTo(buffer, 94);

        var result = PrinterDevModeHelper.ParseDevMode(buffer);

        Assert.Equal(PrintOrientation.Portrait, result.Orientation);
        Assert.Equal(PrintPaperSize.A4, result.PaperSize);
        Assert.Equal(1, result.Copies);
        Assert.Equal(PrintDuplexMode.TwoSidedShortEdge, result.DuplexMode);
    }

    [Fact]
    public void ParseDevMode_Simplex_ParsesCorrectly()
    {
        var buffer = new byte[220];
        uint fields = DM_DUPLEX;
        BitConverter.GetBytes(fields).CopyTo(buffer, 72);
        BitConverter.GetBytes(DMDUP_SIMPLEX).CopyTo(buffer, 94);

        var result = PrinterDevModeHelper.ParseDevMode(buffer);

        Assert.Equal(PrintDuplexMode.OneSided, result.DuplexMode);
        Assert.Null(result.Orientation);
        Assert.Null(result.PaperSize);
        Assert.Null(result.Copies);
    }
}
