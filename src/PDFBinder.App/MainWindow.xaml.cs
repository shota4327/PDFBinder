using System;
using System.Windows;
using System.Windows.Input;
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

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (s, e) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (s, e) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (s, e) => SystemCommands.RestoreWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (s, e) => SystemCommands.CloseWindow(this)));

        StateChanged += OnWindowStateChanged;
    }

    /// <summary>
    /// ウィンドウ状態の変更を検知し、最大化/復元ボタンのグリフとツールチップを更新します。
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MaximizeRestoreGlyph.Text = "\uE923";
            MaximizeRestoreButton.ToolTip = "元に戻す";
        }
        else
        {
            MaximizeRestoreGlyph.Text = "\uE922";
            MaximizeRestoreButton.ToolTip = "最大化";
        }
    }

    /// <summary>
    /// 最大化/復元ボタンのクリックを処理します。
    /// </summary>
    private void OnMaximizeRestoreClicked(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }
}