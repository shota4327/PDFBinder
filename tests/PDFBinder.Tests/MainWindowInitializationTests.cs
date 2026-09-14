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
}
