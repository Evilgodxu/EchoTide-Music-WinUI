using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YiChao.Data.Settings;

/// <summary>
/// 应用偏好：曲库目录列表。写在 LocalAppData 下的单个 JSON，读失败即回落默认值——
/// 偏好丢失不该阻断启动。
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YiChao");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>用户选定的曲库目录，数量不限。空列表表示还没配过。</summary>
    [JsonPropertyName("libraryPaths")]
    public List<string> LibraryPaths { get; set; } = new();

    /// <summary>主题模式，默认跟随系统。</summary>
    [JsonPropertyName("theme")]
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>界面语言，默认跟随系统。</summary>
    [JsonPropertyName("language")]
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>收藏：按曲目全路径记，与曲库标识同口径。</summary>
    [JsonPropertyName("favorites")]
    public List<string> Favorites { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (settings is not null)
                {
                    settings.LibraryPaths ??= new List<string>();
                    settings.Favorites ??= new List<string>();
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // 配置损坏按首次启动处理
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
        }
        catch (Exception)
        {
            // 写入失败只影响下次启动读到的目录，不向用户报错
        }
    }
}
