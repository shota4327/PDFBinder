using System.Collections.Generic;
using PDFBinder.App.Services;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="DisplayProfileService"/> のプロファイルキー生成および設定解決ロジックに関する単体テスト
/// </summary>
public class DisplayProfileServiceTests
{
    [Fact]
    public void BuildProfileKey_WithSingleExternalMonitor_ReturnsExpectedKey()
    {
        // 準備
        var monitors = new List<DisplayMonitorInfo>
        {
            new("T27h-30", 2560, 1440, true)
        };

        // 実行
        string key = DisplayProfileService.BuildProfileKey(monitors);

        // 検証
        Assert.Equal("T27h-30_2560x1440_1mon", key);
    }

    [Fact]
    public void BuildProfileKey_WithInternalLaptopMonitor_ReturnsExpectedKey()
    {
        // 準備
        var monitors = new List<DisplayMonitorInfo>
        {
            new("BOE0892", 1920, 1080, true)
        };

        // 実行
        string key = DisplayProfileService.BuildProfileKey(monitors);

        // 検証
        Assert.Equal("BOE0892_1920x1080_1mon", key);
    }

    [Fact]
    public void BuildProfileKey_WithMultiMonitor_UsesPrimaryMonitorAndTotalCount()
    {
        // 準備: 外部モニター（プライマリ）とノートPC本体（セカンダリ）のデュアル構成
        var monitors = new List<DisplayMonitorInfo>
        {
            new("BOE0892", 1920, 1080, false),
            new("T27h-30", 2560, 1440, true)
        };

        // 実行
        string key = DisplayProfileService.BuildProfileKey(monitors);

        // 検証
        Assert.Equal("T27h-30_2560x1440_2mon", key);
    }

    [Fact]
    public void BuildProfileKey_WhenEmptyOrNull_ReturnsDefaultProfileKey()
    {
        // 実行 & 検証
        Assert.Equal("Default_1100x760_1mon", DisplayProfileService.BuildProfileKey(null));
        Assert.Equal("Default_1100x760_1mon", DisplayProfileService.BuildProfileKey(new List<DisplayMonitorInfo>()));
    }

    [Theory]
    [InlineData("T27h-30", "T27h-30")]
    [InlineData(@"\\.\DISPLAY1", "DISPLAY1")]
    [InlineData("Dell U2720Q (HDMI)", "Dell_U2720Q_HDMI")]
    [InlineData("???***///", "Display")]
    [InlineData("", "Display")]
    [InlineData(null, "Display")]
    public void SanitizeIdentifier_RemovesSpecialCharactersSafely(string? input, string expected)
    {
        // 実行
        string result = DisplayProfileService.SanitizeIdentifier(input);

        // 検証
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveEffectiveWindowSettings_WhenProfileMatches_ReturnsProfileSettings()
    {
        // 準備: 外部モニターのプロファイルキーを返すプロバイダーを設定
        var mockMonitors = new List<DisplayMonitorInfo>
        {
            new("T27h-30", 2560, 1440, true)
        };
        var service = new DisplayProfileService(() => mockMonitors);

        var settings = new AppSettings
        {
            Window = new WindowSettings { Width = 1100, Height = 760, IsMaximized = false },
            DisplayProfiles = new Dictionary<string, WindowSettings>
            {
                ["T27h-30_2560x1440_1mon"] = new WindowSettings { Width = 2000, Height = 1200, IsMaximized = true },
                ["BOE0892_1920x1080_1mon"] = new WindowSettings { Width = 1400, Height = 850, IsMaximized = false }
            }
        };

        // 実行
        var effective = service.ResolveEffectiveWindowSettings(settings);

        // 検証: T27h-30 の個別設定が解決されること
        Assert.NotNull(effective);
        Assert.Equal(2000, effective.Width);
        Assert.Equal(1200, effective.Height);
        Assert.True(effective.IsMaximized);
    }

    [Fact]
    public void ResolveEffectiveWindowSettings_WhenProfileNotFound_FallsBackToWindowSettings()
    {
        // 準備: 保存済みプロファイルに存在しないモニター環境
        var mockMonitors = new List<DisplayMonitorInfo>
        {
            new("UnknownMonitor", 1680, 1050, true)
        };
        var service = new DisplayProfileService(() => mockMonitors);

        var settings = new AppSettings
        {
            Window = new WindowSettings { Width = 1300, Height = 800, IsMaximized = false },
            DisplayProfiles = new Dictionary<string, WindowSettings>
            {
                ["T27h-30_2560x1440_1mon"] = new WindowSettings { Width = 2000, Height = 1200, IsMaximized = true }
            }
        };

        // 実行
        var effective = service.ResolveEffectiveWindowSettings(settings);

        // 検証: 共通の Window 設定にフォールバックすること
        Assert.NotNull(effective);
        Assert.Equal(1300, effective.Width);
        Assert.Equal(800, effective.Height);
        Assert.False(effective.IsMaximized);
    }

    [Fact]
    public void ResolveEffectiveWindowSettings_WhenSettingsIsNull_ReturnsDefaultWindowSettings()
    {
        // 準備
        var service = new DisplayProfileService(() => new List<DisplayMonitorInfo>());

        // 実行
        var effective = service.ResolveEffectiveWindowSettings(null);

        // 検証
        Assert.NotNull(effective);
        Assert.Equal(1100, effective.Width);
        Assert.Equal(760, effective.Height);
        Assert.False(effective.IsMaximized);
    }
}
