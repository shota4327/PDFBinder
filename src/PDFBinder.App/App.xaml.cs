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
    /// コマンドライン引数を解析し、対象PDFの自動読み込みおよび複数ファイル指定時の同一ウィンドウ内オープンを制御します。
    /// </summary>
    /// <param name="e">起動イベント引数</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EnsureLeftAlignedPopups();

        var parseResult = CommandLineArgsHelper.Parse(e.Args);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        var startupFiles = new List<string>();
        if (!string.IsNullOrEmpty(parseResult.PrimaryFile))
        {
            startupFiles.Add(parseResult.PrimaryFile);
        }
        startupFiles.AddRange(parseResult.AdditionalFiles);

        foreach (var file in startupFiles)
        {
            await LoadStartupFileAsync(mainWindow, file);
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

    /// <summary>
    /// タブレットモード等のOS設定によりポップアップやメニューが右揃えになる現象を防止し、
    /// 常に左揃えで開くように強制します。
    /// </summary>
    private static void EnsureLeftAlignedPopups()
    {
        try
        {
            if (SystemParameters.MenuDropAlignment)
            {
                var field = typeof(SystemParameters).GetField("_menuDropAlignment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                field?.SetValue(null, false);
            }
        }
        catch
        {
            // リフレクション失敗時は安全に無視
        }
    }
}

