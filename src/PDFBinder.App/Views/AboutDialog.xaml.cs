using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using PDFBinder.App.Services;

namespace PDFBinder.App.Views;

/// <summary>
/// アプリケーションのバージョンおよびライセンス情報を表示するモーダルダイアログ。
/// </summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionTextBlock.Text = AppVersionHelper.DisplayVersion;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    /// <summary>
    /// 閉じるボタンのクリックイベントハンドラー。
    /// </summary>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// ハイパーリンクのクリックイベントハンドラー（既定のブラウザでURLを開く）。
    /// </summary>
    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"URL起動に失敗しました: {ex.Message}");
        }
        e.Handled = true;
    }
}
