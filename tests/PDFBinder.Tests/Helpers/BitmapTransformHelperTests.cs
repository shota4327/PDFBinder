using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Helpers;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="BitmapTransformHelper"/> の幾何学的回転変換に関する単体テスト
/// </summary>
public class BitmapTransformHelperTests
{
    private static BitmapSource CreateTestBitmap(int width, int height)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32,
            null,
            new byte[width * height * 4],
            width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void CreateRotatedBitmap_NullSource_ReturnsNull()
    {
        var result = BitmapTransformHelper.CreateRotatedBitmap(null, 90);
        Assert.Null(result);
    }

    [Fact]
    public void CreateRotatedBitmap_ZeroDegrees_ReturnsOriginalSource()
    {
        var source = CreateTestBitmap(100, 200);
        var result = BitmapTransformHelper.CreateRotatedBitmap(source, 0);
        Assert.Same(source, result);
    }

    [Fact]
    public void CreateRotatedBitmap_360Degrees_ReturnsOriginalSource()
    {
        var source = CreateTestBitmap(100, 200);
        var result = BitmapTransformHelper.CreateRotatedBitmap(source, 360);
        Assert.Same(source, result);
    }

    [Fact]
    public void CreateRotatedBitmap_90Degrees_SwapsDimensions()
    {
        // Arrange: 幅100, 高さ200
        var source = CreateTestBitmap(100, 200);

        // Act: 90度回転
        var result = BitmapTransformHelper.CreateRotatedBitmap(source, 90);

        // Assert: 幅200, 高さ100に変換されていること
        Assert.NotNull(result);
        Assert.Equal(200, result.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
        Assert.True(result.IsFrozen);
    }

    [Fact]
    public void CreateRotatedBitmap_180Degrees_MaintainsDimensions()
    {
        // Arrange: 幅100, 高さ200
        var source = CreateTestBitmap(100, 200);

        // Act: 180度回転
        var result = BitmapTransformHelper.CreateRotatedBitmap(source, 180);

        // Assert: 幅100, 高さ200のまま
        Assert.NotNull(result);
        Assert.Equal(100, result.PixelWidth);
        Assert.Equal(200, result.PixelHeight);
    }

    [Fact]
    public void CreateRotatedBitmap_Negative90Degrees_RotatesCorrectlyAs270Degrees()
    {
        // Arrange: 幅100, 高さ200
        var source = CreateTestBitmap(100, 200);

        // Act: -90度（270度）回転
        var result = BitmapTransformHelper.CreateRotatedBitmap(source, -90);

        // Assert: 幅200, 高さ100に変換されていること
        Assert.NotNull(result);
        Assert.Equal(200, result.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
    }
}
