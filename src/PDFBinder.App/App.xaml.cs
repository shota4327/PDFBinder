using System.IO;
using System.Windows;
using PDFBinder.App.Helpers;
using PDFBinder.App.ViewModels;

namespace PDFBinder.App;

/// <summary>
/// アプリケーションのエントリポイントおよびライフサイクル制御クラス
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// アプリケーション起動時の処理を行います。
    /// コマンドライン引数を解析し、対象PDFの自動読み込みおよび複数ファイル指定時の別プロセス起動を制御します。
    /// </summary>
    /// <param name="e">起動イベント引数</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var parseResult = CommandLineArgsHelper.Parse(e.Args);

        // 複数ファイルが渡された場合、2つ目以降のファイルを別プロセスとして起動
        foreach (var additionalFile in parseResult.AdditionalFiles)
        {
            CommandLineArgsHelper.LaunchAdditionalProcess(additionalFile);
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (!string.IsNullOrEmpty(parseResult.PrimaryFile))
        {
            await LoadStartupFileAsync(mainWindow, parseResult.PrimaryFile);
        }
    }

    /// <summary>
    /// 起動時に指定されたPDFファイルをメインウィンドウで読み込みます。
    /// </summary>
    /// <param name="window">メインウィンドウ</param>
    /// <param name="filePath">読み込むPDFファイルのフルパス</param>
    private static async Task LoadStartupFileAsync(MainWindow window, string filePath)
    {
        if (window.DataContext is not MainViewModel vm) return;

        if (!File.Exists(filePath))
        {
            MessageBox.Show(window, $"指定されたPDFファイルが見つかりませんでした:\n{filePath}", "ファイル読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            vm.StatusMessage = $"ファイルが見つかりません: {filePath}";
            return;
        }

        await vm.OpenDocumentAsync(filePath);
    }
}

