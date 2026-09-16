using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PDFBinder.Core.Helpers;

namespace PDFBinder.App.Interop.WinTab;

/// <summary>
/// WinTab API連携の実装クラス。
/// タブレットデバイスからの筆圧パケットを受信し、WPF用の座標・正規化筆圧イベントを発行します。
/// </summary>
public class WinTabService : IWinTabService
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private IntPtr _hCtx = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isDisposed;
    private bool _isPenDown;

    /// <inheritdoc />
    public bool IsAvailable => _hCtx != IntPtr.Zero;

    /// <inheritdoc />
    public string DeviceName { get; private set; } = string.Empty;

    /// <inheritdoc />
    public int MaxPressure { get; private set; } = 1024;

    /// <inheritdoc />
    public bool IsPenDown => _isPenDown;

    /// <inheritdoc />
    public event EventHandler<WinTabPacketEventArgs>? PacketReceived;

    /// <inheritdoc />
    public bool Initialize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !WinTabNativeMethods.IsWinTabLibraryAvailable())
        {
            return false;
        }

        try
        {
            if (WinTabNativeMethods.WTInfoW(0, 0, IntPtr.Zero) == 0)
            {
                return false;
            }

            DeviceName = QueryDeviceName();
            MaxPressure = QueryMaxPressure();

            var logContext = CreateDefaultLogContext();
            ConfigureScreenMapping(ref logContext);

            _hCtx = WinTabNativeMethods.WTOpenW(hwnd, ref logContext, true);
            if (_hCtx == IntPtr.Zero)
            {
                return false;
            }

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }

    /// <summary>
    /// WinTabからデフォルト論理コンテキストを取得します。
    /// </summary>
    private static WinTabNativeMethods.LOGCONTEXTW CreateDefaultLogContext()
    {
        var logContext = new WinTabNativeMethods.LOGCONTEXTW();
        int size = Marshal.SizeOf<WinTabNativeMethods.LOGCONTEXTW>();
        IntPtr ptr = Marshal.AllocHGlobal(size);

        try
        {
            WinTabNativeMethods.WTInfoW(WinTabNativeMethods.WTI_DEFCONTEXT, 0, ptr);
            logContext = Marshal.PtrToStructure<WinTabNativeMethods.LOGCONTEXTW>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return logContext;
    }

    /// <summary>
    /// 出力座標系をWindowsの仮想スクリーンピクセル（左上原点）へ合わせるマッピング設定を行います。
    /// </summary>
    private static void ConfigureScreenMapping(ref WinTabNativeMethods.LOGCONTEXTW logContext)
    {
        logContext.lcOptions |= WinTabNativeMethods.CXO_MESSAGES | WinTabNativeMethods.CXO_SYSTEM;
        logContext.lcPktData = WinTabNativeMethods.PK_BUTTONS | WinTabNativeMethods.PK_X | WinTabNativeMethods.PK_Y | WinTabNativeMethods.PK_NORMAL_PRESSURE;
        logContext.lcPktMode = 0; // すべて絶対座標
        logContext.lcMoveMask = WinTabNativeMethods.PK_BUTTONS | WinTabNativeMethods.PK_X | WinTabNativeMethods.PK_Y | WinTabNativeMethods.PK_NORMAL_PRESSURE;
        logContext.lcBtnUpMask = ~0u;
        logContext.lcBtnDnMask = ~0u;

        int virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int virtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int virtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (virtualWidth > 0 && virtualHeight > 0)
        {
            logContext.lcOutOrgX = virtualLeft;
            logContext.lcOutOrgY = virtualTop;
            logContext.lcOutExtX = virtualWidth;
            logContext.lcOutExtY = -virtualHeight; // Y軸反転
        }
    }

    /// <summary>
    /// タブレットデバイス名を取得します。
    /// </summary>
    private static string QueryDeviceName()
    {
        IntPtr ptr = Marshal.AllocHGlobal(256);
        try
        {
            uint size = WinTabNativeMethods.WTInfoW(WinTabNativeMethods.WTI_DEVICES, WinTabNativeMethods.DVC_NAME, ptr);
            if (size > 0)
            {
                return Marshal.PtrToStringUni(ptr) ?? "Generic WinTab Tablet";
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return "Generic WinTab Tablet";
    }

    /// <summary>
    /// タブレットの最大筆圧値を取得します。
    /// </summary>
    private static int QueryMaxPressure()
    {
        int axisSize = Marshal.SizeOf<WinTabNativeMethods.AXIS>();
        IntPtr ptr = Marshal.AllocHGlobal(axisSize);
        try
        {
            uint size = WinTabNativeMethods.WTInfoW(WinTabNativeMethods.WTI_DEVICES, WinTabNativeMethods.DVC_NPRESSURE, ptr);
            if (size > 0)
            {
                var axis = Marshal.PtrToStructure<WinTabNativeMethods.AXIS>(ptr);
                return axis.axMax > 0 ? axis.axMax : 1024;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return 1024;
    }

    /// <summary>
    /// ウィンドウメッセージプロシージャ。WT_PACKETメッセージを処理します。
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == WinTabNativeMethods.WT_PACKET && _hCtx != IntPtr.Zero)
        {
            var packet = new WinTabNativeMethods.PACKET();
            if (WinTabNativeMethods.WTPacket(_hCtx, (uint)lParam, ref packet))
            {
                ProcessPacket(packet);
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 受信したWinTabパケットを解析し、正規化筆圧とイベント種別を判定して発火します。
    /// </summary>
    private void ProcessPacket(WinTabNativeMethods.PACKET packet)
    {
        int rawPressure = packet.pkNormalPressure;
        float factor = WinTabPressureHelper.NormalizePressure(rawPressure, MaxPressure);
        var screenPoint = new Point(packet.pkX, packet.pkY);

        bool tipDown = (packet.pkButtons & 1) != 0 || rawPressure > 0;

        if (tipDown && !_isPenDown)
        {
            _isPenDown = true;
            PacketReceived?.Invoke(this, new WinTabPacketEventArgs(screenPoint, rawPressure, factor, WinTabPacketType.Down));
        }
        else if (tipDown && _isPenDown)
        {
            PacketReceived?.Invoke(this, new WinTabPacketEventArgs(screenPoint, rawPressure, factor, WinTabPacketType.Move));
        }
        else if (!tipDown && _isPenDown)
        {
            _isPenDown = false;
            PacketReceived?.Invoke(this, new WinTabPacketEventArgs(screenPoint, rawPressure, factor, WinTabPacketType.Up));
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        if (_hCtx != IntPtr.Zero)
        {
            WinTabNativeMethods.WTClose(_hCtx);
            _hCtx = IntPtr.Zero;
        }

        _isPenDown = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_isDisposed)
        {
            Close();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
