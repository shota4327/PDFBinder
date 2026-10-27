using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PDFBinder.App.Helpers;

/// <summary>
/// Windows OS のシステムアクセントカラーを取得し、アプリケーションリソースへ反映・追従するヘルパークラス
/// </summary>
public static class SystemAccentColorHelper
{
    /// <summary>
    /// アプリケーションリソース内のアクセントカラーブラシのキー名
    /// </summary>
    public const string ResourceKey = "SystemAccentBrush";

    /// <summary>
    /// アクセントカラーが取得できない場合の既定フォールバック色（Windows 既定ブルー #0078D4）
    /// </summary>
    public static readonly Color FallbackColor = Color.FromRgb(0, 120, 212);

    private const int WmDwmColorizationColorChanged = 0x0320;
    private const int WmSettingChange = 0x001A;

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetColorizationColor")]
    private static extern int DwmGetColorizationColor(out uint pcrColorization, [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);

    /// <summary>
    /// 現在の Windows システムアクセントカラーを取得します。
    /// </summary>
    /// <returns>取得したアクセントカラー、またはフォールバック色</returns>
    public static Color GetAccentColor()
    {
        if (TryGetDwmColor(out var dwmColor))
        {
            return dwmColor;
        }

        if (TryGetRegistryAccentColor(out var registryColor))
        {
            return registryColor;
        }

        return FallbackColor;
    }

    /// <summary>
    /// DWM API (dwmapi.dll) 経由でアクセントカラーの取得を試行します。
    /// </summary>
    private static bool TryGetDwmColor(out Color color)
    {
        try
        {
            int hr = DwmGetColorizationColor(out uint colorization, out _);
            if (hr == 0 && colorization != 0)
            {
                var parsed = ParseColorizationColor(colorization);
                if (parsed.R != 0 || parsed.G != 0 || parsed.B != 0)
                {
                    color = parsed;
                    return true;
                }
            }
        }
        catch
        {
            // DWM 非サポート環境や API 呼び出し失敗時の保護
        }

        color = default;
        return false;
    }

    /// <summary>
    /// Windows レジストリ（HKCU\Software\Microsoft\Windows\DWM）からアクセントカラーの取得を試行します。
    /// </summary>
    private static bool TryGetRegistryAccentColor(out Color color)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key != null)
            {
                // AccentColor (ABGR) の取得
                var accentVal = key.GetValue("AccentColor");
                if (accentVal is int accentInt && accentInt != 0)
                {
                    var parsed = ParseAbgrColor((uint)accentInt);
                    if (parsed.R != 0 || parsed.G != 0 || parsed.B != 0)
                    {
                        color = parsed;
                        return true;
                    }
                }

                // ColorizationColor (ARGB) の取得
                var colorVal = key.GetValue("ColorizationColor");
                if (colorVal is int colorInt && colorInt != 0)
                {
                    var parsed = ParseColorizationColor((uint)colorInt);
                    if (parsed.R != 0 || parsed.G != 0 || parsed.B != 0)
                    {
                        color = parsed;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // レジストリアクセス権限エラー等の保護
        }

        color = default;
        return false;
    }

    /// <summary>
    /// 32ビット ARGB カラー値を <see cref="Color"/> に変換します（不透明度255）。
    /// </summary>
    /// <param name="colorization">32ビット ARGB 値</param>
    /// <returns>変換後の <see cref="Color"/></returns>
    public static Color ParseColorizationColor(uint colorization)
    {
        byte r = (byte)((colorization >> 16) & 0xFF);
        byte g = (byte)((colorization >> 8) & 0xFF);
        byte b = (byte)(colorization & 0xFF);
        return Color.FromRgb(r, g, b);
    }

    /// <summary>
    /// 32ビット ABGR カラー値を <see cref="Color"/> に変換します（不透明度255）。
    /// </summary>
    /// <param name="abgr">32ビット ABGR 値</param>
    /// <returns>変換後の <see cref="Color"/></returns>
    public static Color ParseAbgrColor(uint abgr)
    {
        byte r = (byte)(abgr & 0xFF);
        byte g = (byte)((abgr >> 8) & 0xFF);
        byte b = (byte)((abgr >> 16) & 0xFF);
        return Color.FromRgb(r, g, b);
    }

    /// <summary>
    /// アプリケーションリソース内のアクセントカラーブラシを最新のシステム値で更新します。
    /// </summary>
    /// <param name="resources">対象のリソースディクショナリ（未指定時は Application.Current.Resources）</param>
    public static void UpdateBrush(ResourceDictionary? resources = null)
    {
        var targetResources = resources ?? Application.Current?.Resources;
        if (targetResources == null) return;

        var color = GetAccentColor();
        if (targetResources.Contains(ResourceKey) &&
            targetResources[ResourceKey] is SolidColorBrush existingBrush &&
            !existingBrush.IsFrozen)
        {
            existingBrush.Color = color;
        }
        else
        {
            targetResources[ResourceKey] = new SolidColorBrush(color);
        }
    }

    /// <summary>
    /// ウィンドウに初期カラーを反映し、OS 設定変更メッセージを監視するフックを登録します。
    /// </summary>
    /// <param name="window">対象ウィンドウ</param>
    /// <param name="resources">対象のリソースディクショナリ（テスト用DI）</param>
    public static void Initialize(Window window, ResourceDictionary? resources = null)
    {
        UpdateBrush(resources);

        var source = PresentationSource.FromVisual(window) as HwndSource;
        if (source != null)
        {
            source.AddHook(WndProc);
            window.Closed += (s, e) => source.RemoveHook(WndProc);
        }
    }

    /// <summary>
    /// Windows メッセージプロシージャ（DWM カラー変更およびシステム設定変更を監視）
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WmDwmColorizationColorChanged or WmSettingChange)
        {
            UpdateBrush();
        }

        return IntPtr.Zero;
    }
}
