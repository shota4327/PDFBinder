using System.IO;
using System.Windows;
using PDFBinder.App.Helpers;
using PDFBinder.App.Services;
using PDFBinder.App.ViewModels;

namespace PDFBinder.App;

/// <summary>
/// アプリケーションのエントリポイントおよびライフサイクル制御クラス
/// </summary>
public partial class App : Application
{
    private SingleInstanceManager? _singleInstanceManager;

    /// <summary>
    /// アプリケーション起動時の処理を行います。
    /// 単一インスタンス判定を実施し、既存プロセスへの引数転送またはプライマリとしての起動・IPCサーバー待機を制御します。
    /// </summary>
    /// <param name="e">起動イベント引数</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EnsureLeftAlignedPopups();

        var parseResult = CommandLineArgsHelper.Parse(e.Args);

        _singleInstanceManager = new SingleInstanceManager();
        if (!_singleInstanceManager.TryAcquireOwnership())
        {
            // 既存インスタンスへ引数を送信
            bool sent = await _singleInstanceManager.TrySendToExistingInstanceAsync(parseResult);
            if (sent)
            {
                Shutdown();
                return;
            }

            // 既存インスタンスが応答しなかった場合は自プロセスがプライマリを引き継ぎ
            _singleInstanceManager.TryAcquireOwnership();
        }

        ShutdownMode = ShutdownMode.OnLastWindowClose;
        _singleInstanceManager.StartServer(OnIpcPayloadReceivedAsync);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        WindowActivationHelper.BringToForeground(mainWindow);

        foreach (var file in parseResult.Files)
        {
            await LoadStartupFileAsync(mainWindow, file);
        }
    }

    /// <summary>
    /// プロセス終了時のクリーンアップ処理を行います。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceManager?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 外部の起動インスタンスから名前付きパイプ経由で受信したペイロードを処理します。
    /// </summary>
    private async Task OnIpcPayloadReceivedAsync(SingleInstancePayload payload)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (payload.ForceNewWindow || payload.Files.Count == 0)
            {
                await OpenInNewWindowAsync(payload.Files);
            }
            else
            {
                await OpenInExistingActiveWindowAsync(payload.Files);
            }
        });
    }

    /// <summary>
    /// 新規ウィンドウを作成して指定されたファイル群を開きます。
    /// </summary>
    private static async Task OpenInNewWindowAsync(IReadOnlyList<string> files)
    {
        var window = new MainWindow();
        window.Show();
        WindowActivationHelper.BringToForeground(window);

        foreach (var file in files)
        {
            await LoadStartupFileAsync(window, file);
        }
    }

    /// <summary>
    /// 最も直近にアクティブだった既存ウィンドウで指定されたファイル群を新しいタブとして開きます。
    /// </summary>
    private static async Task OpenInExistingActiveWindowAsync(IReadOnlyList<string> files)
    {
        var window = WindowActivationHelper.GetMostRecentActiveWindow();
        if (window == null)
        {
            window = new MainWindow();
            window.Show();
        }

        WindowActivationHelper.BringToForeground(window);

        foreach (var file in files)
        {
            await LoadStartupFileAsync(window, file);
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

