using PDFBinder.Core.Models;

namespace PDFBinder.App.Services;

/// <summary>
/// ディスプレイ情報（モニター識別子、解像度、プライマリフラグ）を保持するレコード
/// </summary>
/// <param name="MonitorName">モニター名または識別コード（例: T27h-30）</param>
/// <param name="Width">画面解像度の幅（ピクセル）</param>
/// <param name="Height">画面解像度の高さ（ピクセル）</param>
/// <param name="IsPrimary">プライマリモニターであるかどうかのフラグ</param>
public record DisplayMonitorInfo(string MonitorName, int Width, int Height, bool IsPrimary);

/// <summary>
/// 現在のディスプレイ接続構成（モニター名、解像度、画面数）の識別および設定解決を行うサービスのインターフェース
/// </summary>
public interface IDisplayProfileService
{
    /// <summary>
    /// 現在のアクティブなディスプレイ環境を一意に表すプロファイルキーを取得します。
    /// </summary>
    /// <returns>ディスプレイプロファイルキー（例: "T27h-30_2560x1440_1mon"）</returns>
    string GetCurrentProfileKey();

    /// <summary>
    /// 設定情報から、現在のディスプレイプロファイルに対応する最適なウィンドウ設定を解決します。
    /// プロファイルが存在しない場合は、共通の WindowSettings または既定値へフォールバックします。
    /// </summary>
    /// <param name="settings">アプリケーション設定情報</param>
    /// <returns>解決されたウィンドウ設定</returns>
    WindowSettings ResolveEffectiveWindowSettings(AppSettings? settings);
}
