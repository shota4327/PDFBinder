using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="WindowBoundsHelper"/> のウィンドウサイズ境界調整ロジックに関する単体テスト
/// </summary>
public class WindowBoundsHelperTests
{
    [Fact]
    public void AdjustBounds_WithinWorkArea_PreservesSavedDimensions()
    {
        // 準備: 作業領域 1920x1040 内におさまる保存サイズ 1200x800
        double savedWidth = 1200;
        double savedHeight = 800;
        double workAreaWidth = 1920;
        double workAreaHeight = 1040;

        // 実行
        var (width, height) = WindowBoundsHelper.AdjustBounds(savedWidth, savedHeight, workAreaWidth, workAreaHeight);

        // 検証
        Assert.Equal(1200, width);
        Assert.Equal(800, height);
    }

    [Fact]
    public void AdjustBounds_ExceedsWorkArea_ClampsToWorkAreaDimensions()
    {
        // 準備: 作業領域 1920x1040 を超える巨大サイズ 2560x1440
        double savedWidth = 2560;
        double savedHeight = 1440;
        double workAreaWidth = 1920;
        double workAreaHeight = 1040;

        // 実行
        var (width, height) = WindowBoundsHelper.AdjustBounds(savedWidth, savedHeight, workAreaWidth, workAreaHeight);

        // 検証: 作業領域内におさまるようクランプされること
        Assert.Equal(1920, width);
        Assert.Equal(1040, height);
    }

    [Fact]
    public void AdjustBounds_BelowMinimumSize_ExpandsToMinimumSize()
    {
        // 準備: 最小サイズ（750x500）を下回るサイズ 400x300
        double savedWidth = 400;
        double savedHeight = 300;
        double workAreaWidth = 1920;
        double workAreaHeight = 1040;

        // 実行
        var (width, height) = WindowBoundsHelper.AdjustBounds(savedWidth, savedHeight, workAreaWidth, workAreaHeight);

        // 検証: 最小サイズ（750x500）に補正されること
        Assert.Equal(750, width);
        Assert.Equal(500, height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-100, -100)]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity)]
    public void AdjustBounds_InvalidDimensions_FallsBackToDefaultSize(double invalidWidth, double invalidHeight)
    {
        // 準備: 無効な数値
        double workAreaWidth = 1920;
        double workAreaHeight = 1040;

        // 実行
        var (width, height) = WindowBoundsHelper.AdjustBounds(invalidWidth, invalidHeight, workAreaWidth, workAreaHeight);

        // 検証: デフォルトサイズ（1100x760）にフォールバックすること
        Assert.Equal(WindowBoundsHelper.DefaultWidth, width);
        Assert.Equal(WindowBoundsHelper.DefaultHeight, height);
    }

    [Fact]
    public void AdjustBounds_WorkAreaSmallerThanMinimumSize_FitsWithinWorkArea()
    {
        // 準備: 作業領域自体が最小サイズより小さい極小解像度環境（600x400）
        double savedWidth = 1100;
        double savedHeight = 760;
        double workAreaWidth = 600;
        double workAreaHeight = 400;

        // 実行
        var (width, height) = WindowBoundsHelper.AdjustBounds(savedWidth, savedHeight, workAreaWidth, workAreaHeight);

        // 検証: 作業領域（画面）をはみ出さないよう画面解像度内に収まること
        Assert.Equal(600, width);
        Assert.Equal(400, height);
    }
}
