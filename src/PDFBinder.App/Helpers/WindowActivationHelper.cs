using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PDFBinder.App.Helpers;

/// <summary>
/// メインウィンドウのアクティブ履歴の追跡および安全な最前面化・フォーカス復元を行うヘルパークラス
/// </summary>
public static class WindowActivationHelper
{
    private static readonly Dictionary<MainWindow, DateTime> WindowActivationTimes = new();
    private static readonly object LockObject = new();

    private const int SwRestore = 9;
    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>
    /// メインウィンドウのアクティブ化イベントおよび終了イベントを購読し、履歴管理に登録します。
    /// </summary>
    /// <param name="window">対象のメインウィンドウ</param>
    public static void RegisterWindow(MainWindow window)
    {
        lock (LockObject)
        {
            WindowActivationTimes[window] = DateTime.UtcNow;
        }

        window.Activated += OnWindowActivated;
        window.Closed += OnWindowClosed;
    }

    /// <summary>
    /// ウィンドウのアクティブ化イベントハンドラー
    /// </summary>
    private static void OnWindowActivated(object? sender, EventArgs e)
    {
        if (sender is not MainWindow window) return;

        lock (LockObject)
        {
            WindowActivationTimes[window] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// ウィンドウのクローズイベントハンドラー
    /// </summary>
    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not MainWindow window) return;

        window.Activated -= OnWindowActivated;
        window.Closed -= OnWindowClosed;

        lock (LockObject)
        {
            WindowActivationTimes.Remove(window);
        }
    }

    /// <summary>
    /// 現在開いているメインウィンドウの中で、最も直近にアクティブ（操作）されていたウィンドウを取得します。
    /// </summary>
    /// <returns>直近アクティブなメインウィンドウ（存在しない場合はnull）</returns>
    public static MainWindow? GetMostRecentActiveWindow()
    {
        lock (LockObject)
        {
            // 登録済みウィンドウの中で生存しているものを最終アクティブ降順で取得
            var recent = WindowActivationTimes
                .Where(kvp => kvp.Key.IsLoaded)
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (recent != null) return recent;
        }

        // 辞書に見つからない場合は Application.Current.Windows からフォールバック探索
        return Application.Current?.Windows
            .OfType<MainWindow>()
            .FirstOrDefault(w => w.IsLoaded);
    }

    /// <summary>
    /// 指定されたウィンドウを最小化から復元し、最前面（アクティブ状態）に表示します。
    /// </summary>
    /// <param name="window">対象のウィンドウ</param>
    public static void BringToForeground(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
        }

        // WPF層での一時的なTopmost切り替えによりフォーカスを確実に確立
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    /// <summary>
    /// 他のプロセスがフォアグラウンドウィンドウを設定することを許可します。
    /// IPCクライアントが終了直前に呼び出します。
    /// </summary>
    public static void GrantForegroundPermission()
    {
        try
        {
            AllowSetForegroundWindow(AsfwAny);
        }
        catch
        {
            // Win32呼び出し失敗時は安全に無視
        }
    }
}
