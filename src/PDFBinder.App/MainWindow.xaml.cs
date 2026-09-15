using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    /// キー入力を先行検知し、フォーカス位置に関わらずショートカットキーの確実な実行を制御します。
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not MainViewModel vm) return;

        // 1. 保存確認ダイアログのキー制御
        if (HandleSaveConfirmationKeyDown(vm, e)) return;

        // 2. テキストボックス編集中（ページ番号入力欄等）は、文字入力・カーソル移動・削除を優先
        if (Keyboard.FocusedElement is TextBoxBase || e.OriginalSource is TextBoxBase)
        {
            return;
        }

        // 3. 詳細ビューでのページ移動（PgUp/PgDn/矢印キー）
        if (HandlePageNavigationKeyDown(vm, e)) return;

        // 4. その他のグローバルショートカット（Window.InputBindings）
        HandleGlobalInputBindingsKeyDown(vm, e);
    }

    /// <summary>
    /// 保存確認ダイアログ表示中のキーボード操作（Escによるキャンセル等）を先行処理します。
    /// </summary>
    private static bool HandleSaveConfirmationKeyDown(MainViewModel vm, KeyEventArgs e)
    {
        if (!vm.IsSaveConfirmationVisible) return false;

        if (e.Key == Key.Escape)
        {
            vm.ConfirmSave(SaveConfirmationResult.Cancel);
            e.Handled = true;
            return true;
        }

        // 保存確認ダイアログ表示中は、ダイアログ操作以外のグローバルショートカットキーを抑止
        if (Keyboard.Modifiers == ModifierKeys.Control || e.Key == Key.Delete)
        {
            e.Handled = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 詳細ビューにおけるPgUp/PgDnおよび矢印キー（↑↓←→）によるページ移動を先行処理します。
    /// </summary>
    private static bool HandlePageNavigationKeyDown(MainViewModel vm, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandlePageNavigation(vm, key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 指定されたキーおよび修飾キーをもとにページ移動コマンドを実行可能か判定し、実行します（テスト可能な静的ヘルパー）。
    /// </summary>
    internal static bool HandlePageNavigation(MainViewModel vm, Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.None) return false;

        if (key is Key.PageUp or Key.Up or Key.Left)
        {
            if (vm.CanGoToPreviousPage)
            {
                vm.GoToPreviousPageCommand.Execute(null);
                return true;
            }
        }
        else if (key is Key.PageDown or Key.Down or Key.Right)
        {
            if (vm.CanGoToNextPage)
            {
                vm.GoToNextPageCommand.Execute(null);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ウィンドウのInputBindingsに定義されたショートカットを先行実行し、子コントロールによる消費を防ぎます。
    /// </summary>
    private bool HandleGlobalInputBindingsKeyDown(MainViewModel vm, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // 選択ツールでインクストロークが選択されている場合は、ストローク消去を優先するためDeleteキーをパススルー
        if (key == Key.Delete && modifiers == ModifierKeys.None && vm.IsDetailViewActive)
        {
            if (Keyboard.FocusedElement is InkCanvas inkCanvas && inkCanvas.GetSelectedStrokes().Count > 0)
            {
                return false;
            }
        }

        foreach (InputBinding ib in InputBindings)
        {
            if (ib is KeyBinding kb && kb.Key == key && kb.Modifiers == modifiers)
            {
                if (kb.Command != null && kb.Command.CanExecute(kb.CommandParameter))
                {
                    kb.Command.Execute(kb.CommandParameter);
                    e.Handled = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// ウィンドウ終了時に未保存の変更がある場合、確認ダイアログを表示して終了処理を制御します。
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_isClosingConfirmed) return;

        if (DataContext is not MainViewModel vm || !vm.Document.IsModified || vm.Document.Pages.Count == 0)
        {
            return;
        }

        // 既に確認ダイアログが表示中の場合は多重呼び出しを防止
        if (vm.IsSaveConfirmationVisible)
        {
            e.Cancel = true;
            return;
        }

        // 未保存変更があるため一旦ウィンドウクローズをキャンセルし、インアプリオーバーレイを表示
        e.Cancel = true;

        var choice = await vm.PromptSaveConfirmationAsync(vm.Document.FileName);
        if (choice == SaveConfirmationResult.Cancel)
        {
            return;
        }

        if (choice == SaveConfirmationResult.Discard)
        {
            _isClosingConfirmed = true;
            Close();
            return;
        }

        if (choice == SaveConfirmationResult.Save)
        {
            bool saved = await vm.SaveDocumentAsync();
            if (saved)
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

    /// <summary>
    /// ステータスバーのページ番号入力欄でのEnterキー押下時にバインディングを更新してフォーカスを外します。
    /// </summary>
    private void OnPageNumberTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is System.Windows.Controls.TextBox tb)
        {
            var binding = tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
            binding?.UpdateSource();
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }
}