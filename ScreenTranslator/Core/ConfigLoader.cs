using System.Text.Json;
using System.Text.Encodings.Web;

namespace ScreenTranslator.Core;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            return new AppConfig();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
    }

    /// <summary>把配置写回磁盘（供 UI 改动模型等持久化到下次启动）。</summary>
    public static void Save(string path, AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(path, json);
    }
}
