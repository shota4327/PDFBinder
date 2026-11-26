using System.Runtime.InteropServices;

namespace PDFBinder.App.Services;

/// <summary>
/// Win32 GDI 印刷用の P/Invoke 宣言および定数・構造体定義
/// </summary>
internal static class GdiNativeMethods
{
    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;
    public const int PHYSICALWIDTH = 110;
    public const int PHYSICALHEIGHT = 111;
    public const int PHYSICALOFFSETX = 112;
    public const int PHYSICALOFFSETY = 113;

    public const int HALFTONE = 4;
    public const uint DIB_RGB_COLORS = 0;
    public const uint SRCCOPY = 0x00CC0020;
    public const uint BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DOCINFOW
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszDocName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszOutput;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszDatatype;
        public uint fwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateDCW(
        string? lpszDriver,
        string? lpszDevice,
        string? lpszOutput,
        nint lpInitData);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll", EntryPoint = "StartDocW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int StartDocW(nint hdc, ref DOCINFOW lpdi);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int EndDoc(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int AbortDoc(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int StartPage(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int EndPage(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(nint hdc, int index);

    [DllImport("gdi32.dll")]
    public static extern int SaveDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RestoreDC(nint hdc, int nSavedDC);

    [DllImport("gdi32.dll")]
    public static extern int IntersectClipRect(nint hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern int SetStretchBltMode(nint hdc, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetBrushOrgEx(nint hdc, int x, int y, out POINT lppt);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int StretchDIBits(
        nint hdc,
        int xDest,
        int yDest,
        int destWidth,
        int destHeight,
        int xSrc,
        int ySrc,
        int srcWidth,
        int srcHeight,
        byte[] lpBits,
        ref BITMAPINFOHEADER lpbmi,
        uint iUsage,
        uint rop);
}
