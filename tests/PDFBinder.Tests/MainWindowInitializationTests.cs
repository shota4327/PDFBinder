using System.Windows;
using PDFBinder.App;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="MainWindow"/> のXAML構文および初期化処理の単体テスト
/// </summary>
public class MainWindowInitializationTests
{
    [Fact]
    public void MainWindow_ShouldInitializeWithoutXamlParseException()
    {
        Exception? caughtException = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var app = new PDFBinder.App.App();
                    app.InitializeComponent();
                }

                var window = new MainWindow();
                Assert.NotNull(window);
                window.Close();
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(TimeSpan.FromSeconds(10));

        Assert.True(finished, "MainWindow の初期化テストがタイムアウトしました。");
        Assert.Null(caughtException);
    }

    [Fact]
    public void MainWindow_WithCustomSettings_ShouldRestoreSizeAndMaximizedState()
    {
        Exception? caughtException = null;
        var fakeSettingsService = new FakeSettingsService(new PDFBinder.Core.Models.AppSettings
        {
            Window = new PDFBinder.Core.Models.WindowSettings
            {
                Width = 1200,
                Height = 850,
                IsMaximized = true
            }
        });

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var app = new PDFBinder.App.App();
                    app.InitializeComponent();
                }

                var window = new MainWindow(fakeSettingsService);
                Assert.Equal(1200, window.Width);
                Assert.Equal(850, window.Height);
                Assert.Equal(WindowState.Maximized, window.WindowState);

                window.Close();
                Assert.NotNull(fakeSettingsService.SavedSettings);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(TimeSpan.FromSeconds(10));

        Assert.True(finished, "MainWindow の設定復元テストがタイムアウトしました。");
        Assert.Null(caughtException);
    }

    [Fact]
    public void MainWindow_WithDisplayProfileSettings_ShouldRestoreProfileSpecificSizeAndSaveProfile()
    {
        Exception? caughtException = null;
        var fakeProfileService = new FakeDisplayProfileService("CustomMonitor_2560x1440_1mon");
        var fakeSettingsService = new FakeSettingsService(new PDFBinder.Core.Models.AppSettings
        {
            Window = new PDFBinder.Core.Models.WindowSettings { Width = 1100, Height = 760, IsMaximized = false },
            DisplayProfiles = new Dictionary<string, PDFBinder.Core.Models.WindowSettings>
            {
                ["CustomMonitor_2560x1440_1mon"] = new PDFBinder.Core.Models.WindowSettings
                {
                    Width = 1400,
                    Height = 900,
                    IsMaximized = false
                }
            }
        });

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var app = new PDFBinder.App.App();
                    app.InitializeComponent();
                }

                var window = new MainWindow(fakeSettingsService, fakeProfileService);
                Assert.Equal(1400, window.Width);
                Assert.Equal(900, window.Height);

                window.Close();

                Assert.NotNull(fakeSettingsService.SavedSettings);
                Assert.NotNull(fakeSettingsService.SavedSettings.DisplayProfiles);
                Assert.True(fakeSettingsService.SavedSettings.DisplayProfiles.ContainsKey("CustomMonitor_2560x1440_1mon"));
                Assert.Equal(1400, fakeSettingsService.SavedSettings.DisplayProfiles["CustomMonitor_2560x1440_1mon"].Width);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(TimeSpan.FromSeconds(10));

        Assert.True(finished, "MainWindow のディスプレイプロファイル復元テストがタイムアウトしました。");
        Assert.Null(caughtException);
    }

    private class FakeDisplayProfileService : PDFBinder.App.Services.IDisplayProfileService
    {
        private readonly string _profileKey;

        public FakeDisplayProfileService(string profileKey)
        {
            _profileKey = profileKey;
        }

        public string GetCurrentProfileKey() => _profileKey;

        public PDFBinder.Core.Models.WindowSettings ResolveEffectiveWindowSettings(PDFBinder.Core.Models.AppSettings? settings)
        {
            if (settings?.DisplayProfiles != null && settings.DisplayProfiles.TryGetValue(_profileKey, out var val))
            {
                return val;
            }
            return settings?.Window ?? new PDFBinder.Core.Models.WindowSettings();
        }
    }

    private class FakeSettingsService : PDFBinder.Core.Services.ISettingsService
    {
        private readonly PDFBinder.Core.Models.AppSettings _settings;
        public PDFBinder.Core.Models.AppSettings? SavedSettings { get; private set; }

        public FakeSettingsService(PDFBinder.Core.Models.AppSettings settings)
        {
            _settings = settings;
        }

        public PDFBinder.Core.Models.AppSettings Load() => _settings;

        public void Save(PDFBinder.Core.Models.AppSettings settings)
        {
            SavedSettings = settings;
        }
    }
}
