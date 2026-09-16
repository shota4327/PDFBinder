using System.Runtime.InteropServices;

namespace PDFBinder.App.Interop.WinTab;

/// <summary>
/// WinTab API（wintab32.dll）のP/Invokeおよび定数・構造体定義。
/// </summary>
internal static class WinTabNativeMethods
{
    internal const string WinTabDll = "wintab32.dll";

    // WinTab カテゴリ定数
    internal const uint WTI_DEFCONTEXT = 3;
    internal const uint WTI_DEVICES = 100;
    internal const uint DVC_NAME = 1;
    internal const uint DVC_NPRESSURE = 15;

    // メッセージ定数
    internal const uint WT_DEFBASE = 0x7FF0;
    internal const uint WT_PACKET = WT_DEFBASE + 0;
    internal const uint WT_CTXOPEN = WT_DEFBASE + 1;
    internal const uint WT_CTXCLOSE = WT_DEFBASE + 2;
    internal const uint WT_PROXIMITY = WT_DEFBASE + 5;

    // パケットデータ指定フラグ (WTPKT)
    internal const uint PK_BUTTONS = 0x0040;
    internal const uint PK_X = 0x0080;
    internal const uint PK_Y = 0x0100;
    internal const uint PK_NORMAL_PRESSURE = 0x0400;

    // コンテキストオプションフラグ
    internal const uint CXO_SYSTEM = 0x0001;
    internal const uint CXO_MESSAGES = 0x0004;

    // ボタン状態フラグ
    internal const uint TBN_NONE = 0;
    internal const uint TBN_UP = 1;
    internal const uint TBN_DOWN = 2;

    /// <summary>
    /// タブレット軸情報構造体（筆圧レンジ等の取得用）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AXIS
    {
        public int axMin;
        public int axMax;
        public int axUnits;
        public int axResolution;
    }

    /// <summary>
    /// WinTab論理コンテキスト構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LOGCONTEXTW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)]
        public string lcName;
        public uint lcOptions;
        public uint lcStatus;
        public uint lcLocks;
        public uint lcMsgBase;
        public uint lcDevice;
        public uint lcPktRate;
        public uint lcPktData;
        public uint lcPktMode;
        public uint lcMoveMask;
        public uint lcBtnDnMask;
        public uint lcBtnUpMask;
        public int lcInOrgX;
        public int lcInOrgY;
        public int lcInOrgZ;
        public int lcInExtX;
        public int lcInExtY;
        public int lcInExtZ;
        public int lcOutOrgX;
        public int lcOutOrgY;
        public int lcOutOrgZ;
        public int lcOutExtX;
        public int lcOutExtY;
        public int lcOutExtZ;
        public int lcSensX;
        public int lcSensY;
        public int lcSensZ;
        public bool lcSysMode;
        public int lcSysOrgX;
        public int lcSysOrgY;
        public int lcSysExtX;
        public int lcSysExtY;
        public int lcSysSensX;
        public int lcSysSensY;
    }

    /// <summary>
    /// lcPktData（PK_BUTTONS | PK_X | PK_Y | PK_NORMAL_PRESSURE）に対応するパケットデータ構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct PACKET
    {
        public uint pkButtons;
        public int pkX;
        public int pkY;
        public int pkNormalPressure;
    }

    [DllImport(WinTabDll, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint WTInfoW(uint wCategory, uint nIndex, IntPtr lpOutput);

    [DllImport(WinTabDll, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr WTOpenW(IntPtr hWnd, ref LOGCONTEXTW lpLogCtx, bool fEnable);

    [DllImport(WinTabDll, SetLastError = true)]
    internal static extern bool WTClose(IntPtr hCtx);

    [DllImport(WinTabDll, SetLastError = true)]
    internal static extern bool WTPacket(IntPtr hCtx, uint wSerial, ref PACKET lpPkt);

    [DllImport(WinTabDll, SetLastError = true)]
    internal static extern bool WTEnable(IntPtr hCtx, bool fEnable);

    [DllImport(WinTabDll, SetLastError = true)]
    internal static extern bool WTOverlap(IntPtr hCtx, bool fOverlap);

    /// <summary>
    /// システムに wintab32.dll が存在し、安全にロード可能か検証します。
    /// </summary>
    internal static bool IsWinTabLibraryAvailable()
    {
        if (NativeLibrary.TryLoad(WinTabDll, out IntPtr handle))
        {
            NativeLibrary.Free(handle);
            return true;
        }
        return false;
    }
}
