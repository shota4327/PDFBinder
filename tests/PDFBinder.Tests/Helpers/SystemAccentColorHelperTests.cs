using System.Windows;
using System.Windows.Media;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="SystemAccentColorHelper"/> のカラー値変換およびリソース反映ロジックの単体テスト
/// </summary>
public class SystemAccentColorHelperTests
{
    [Theory]
    [InlineData(0xC40078D4, 0, 120, 212)]
    [InlineData(0xFF112233, 0x11, 0x22, 0x33)]
    [InlineData(0x80FF0080, 255, 0, 128)]
    [InlineData(0x00FFFFFF, 255, 255, 255)]
    public void ParseColorizationColor_ConvertsArgbToColorCorrectly(uint colorization, byte expectedR, byte expectedG, byte expectedB)
    {
        // Act
        var color = SystemAccentColorHelper.ParseColorizationColor(colorization);

        // Assert
        Assert.Equal(255, color.A);
        Assert.Equal(expectedR, color.R);
        Assert.Equal(expectedG, color.G);
        Assert.Equal(expectedB, color.B);
    }

    [Theory]
    [InlineData(0xFFD47800, 0, 120, 212)]
    [InlineData(0xFF332211, 0x11, 0x22, 0x33)]
    [InlineData(0x808000FF, 255, 0, 128)]
    [InlineData(0x00FFFFFF, 255, 255, 255)]
    public void ParseAbgrColor_ConvertsAbgrToColorCorrectly(uint abgr, byte expectedR, byte expectedG, byte expectedB)
    {
        // Act
        var color = SystemAccentColorHelper.ParseAbgrColor(abgr);

        // Assert
        Assert.Equal(255, color.A);
        Assert.Equal(expectedR, color.R);
        Assert.Equal(expectedG, color.G);
        Assert.Equal(expectedB, color.B);
    }

    [Fact]
    public void GetAccentColor_ReturnsValidNonBlackColor()
    {
        // Act
        var color = SystemAccentColorHelper.GetAccentColor();

        // Assert
        Assert.Equal(255, color.A);
        // 純粋な黒色（0, 0, 0）にならず、何らかの色またはフォールバック色が返ることを検証
        bool isNonBlack = color.R != 0 || color.G != 0 || color.B != 0;
        Assert.True(isNonBlack);
    }

    [Fact]
    public void UpdateBrush_PopulatesResourceDictionary()
    {
        // Arrange
        var resources = new ResourceDictionary();

        // Act
        SystemAccentColorHelper.UpdateBrush(resources);

        // Assert
        Assert.True(resources.Contains(SystemAccentColorHelper.ResourceKey));
        var brush = resources[SystemAccentColorHelper.ResourceKey] as SolidColorBrush;
        Assert.NotNull(brush);
        Assert.Equal(255, brush.Color.A);
    }

    [Fact]
    public void UpdateBrush_UpdatesExistingUnfrozenBrush()
    {
        // Arrange
        var resources = new ResourceDictionary();
        var initialBrush = new SolidColorBrush(Colors.Red);
        resources[SystemAccentColorHelper.ResourceKey] = initialBrush;

        // Act
        SystemAccentColorHelper.UpdateBrush(resources);

        // Assert
        var updatedBrush = resources[SystemAccentColorHelper.ResourceKey] as SolidColorBrush;
        Assert.NotNull(updatedBrush);
        Assert.Same(initialBrush, updatedBrush);
        Assert.NotEqual(Colors.Red, updatedBrush.Color);
    }

    [Fact]
    public void UpdateBrush_HandlesNullGracefullyWithoutThrowing()
    {
        // Act & Assert（例外が発生しないことを確認）
        var exception = Record.Exception(() => SystemAccentColorHelper.UpdateBrush(null));
        Assert.Null(exception);
    }
}
