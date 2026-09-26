using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Services;

/// <summary>
/// ディスプレイ接続環境（モニター名、解像度、画面数）の識別および設定解決を行うサービス
/// </summary>
public class DisplayProfileService : IDisplayProfileService
{
    private readonly Func<IReadOnlyList<DisplayMonitorInfo>> _monitorProvider;

    /// <summary>
    /// <see cref="DisplayProfileService"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="monitorProvider">ディスプレイ情報取得関数（テスト時のモック注入用、既定値は Win32/WPF 検出）</param>
    public DisplayProfileService(Func<IReadOnlyList<DisplayMonitorInfo>>? monitorProvider = null)
    {
        _monitorProvider = monitorProvider ?? DetectDisplayMonitors;
    }

    /// <inheritdoc />
    public string GetCurrentProfileKey()
    {
        var monitors = _monitorProvider();
        return BuildProfileKey(monitors);
    }

    /// <inheritdoc />
    public WindowSettings ResolveEffectiveWindowSettings(AppSettings? settings)
    {
        if (settings == null)
        {
            return new WindowSettings();
        }

        var key = GetCurrentProfileKey();
        if (settings.DisplayProfiles != null &&
            settings.DisplayProfiles.TryGetValue(key, out var profileSettings) &&
            profileSettings != null)
        {
            return new WindowSettings
            {
                Width = profileSettings.Width,
                Height = profileSettings.Height,
                IsMaximized = profileSettings.IsMaximized
            };
        }

        if (settings.Window != null)
        {
            return new WindowSettings
            {
                Width = settings.Window.Width,
                Height = settings.Window.Height,
                IsMaximized = settings.Window.IsMaximized
            };
        }

        return new WindowSettings();
    }

    /// <summary>
    /// ディスプレイ情報リストからプロファイルキー文字列を生成します。
    /// </summary>
    internal static string BuildProfileKey(IReadOnlyList<DisplayMonitorInfo>? monitors)
    {
        if (monitors == null || monitors.Count == 0)
        {
            return "Default_1100x760_1mon";
        }

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        string safeName = SanitizeIdentifier(primary.MonitorName);
        int width = primary.Width > 0 ? primary.Width : 1920;
        int height = primary.Height > 0 ? primary.Height : 1080;
        int count = monitors.Count;

        return $"{safeName}_{width}x{height}_{count}mon";
    }

    /// <summary>
    /// プロファイルキー用文字列として安全な英数字・アンダースコア表現に正規化します。
    /// </summary>
    internal static string SanitizeIdentifier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Display";
        }

        string sanitized = Regex.Replace(raw.Trim(), @"[^a-zA-Z0-9_\-]", "_");
        sanitized = Regex.Replace(sanitized, @"_+", "_").Trim('_', '-');
        return string.IsNullOrEmpty(sanitized) ? "Display" : sanitized;
    }

    /// <summary>
    /// Win32 API を用いて接続中のモニター一覧を取得します。
    /// </summary>
    private static IReadOnlyList<DisplayMonitorInfo> DetectDisplayMonitors()
    {
        var list = new List<DisplayMonitorInfo>();

        try
        {
            NativeDisplayMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr hMon, IntPtr hdc, ref NativeDisplayMethods.RECT rc, IntPtr data) =>
                {
                    var mi = new NativeDisplayMethods.MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(mi);
                    if (NativeDisplayMethods.GetMonitorInfo(hMon, ref mi))
                    {
                        bool isPrimary = (mi.dwFlags & 1) != 0;
                        int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                        int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                        string monitorName = QueryMonitorName(mi.szDevice);

                        list.Add(new DisplayMonitorInfo(monitorName, width, height, isPrimary));
                    }
                    return true;
                },
                IntPtr.Zero);
        }
        catch
        {
            // Win32 API 呼び出し例外時はフォールバックへ
        }

        if (list.Count == 0)
        {
            list.Add(CreateFallbackMonitorInfo());
        }

        return list;
    }

    /// <summary>
    /// 指定されたアダプターデバイス名（例: \\.\DISPLAY1）に接続されたモニターの識別名を取得します。
    /// </summary>
    private static string QueryMonitorName(string adapterDeviceName)
    {
        try
        {
            var d = new NativeDisplayMethods.DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);
            if (NativeDisplayMethods.EnumDisplayDevices(adapterDeviceName, 0, ref d, 1))
            {
                if (!string.IsNullOrWhiteSpace(d.DeviceString) &&
                    !d.DeviceString.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
                {
                    return d.DeviceString;
                }

                if (!string.IsNullOrWhiteSpace(d.DeviceID))
                {
                    var parts = d.DeviceID.Split('#', '\\');
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        return parts[1];
                    }
                }
            }
        }
        catch
        {
            // デバイス情報取得エラー時はフォールバック
        }

        return !string.IsNullOrWhiteSpace(adapterDeviceName) ? adapterDeviceName : "Display";
    }

    /// <summary>
    /// WPF の SystemParameters を基にフォールバック用のモニター情報を生成します。
    /// </summary>
    private static DisplayMonitorInfo CreateFallbackMonitorInfo()
    {
        int width = (int)SystemParameters.PrimaryScreenWidth;
        int height = (int)SystemParameters.PrimaryScreenHeight;
        if (width <= 0) width = 1920;
        if (height <= 0) height = 1080;

        return new DisplayMonitorInfo("DefaultDisplay", width, height, true);
    }
}

/// <summary>
/// ディスプレイ情報取得用の Win32 Native API 定義
/// </summary>
internal static class NativeDisplayMethods
{
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
}
