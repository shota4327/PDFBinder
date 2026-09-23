using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PDFBinder.App.Services;

namespace PDFBinder.App.Views;

/// <summary>
/// アプリケーションのバージョンおよびライセンス情報を表示するインアプリ・オーバーレイコントロール。
/// </summary>
public partial class AboutOverlayControl : UserControl
{
    /// <summary>
    /// <see cref="AboutOverlayControl"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    public AboutOverlayControl()
    {
        InitializeComponent();
        VersionTextBlock.Text = AppVersionHelper.DisplayVersion;
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
            e.Handled = true;
        }
        catch
        {
            // ブラウザ起動失敗時は静かに無視
        }
    }
}
