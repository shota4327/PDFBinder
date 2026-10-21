using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// <see cref="AppSettings"/> のリフレクションフリーなシリアライズ・デシリアライズを行う JSON コンテキスト
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// アプリケーション設定の永続化および読み込みを行うサービス実装クラス
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly object _cacheLock = new();
    private AppSettings? _cachedSettings;

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
        lock (_cacheLock)
        {
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            _cachedSettings = LoadFromDisk();
            return _cachedSettings;
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_cacheLock)
        {
            _cachedSettings = settings;

            try
            {
                string? dir = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception)
            {
                // 書き込み権限不足等の環境でもアプリの終了を阻害しないよう例外を安全に抑止
            }
        }
    }

    /// <summary>
    /// メモリ上の設定キャッシュを破棄し、次回 <see cref="Load"/> 時にディスクから再読み込みさせます。
    /// </summary>
    internal void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedSettings = null;
        }
    }

    /// <summary>
    /// ディスク上の設定ファイルから設定を読み込み、デシリアライズします。
    /// </summary>
    private AppSettings LoadFromDisk()
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

            var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            return settings ?? new AppSettings();
        }
        catch (Exception)
        {
            // 設定ファイルの読み込みやJSONデシリアライズ失敗時はデフォルト設定を返却して安全に継続
            return new AppSettings();
        }
    }
}
