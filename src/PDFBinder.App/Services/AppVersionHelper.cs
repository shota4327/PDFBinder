using System.Reflection;

namespace PDFBinder.App.Services;

/// <summary>
/// アプリケーションのバージョン情報取得を支援するヘルパークラス。
/// </summary>
public static class AppVersionHelper
{
    /// <summary>
    /// 現在実行中のアセンブリのセマンティックバージョン文字列（例: "0.1.0"）を取得します。
    /// </summary>
    public static string CurrentVersion => GetVersionString(Assembly.GetEntryAssembly() ?? typeof(AppVersionHelper).Assembly);

    /// <summary>
    /// 表示用のフォーマット済みバージョン文字列（例: "v0.1.0"）を取得します。
    /// </summary>
    public static string DisplayVersion => $"v{CurrentVersion}";

    /// <summary>
    /// 指定されたアセンブリからバージョン文字列を抽出します。
    /// </summary>
    /// <param name="assembly">対象アセンブリ</param>
    /// <returns>セマンティックバージョン文字列</returns>
    public static string GetVersionString(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        // 1. AssemblyInformationalVersionAttribute の確認（ビルドメタデータやコミットハッシュが付与される場合があるため "+" 前を抽出）
        var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (!string.IsNullOrWhiteSpace(infoVersionAttr?.InformationalVersion))
        {
            var raw = infoVersionAttr.InformationalVersion.Trim();
            var plusIndex = raw.IndexOf('+');
            return plusIndex > 0 ? raw[..plusIndex] : raw;
        }

        // 2. AssemblyName.Version の確認（Major.Minor.Build）
        var version = assembly.GetName().Version;
        if (version != null)
        {
            return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }

        return "0.1.0";
    }
}
