#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PDFBinder.App.Controls;

/// <summary>
/// OLEドラッグ＆ドロップ中およびドラッグオーバー中におけるマウスホイール回転を検知するヘルパークラス
/// </summary>
public sealed class DragMouseWheelHook : IDisposable
{
    private const int WH_MOUSE = 7;
    private const int WM_MOUSEWHEEL = 0x020A;

    private readonly HookProc _hookProc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private bool _isFilterAttached;
    private bool _disposed;

    /// <summary>
    /// マウスホイール回転が検知された際に呼び出されるコールバック（引数はホイールのデルタ値）
    /// </summary>
    public event Action<int>? MouseWheelRotated;

    /// <summary>
    /// DragMouseWheelHook クラスの新しいインスタンスを初期化します。
    /// </summary>
    public DragMouseWheelHook()
    {
        // ガベージコレクションによるコールバック破棄を防ぐため参照を保持
        _hookProc = MouseHookCallback;
    }

    /// <summary>
    /// アプリ内ドラッグ（DoDragDrop）用のスレッドローカルマウスフックを開始します。
    /// </summary>
    public void StartHook()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            uint threadId = GetCurrentThreadId();
            _hookHandle = SetWindowsHookEx(WH_MOUSE, _hookProc, IntPtr.Zero, threadId);
        }

        AttachThreadFilter();
    }

    /// <summary>
    /// 外部ドラッグ時など、ComponentDispatcher 経由でのメッセージ監視を開始します。
    /// </summary>
    public void AttachThreadFilter()
    {
        if (!_isFilterAttached)
        {
            ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
            _isFilterAttached = true;
        }
    }

    /// <summary>
    /// フックおよびメッセージ監視を停止します。
    /// </summary>
    public void StopHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        if (_isFilterAttached)
        {
            ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
            _isFilterAttached = false;
        }
    }

    /// <summary>
    /// WH_MOUSE フックからのコールバックを処理します。
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL)
        {
            var hookStruct = Marshal.PtrToStructure<MOUSEHOOKSTRUCTEX>(lParam);
            short delta = unchecked((short)((hookStruct.mouseData >> 16) & 0xffff));
            if (delta != 0)
            {
                MouseWheelRotated?.Invoke(delta);
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// ComponentDispatcher からの Windows メッセージをフィルタリングします。
    /// </summary>
    private void OnThreadFilterMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == WM_MOUSEWHEEL)
        {
            short delta = unchecked((short)((msg.wParam.ToInt64() >> 16) & 0xffff));
            if (delta != 0)
            {
                MouseWheelRotated?.Invoke(delta);
                // 通常のフォーカススクロールと二重に発火しないよう handled を設定可能
                handled = true;
            }
        }
    }

    /// <summary>
    /// リソースを解放し、フックを安全に解除します。
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            StopHook();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~DragMouseWheelHook()
    {
        Dispose();
    }

    #region Win32 Native Interop

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEHOOKSTRUCTEX
    {
        public POINT pt;
        public IntPtr hwnd;
        public uint wHitTestCode;
        public UIntPtr dwExtraInfo;
        public uint mouseData;
    }

    #endregion
}
