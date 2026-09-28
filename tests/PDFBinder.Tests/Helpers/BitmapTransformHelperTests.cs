using System.IO;
using System.Threading;
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

    [Fact]
    public void CreateDetachedBitmap_NullSource_ReturnsNull()
    {
        var result = BitmapTransformHelper.CreateDetachedBitmap(null);
        Assert.Null(result);
    }

    [Fact]
    public void CreateDetachedBitmap_FromWorkerThread_AllowsCrossThreadTransformedBitmapFreeze()
    {
        // ワーカースレッド上でデコーダーからBitmapFrameを生成
        BitmapSource? detached = null;
        var thread = new Thread(() =>
        {
            var rtb = new RenderTargetBitmap(150, 100, 96, 96, PixelFormats.Pbgra32);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            detached = BitmapTransformHelper.CreateDetachedBitmap(decoder.Frames[0]);
        });
        thread.Start();
        thread.Join();

        Assert.NotNull(detached);
        Assert.True(detached.IsFrozen);
        Assert.Equal(150, detached.PixelWidth);
        Assert.Equal(100, detached.PixelHeight);

        // メイン（別）スレッド上で TransformedBitmap.Freeze() を実行してもスレッド例外が発生しないこと
        var rotated = BitmapTransformHelper.CreateRotatedBitmap(detached, 90);
        Assert.NotNull(rotated);
        Assert.Equal(100, rotated.PixelWidth);
        Assert.Equal(150, rotated.PixelHeight);
        Assert.True(rotated.IsFrozen);
    }
}
