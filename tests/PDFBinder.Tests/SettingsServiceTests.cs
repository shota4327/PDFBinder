using System;
using System.IO;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="SettingsService"/> の設定読み込み・保存処理に関する単体テスト
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _settingsFilePath;

    public SettingsServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PDFBinder_SettingsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _settingsFilePath = Path.Combine(_tempDirectory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // テスト一時ディレクトリのクリーンアップ失敗は無視
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaultSettings()
    {
        // 準備
        var service = new SettingsService(_settingsFilePath);

        // 実行
        var settings = service.Load();

        // 検証
        Assert.NotNull(settings);
        Assert.NotNull(settings.Window);
        Assert.Equal(1100, settings.Window.Width);
        Assert.Equal(760, settings.Window.Height);
        Assert.False(settings.Window.IsMaximized);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesAllWindowSettings()
    {
        // 準備
        var service = new SettingsService(_settingsFilePath);
        var expectedSettings = new AppSettings
        {
            Window = new WindowSettings
            {
                Width = 1440,
                Height = 900,
                IsMaximized = true
            }
        };

        // 実行
        service.Save(expectedSettings);
        var loadedSettings = service.Load();

        // 検証
        Assert.NotNull(loadedSettings);
        Assert.NotNull(loadedSettings.Window);
        Assert.Equal(1440, loadedSettings.Window.Width);
        Assert.Equal(900, loadedSettings.Window.Height);
        Assert.True(loadedSettings.Window.IsMaximized);
    }

    [Fact]
    public void Load_WhenJsonIsCorrupted_ReturnsDefaultSettingsWithoutException()
    {
        // 準備: 不正なJSONテキストを書き込み
        File.WriteAllText(_settingsFilePath, "{ invalid json content !!!");
        var service = new SettingsService(_settingsFilePath);

        // 実行
        var settings = service.Load();

        // 検証: 例外が発生せずデフォルト値が返ること
        Assert.NotNull(settings);
        Assert.NotNull(settings.Window);
        Assert.Equal(1100, settings.Window.Width);
        Assert.Equal(760, settings.Window.Height);
    }

    [Fact]
    public void Load_WhenFileIsEmpty_ReturnsDefaultSettings()
    {
        // 準備: 空ファイルを作成
        File.WriteAllText(_settingsFilePath, "");
        var service = new SettingsService(_settingsFilePath);

        // 実行
        var settings = service.Load();

        // 検証
        Assert.NotNull(settings);
        Assert.NotNull(settings.Window);
        Assert.Equal(1100, settings.Window.Width);
    }

    [Fact]
    public void Save_WhenSettingsIsNull_ThrowsArgumentNullException()
    {
        // 準備
        var service = new SettingsService(_settingsFilePath);

        // 実行・検証
        Assert.Throws<ArgumentNullException>(() => service.Save(null!));
    }

    [Fact]
    public void Save_WhenDirectoryDoesNotExist_CreatesDirectoryAndSavesSuccessfully()
    {
        // 準備: 存在しないネストされたディレクトリを指定
        string nestedFilePath = Path.Combine(_tempDirectory, "sub1", "sub2", "settings.json");
        var service = new SettingsService(nestedFilePath);
        var settings = new AppSettings
        {
            Window = new WindowSettings { Width = 1280, Height = 720, IsMaximized = false }
        };

        // 実行
        service.Save(settings);

        // 検証
        Assert.True(File.Exists(nestedFilePath));
        var loaded = service.Load();
        Assert.Equal(1280, loaded.Window.Width);
    }

    [Fact]
    public void SaveAndLoad_WithDisplayProfiles_PreservesProfileSettings()
    {
        // 準備
        var service = new SettingsService(_settingsFilePath);
        var expectedSettings = new AppSettings
        {
            Window = new WindowSettings { Width = 1100, Height = 760, IsMaximized = false },
            DisplayProfiles = new System.Collections.Generic.Dictionary<string, WindowSettings>
            {
                ["T27h-30_2560x1440_1mon"] = new WindowSettings { Width = 1920, Height = 1080, IsMaximized = true },
                ["Internal_1920x1080_1mon"] = new WindowSettings { Width = 1280, Height = 720, IsMaximized = false }
            }
        };

        // 実行
        service.Save(expectedSettings);
        var loaded = service.Load();

        // 検証
        Assert.NotNull(loaded);
        Assert.NotNull(loaded.DisplayProfiles);
        Assert.Equal(2, loaded.DisplayProfiles.Count);

        Assert.True(loaded.DisplayProfiles.ContainsKey("T27h-30_2560x1440_1mon"));
        Assert.Equal(1920, loaded.DisplayProfiles["T27h-30_2560x1440_1mon"].Width);
        Assert.Equal(1080, loaded.DisplayProfiles["T27h-30_2560x1440_1mon"].Height);
        Assert.True(loaded.DisplayProfiles["T27h-30_2560x1440_1mon"].IsMaximized);

        Assert.True(loaded.DisplayProfiles.ContainsKey("Internal_1920x1080_1mon"));
        Assert.Equal(1280, loaded.DisplayProfiles["Internal_1920x1080_1mon"].Width);
        Assert.Equal(720, loaded.DisplayProfiles["Internal_1920x1080_1mon"].Height);
        Assert.False(loaded.DisplayProfiles["Internal_1920x1080_1mon"].IsMaximized);
    }

    [Fact]
    public void Load_WhenLegacyJsonWithoutDisplayProfiles_DeserializesWithEmptyDisplayProfiles()
    {
        // 準備: DisplayProfiles キーが存在しない古い形式の JSON を書き込み
        string legacyJson = "{\"Window\":{\"Width\":1366,\"Height\":768,\"IsMaximized\":false}}";
        File.WriteAllText(_settingsFilePath, legacyJson);
        var service = new SettingsService(_settingsFilePath);

        // 実行
        var loaded = service.Load();

        // 検証
        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Window);
        Assert.Equal(1366, loaded.Window.Width);
        Assert.Equal(768, loaded.Window.Height);
        Assert.False(loaded.Window.IsMaximized);
        Assert.NotNull(loaded.DisplayProfiles);
        Assert.Empty(loaded.DisplayProfiles);
    }
}
