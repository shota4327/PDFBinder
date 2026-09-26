using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PDFBinder.App.Helpers;
using PDFBinder.App.Services;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App;

/// <summary>
/// アプリケーションのメインウィンドウ
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly IDisplayProfileService _displayProfileService;
    private bool _isClosingConfirmed;
    private bool _isSettingsSaved;

    /// <summary>
    /// <see cref="MainWindow"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="settingsService">設定サービス（テスト用DI、未指定時は既定の SettingsService）</param>
    /// <param name="displayProfileService">ディスプレイプロファイルサービス（テスト用DI、未指定時は既定の DisplayProfileService）</param>
    public MainWindow(ISettingsService? settingsService = null, IDisplayProfileService? displayProfileService = null)
    {
        _settingsService = settingsService ?? new SettingsService();
        _displayProfileService = displayProfileService ?? new DisplayProfileService();

        InitializeComponent();
        DataContext = new MainViewModel();

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (s, e) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (s, e) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (s, e) => SystemCommands.RestoreWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (s, e) => SystemCommands.CloseWindow(this)));

        StateChanged += OnWindowStateChanged;

        RestoreWindowSettings();
        WindowActivationHelper.RegisterWindow(this);
    }

    /// <summary>
    /// キー入力を先行検知し、フォーカス位置に関わらずショートカットキーの確実な実行を制御します。
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not MainViewModel vm) return;

        // 0. エラー・警告通知ダイアログのキー制御
        if (HandleErrorDialogKeyDown(vm, e)) return;

        // 1. 保存確認ダイアログのキー制御
        if (HandleSaveConfirmationKeyDown(vm, e)) return;

        // 2. 印刷確認ダイアログのキー制御
        if (HandlePrintDialogKeyDown(vm, e)) return;

        // 3. バージョン情報ダイアログのキー制御
        if (HandleAboutDialogKeyDown(vm, e)) return;

        // 3. テキストボックス編集中（ページ番号入力欄等）は、文字入力・カーソル移動・削除を優先
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
    /// エラー・警告通知ダイアログ表示中のキーボード操作（Enter/Esc/Spaceによる閉じる等）を先行処理します。
    /// </summary>
    private static bool HandleErrorDialogKeyDown(MainViewModel vm, KeyEventArgs e)
    {
        if (!vm.IsErrorDialogVisible) return false;

        if (e.Key is Key.Escape or Key.Enter or Key.Space)
        {
            vm.CloseErrorDialog();
            e.Handled = true;
            return true;
        }

        // エラー・警告通知ダイアログ表示中は、ダイアログ操作以外のグローバルショートカットキーを抑止
        if (Keyboard.Modifiers == ModifierKeys.Control || e.Key == Key.Delete)
        {
            e.Handled = true;
            return true;
        }

        return false;
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
    /// 印刷確認ダイアログ表示中のキーボード操作（Escによるキャンセル等）を先行処理します。
    /// </summary>
    private static bool HandlePrintDialogKeyDown(MainViewModel vm, KeyEventArgs e)
    {
        if (!vm.IsPrintDialogVisible) return false;

        if (e.Key == Key.Escape)
        {
            vm.ClosePrintDialogCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        // 印刷ダイアログ表示中は、ダイアログ操作以外のグローバルショートカットキーを抑止
        if (Keyboard.Modifiers == ModifierKeys.Control || e.Key == Key.Delete)
        {
            e.Handled = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// バージョン情報ダイアログ表示中のキーボード操作（Escによる閉じる等）を先行処理します。
    /// </summary>
    private static bool HandleAboutDialogKeyDown(MainViewModel vm, KeyEventArgs e)
    {
        if (!vm.IsAboutDialogVisible) return false;

        if (e.Key == Key.Escape)
        {
            vm.CloseAboutCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        // バージョン情報ダイアログ表示中は、ダイアログ操作以外のグローバルショートカットキーを抑止
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
    /// 設定情報から現在のディスプレイ環境に応じたウィンドウサイズおよび最大化状態を復元します。
    /// </summary>
    private void RestoreWindowSettings()
    {
        var settings = _settingsService.Load();
        var effective = _displayProfileService.ResolveEffectiveWindowSettings(settings);

        var workArea = SystemParameters.WorkArea;
        var (adjustedWidth, adjustedHeight) = WindowBoundsHelper.AdjustBounds(
            effective.Width,
            effective.Height,
            workArea.Width,
            workArea.Height,
            MinWidth,
            MinHeight);

        Width = adjustedWidth;
        Height = adjustedHeight;

        if (effective.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// 現在のウィンドウサイズおよび最大化状態を設定情報（ディスプレイプロファイル別および共通設定）へ保存します。
    /// </summary>
    private void SaveWindowSettings()
    {
        if (_isSettingsSaved) return;
        _isSettingsSaved = true;

        var settings = _settingsService.Load() ?? new AppSettings();
        settings.Window ??= new WindowSettings();
        settings.DisplayProfiles ??= new Dictionary<string, WindowSettings>();

        var currentSettings = CaptureCurrentWindowSettings();

        // 共通フォールバック設定を更新
        settings.Window.Width = currentSettings.Width;
        settings.Window.Height = currentSettings.Height;
        settings.Window.IsMaximized = currentSettings.IsMaximized;

        // 現在のディスプレイプロファイル別設定を更新
        var profileKey = _displayProfileService.GetCurrentProfileKey();
        settings.DisplayProfiles[profileKey] = currentSettings;

        _settingsService.Save(settings);
    }

    /// <summary>
    /// 現在のウィンドウの表示状態（幅・高さ・最大化状態）を取得します。
    /// </summary>
    private WindowSettings CaptureCurrentWindowSettings()
    {
        var current = new WindowSettings();

        if (WindowState == WindowState.Maximized)
        {
            current.IsMaximized = true;
            var bounds = RestoreBounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                current.Width = bounds.Width;
                current.Height = bounds.Height;
            }
        }
        else if (WindowState == WindowState.Normal)
        {
            current.IsMaximized = false;
            current.Width = ActualWidth > 0 ? ActualWidth : Width;
            current.Height = ActualHeight > 0 ? ActualHeight : Height;
        }
        else // 最小化時
        {
            current.IsMaximized = false;
            var bounds = RestoreBounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                current.Width = bounds.Width;
                current.Height = bounds.Height;
            }
        }

        return current;
    }

    /// <summary>
    /// ウィンドウ終了時に未保存の変更がある場合、確認ダイアログを表示して終了処理を制御します。
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_isClosingConfirmed)
        {
            SaveWindowSettings();
            return;
        }

        if (DataContext is not MainViewModel vm || !vm.HasModifiedDocuments)
        {
            SaveWindowSettings();
            return;
        }

        // 既に確認ダイアログが表示中の場合は多重呼び出しを防止
        if (vm.IsSaveConfirmationVisible)
        {
            e.Cancel = true;
            return;
        }

        // 未保存変更があるため一旦ウィンドウクローズをキャンセルし、順次確認を実施
        e.Cancel = true;

        bool canClose = await vm.ConfirmSaveAllAsync();
        if (canClose)
        {
            _isClosingConfirmed = true;
            SaveWindowSettings();
            Close();
        }
    }

    private void OnFileDropdownItemClick(object sender, RoutedEventArgs e)
    {
        if (FindName("FileDropdownToggle") is System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            toggle.IsChecked = false;
        }
    }

    private void OnFileDropdownCloseClick(object sender, RoutedEventArgs e)
    {
        if (FindName("FileDropdownToggle") is System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            toggle.IsChecked = false;
        }
    }

    /// <summary>
    /// タイトルバー中央のファイル切り替えポップアップをトグルボタンの中央揃えで配置します。
    /// </summary>
    private CustomPopupPlacement[] OnFileDropdownPopupPlacement(Size popupSize, Size targetSize, Point offset)
    {
        return CalculateFileDropdownPopupPlacement(popupSize, targetSize, offset);
    }

    /// <summary>
    /// ポップアップがトグルボタンの中央下部に揃う座標を計算します（テスト用ヘルパー）。
    /// </summary>
    internal static CustomPopupPlacement[] CalculateFileDropdownPopupPlacement(Size popupSize, Size targetSize, Point offset)
    {
        // トグルボタンの中心とポップアップの中心が一致するように X 座標をオフセット
        double x = (targetSize.Width - popupSize.Width) / 2.0;
        double y = targetSize.Height + 2.0;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
    }

    /// <summary>
    /// ウィンドウが閉じられた際の最終処理を行います。
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SaveWindowSettings();
    }

    /// <summary>
    /// ウィンドウ状態の変更を検知し、最大化/復元ボタンのグリフとツールチップを更新します。
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MaximizeRestoreGlyph.Text = "\uE3E0";
            MaximizeRestoreButton.ToolTip = "元に戻す";
        }
        else
        {
            MaximizeRestoreGlyph.Text = "\uE3C6";
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

            // グリッドビュー表示中かつページが存在し、PDFが含まれる場合は、GridView 側の OnGridDrop に委ねる
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool hasPdf = files != null && files.Any(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
                if (!vm.IsDetailViewActive && vm.Document.Pages.Count > 0 && hasPdf)
                {
                    return;
                }

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
            bool hasSupportedFile = files != null && files.Any(IsSupportedDropFile);
            if (hasSupportedFile)
            {
                e.Effects = DragDropEffects.Copy;
                // 詳細ビュー表示中のみ全画面ドロップオーバーレイを表示
                if (vm.Document.Pages.Count > 0 && vm.IsDetailViewActive)
                {
                    vm.IsDragOver = true;
                    e.Handled = true;
                }
                else
                {
                    vm.IsDragOver = false;
                }
                return;
            }
        }

        e.Effects = DragDropEffects.None;
        if (DataContext is MainViewModel vmReset)
        {
            vmReset.IsDragOver = false;
        }
    }

    private static bool IsSupportedDropFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = System.IO.Path.GetExtension(path);
        return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// タイトルバーのアプリアイコンクリック時にプルダウン（コンテキストメニュー）を表示します。
    /// </summary>
    private void OnAppIconButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// バージョン情報ダイアログの背景（暗転部分）クリック時にダイアログを閉じます。
    /// </summary>
    private void OnAboutBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.CloseAboutCommand.Execute(null);
        }
    }

    /// <summary>
    /// バージョン情報ダイアログのカード本体クリック時にイベントのバブリングを防止します。
    /// </summary>
    private void OnAboutCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}