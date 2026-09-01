using System.Text;
using System.Text.Json;

namespace KimiWebBox.Usage;

/// <summary>
/// Auto-imports the kimi.com access_token from the user's real browser via the local
/// Kimi WebBridge daemon (127.0.0.1:10086). Read-only, best-effort, silent on failure.
/// With allowOpenTab=false it only reads when the user's ACTIVE tab is kimi.com;
/// with allowOpenTab=true it may open a tab, read, then close it (one-shot bootstrap).
/// </summary>
internal static class WebBridgeToken
{
    private const string Daemon = "http://127.0.0.1:10086/command";
    private const string Session = "kimiwebbox";

    public static async Task<string?> TryFetchAsync(bool allowOpenTab)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var found = await Post(http, new { action = "find_tab", args = new { url = "https://www.kimi.com", active = true }, session = Session });
            if (found != null && found.Contains("\"success\":true"))
            {
                var token = await ReadToken(http);
                if (!string.IsNullOrEmpty(token)) return token;
                ShellLog.Write("webbridge: active kimi.com tab found but token empty");
            }

            if (!allowOpenTab) return null;

            var nav = await Post(http, new { action = "navigate", args = new { url = "https://www.kimi.com/code/console", newTab = true, group_title = "KimiWebBox" }, session = Session });
            if (nav == null || !nav.Contains("\"success\":true"))
            {
                ShellLog.Write("webbridge: navigate failed (daemon/extension offline?)");
                return null;
            }
            await Task.Delay(4000);
            try
            {
                var token = await ReadToken(http);
                if (string.IsNullOrEmpty(token)) ShellLog.Write("webbridge: tab opened but token empty");
                return string.IsNullOrEmpty(token) ? null : token;
            }
            finally
            {
                await Post(http, new { action = "close_tab", args = new { }, session = Session });
            }
        }
        catch (Exception ex) { ShellLog.Write("webbridge: " + ex.Message); return null; }
    }

    private static async Task<string?> ReadToken(HttpClient http)
    {
        var resp = await Post(http, new { action = "evaluate", args = new { code = "localStorage.getItem('access_token')" }, session = Session });
        if (resp == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(resp);
            var value = doc.RootElement.GetProperty("data").GetProperty("value").GetString();
            return string.IsNullOrWhiteSpace(value) || value == "null" ? null : value;
        }
        catch { return null; }
    }

    private static async Task<string?> Post(HttpClient http, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var res = await http.PostAsync(Daemon, new StringContent(json, Encoding.UTF8, "application/json"));
            return await res.Content.ReadAsStringAsync();
        }
        catch { return null; }
    }
}
