using System.Globalization;
using System.Windows.Data;
using PDFBinder.App.Controls;
using PDFBinder.App.Converters;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="ToolToBooleanConverter"/> の単体テスト
/// </summary>
public class ToolToBooleanConverterTests
{
    private readonly ToolToBooleanConverter _converter = new();

    [Theory]
    [InlineData(EditorToolMode.Pen, "Pen", true)]
    [InlineData(EditorToolMode.Pen, "pen", true)]
    [InlineData(EditorToolMode.EraserStroke, "EraserStroke", true)]
    [InlineData(EditorToolMode.Highlighter, "Pen", false)]
    [InlineData(EditorToolMode.StraightLine, "Hand", false)]
    public void Convert_MatchesParameterCaseInsensitive(EditorToolMode tool, string parameter, bool expected)
    {
        // Act
        var result = _converter.Convert(tool, typeof(bool), parameter, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NullValueOrParameter_ReturnsFalse()
    {
        Assert.Equal(false, _converter.Convert(null!, typeof(bool), "Pen", CultureInfo.InvariantCulture));
        Assert.Equal(false, _converter.Convert(EditorToolMode.Pen, typeof(bool), null!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Pen", EditorToolMode.Pen)]
    [InlineData("pen", EditorToolMode.Pen)]
    [InlineData("Highlighter", EditorToolMode.Highlighter)]
    [InlineData("EraserStroke", EditorToolMode.EraserStroke)]
    [InlineData("EraserPoint", EditorToolMode.EraserPoint)]
    [InlineData("StraightLine", EditorToolMode.StraightLine)]
    [InlineData("Hand", EditorToolMode.Hand)]
    public void ConvertBack_WhenTrue_ReturnsCorrespondingToolMode(string parameter, EditorToolMode expected)
    {
        // Act
        var result = _converter.ConvertBack(true, typeof(EditorToolMode), parameter, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void ConvertBack_WhenNotTrue_ReturnsDoNothing(object? value)
    {
        // Act
        var result = _converter.ConvertBack(value!, typeof(EditorToolMode), "Pen", CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Binding.DoNothing, result);
    }

    [Theory]
    [InlineData("InvalidMode")]
    [InlineData("")]
    [InlineData(null)]
    public void ConvertBack_WhenParameterInvalidOrNull_ReturnsDoNothing(string? parameter)
    {
        // Act
        var result = _converter.ConvertBack(true, typeof(EditorToolMode), parameter!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Binding.DoNothing, result);
    }
}
