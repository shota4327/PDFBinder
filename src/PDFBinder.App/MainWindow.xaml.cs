using System.Windows;
using PDFBinder.App.ViewModels;

namespace PDFBinder.App;

/// <summary>
/// アプリケーションのメインウィンドウ
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}