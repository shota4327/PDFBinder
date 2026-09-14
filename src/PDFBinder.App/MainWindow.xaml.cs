using System;
using System.ComponentModel;
using System.Linq;
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

    private bool _isClosingConfirmed;

    /// <summary>
    /// ウィンドウ終了時に未保存の変更がある場合、確認ダイアログを表示して終了処理を制御します。
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_isClosingConfirmed) return;

        if (DataContext is MainViewModel vm && vm.Document.IsModified && vm.Document.Pages.Count > 0)
        {
            e.Cancel = true;
            bool canClose = await vm.ConfirmSaveAndProceedAsync();
            if (canClose)
            {
                _isClosingConfirmed = true;
                Close();
            }
        }
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

    /// <summary>
    /// メイン領域へのドラッグ進入時の処理を行います。
    /// </summary>
    private void OnMainAreaDragEnter(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    /// <summary>
    /// メイン領域上でのドラッグ移動中の処理を行います。
    /// </summary>
    private void OnMainAreaDragOver(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    /// <summary>
    /// ドラッグ離脱時の処理を行います。
    /// </summary>
    private void OnMainAreaDragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsDragOver = false;
        }
    }

    /// <summary>
    /// メイン領域への外部ファイルドロップを処理します。
    /// </summary>
    private async void OnMainAreaDrop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsDragOver = false;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                await vm.HandleFileDropAsync(files);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// ドラッグ中のデータ種別を判定し、ドロップ効果およびオーバーレイ表示状態を更新します。
    /// </summary>
    private void UpdateDragState(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is MainViewModel vm)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            bool hasPdf = files != null && files.Any(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (hasPdf)
            {
                e.Effects = DragDropEffects.Copy;
                if (vm.Document.Pages.Count > 0)
                {
                    vm.IsDragOver = true;
                }
                e.Handled = true;
                return;
            }
        }

        e.Effects = DragDropEffects.None;
        if (DataContext is MainViewModel vmReset)
        {
            vmReset.IsDragOver = false;
        }
    }
}