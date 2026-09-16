using PDFBinder.Core.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// WinTab筆圧正規化ヘルパー（WinTabPressureHelper）の単体テスト。
/// </summary>
public class WinTabPressureHelperTests
{
    [Fact]
    public void NormalizePressure_MiddleValue_ReturnsLinearRatio()
    {
        // 4096段階中の中間値2048の場合、0.5fを返すことを検証
        float result = WinTabPressureHelper.NormalizePressure(2048, 4096);
        Assert.Equal(0.5f, result, 0.001f);
    }

    [Fact]
    public void NormalizePressure_ZeroOrNegative_ClampsToMinimum()
    {
        // 筆圧が0または負値の場合は最小クランプ値（0.05f）を返すことを検証
        float resultZero = WinTabPressureHelper.NormalizePressure(0, 4096);
        Assert.Equal(WinTabPressureHelper.MinPressureFactor, resultZero);

        float resultNegative = WinTabPressureHelper.NormalizePressure(-100, 4096);
        Assert.Equal(WinTabPressureHelper.MinPressureFactor, resultNegative);
    }

    [Fact]
    public void NormalizePressure_ExceedsMax_ClampsToMaximum()
    {
        // 最大筆圧値を超過した場合は最大クランプ値（1.0f）を返すことを検証
        float resultMax = WinTabPressureHelper.NormalizePressure(4096, 4096);
        Assert.Equal(WinTabPressureHelper.MaxPressureFactor, resultMax);

        float resultOver = WinTabPressureHelper.NormalizePressure(5000, 4096);
        Assert.Equal(WinTabPressureHelper.MaxPressureFactor, resultOver);
    }

    [Fact]
    public void NormalizePressure_InvalidMaxPressure_ReturnsDefaultFactor()
    {
        // 最大筆圧値が0以下の異常値の場合、安全にデフォルト値（0.5f）を返すことを検証
        float resultZeroMax = WinTabPressureHelper.NormalizePressure(100, 0);
        Assert.Equal(WinTabPressureHelper.DefaultPressureFactor, resultZeroMax);

        float resultNegativeMax = WinTabPressureHelper.NormalizePressure(100, -100);
        Assert.Equal(WinTabPressureHelper.DefaultPressureFactor, resultNegativeMax);
    }

    [Theory]
    [InlineData(1024, 8192, 0.125f)]
    [InlineData(4096, 8192, 0.5f)]
    [InlineData(6144, 8192, 0.75f)]
    public void NormalizePressure_HighResolutionPressure_ReturnsCorrectRatio(int raw, int max, float expected)
    {
        // 8192段階（最新Wacomプロモデル等）での高解像度筆圧比率を検証
        float result = WinTabPressureHelper.NormalizePressure(raw, max);
        Assert.Equal(expected, result, 0.001f);
    }
}
