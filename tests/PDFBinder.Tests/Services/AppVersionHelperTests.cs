using System.Reflection;
using PDFBinder.App.Services;
using Xunit;

namespace PDFBinder.Tests.Services;

/// <summary>
/// <see cref="AppVersionHelper"/> のバージョン文字列取得・検証に関する単体テスト。
/// </summary>
public class AppVersionHelperTests
{
    [Fact]
    public void CurrentVersion_ReturnsNonEmptySemVerString()
    {
        // Act
        var version = AppVersionHelper.CurrentVersion;

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(version));
        // セマンティックバージョニング形式（X.Y.Z）の確認
        var parts = version.Split('.');
        Assert.True(parts.Length >= 2, $"バージョン形式が不正です: {version}");
        foreach (var part in parts)
        {
            Assert.True(int.TryParse(part, out _), $"バージョン構成要素が数値ではありません: {part}");
        }
    }

    [Fact]
    public void DisplayVersion_StartsWithVPrefix()
    {
        // Act
        var displayVersion = AppVersionHelper.DisplayVersion;

        // Assert
        Assert.StartsWith("v", displayVersion);
        Assert.Equal($"v{AppVersionHelper.CurrentVersion}", displayVersion);
    }

    [Fact]
    public void GetVersionString_WhenAssemblyIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => AppVersionHelper.GetVersionString(null!));
    }

    [Fact]
    public void GetVersionString_ForCurrentAssembly_ReturnsValidVersion()
    {
        // Arrange
        var testAssembly = Assembly.GetExecutingAssembly();

        // Act
        var version = AppVersionHelper.GetVersionString(testAssembly);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain("+", version); // ビルドメタデータが除外されていること
    }

    [Fact]
    public void MainViewModel_ShowAboutCommand_InvokesCustomAction()
    {
        // Arrange
        var vm = new PDFBinder.App.ViewModels.MainViewModel();
        var wasCalled = false;
        vm.ShowAboutDialogAction = () => wasCalled = true;

        // Act
        vm.ShowAboutCommand.Execute(null);

        // Assert
        Assert.True(wasCalled);
    }
}

