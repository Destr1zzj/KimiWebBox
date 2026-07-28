using System.Text.Json;

namespace KimiWebBox.Usage;

// Ported from KimiTokenMonitor (MIT). Stored next to the exe as config.local.json.
// NOTE: contains secrets — never commit (covered by *.local.json in .gitignore).

internal sealed class AppConfig
{
    public string KimiAuthToken = "";
    public string KimiCodeApiKey = "";

    private static string ConfigPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.local.json");

    public static AppConfig Load()
    {
        var config = new AppConfig
        {
            KimiAuthToken = Clean(Environment.GetEnvironmentVariable("KIMI_AUTH_TOKEN"))
                ?? Clean(Environment.GetEnvironmentVariable("KIMI_MANUAL_COOKIE")) ?? "",
            KimiCodeApiKey = Clean(Environment.GetEnvironmentVariable("KIMI_CODE_API_KEY")) ?? "",
        };
        if (config.KimiAuthToken.Length > 0 || config.KimiCodeApiKey.Length > 0) return config;
        try
        {
            if (File.Exists(ConfigPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                var root = doc.RootElement;
                config.KimiAuthToken = Clean(root.TryGetProperty("kimiAuthToken", out var t) ? t.GetString() : "") ?? "";
                config.KimiCodeApiKey = Clean(root.TryGetProperty("kimiCodeApiKey", out var k) ? k.GetString() : "") ?? "";
            }
        }
        catch { }
        return config;
    }

    public void Save()
    {
        var payload = new Dictionary<string, string>();
        if (KimiAuthToken.Length > 0) payload["kimiAuthToken"] = KimiAuthToken;
        if (KimiCodeApiKey.Length > 0) payload["kimiCodeApiKey"] = KimiCodeApiKey;
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload));
        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
        File.Move(tmp, ConfigPath);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var raw = value.Trim();
        if (raw.Length >= 2 && raw.StartsWith('\"') && raw.EndsWith('\"')) raw = raw[1..^1].Trim();
        return raw.Length > 0 ? raw : null;
    }
}
