using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// アプリケーション設定の永続化および読み込みを担うサービスインターフェース
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// 設定ファイルから設定情報を読み込みます。
    /// ファイルが存在しない場合や破損している場合は、デフォルト設定を返却します。
    /// </summary>
    /// <returns>読み込まれたアプリケーション設定</returns>
    AppSettings Load();

    /// <summary>
    /// 指定された設定情報を設定ファイルへ保存します。
    /// 書き込み権限エラー等が発生した場合は例外を内部で捕捉し、安全に処理します。
    /// </summary>
    /// <param name="settings">保存するアプリケーション設定</param>
    void Save(AppSettings settings);
}
