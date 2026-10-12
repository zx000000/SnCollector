using System.Text.Json;
using SnCollector.Core.Models;

namespace SnCollector.Core.Storage;

/// <summary>配置本地 JSON 存储：%AppData%/SnCollector/config.json，启动自动加载</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ConfigPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SnCollector");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "config.json");
        }
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg is not null && cfg.Items.Count > 0)
                    return cfg;
            }
        }
        catch
        {
            // 配置损坏时回退默认
        }
        var def = new AppConfig();
        Save(def);
        return def;
    }

    public static void Save(AppConfig config) =>
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
}
