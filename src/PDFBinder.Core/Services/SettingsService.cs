using System.IO;
using System.Text.Json;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// アプリケーション設定の永続化および読み込みを行うサービス実装クラス
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// 設定ファイルの絶対パス
    /// </summary>
    public string SettingsFilePath { get; }

    /// <summary>
    /// <see cref="SettingsService"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="settingsFilePath">カスタム設定ファイルパス（未指定時はEXE実行フォルダ内の settings.json）</param>
    public SettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json")
            : settingsFilePath;
    }

    /// <inheritdoc />
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(SettingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? new AppSettings();
        }
        catch (Exception)
        {
            // 設定ファイルの読み込みやJSONデシリアライズ失敗時はデフォルト設定を返却して安全に継続
            return new AppSettings();
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            string? dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception)
        {
            // 書き込み権限不足等の環境でもアプリの終了を阻害しないよう例外を安全に抑止
        }
    }
}
