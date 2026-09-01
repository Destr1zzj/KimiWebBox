using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KimiWebBox.Usage;

// Ported from KimiTokenMonitor (MIT) — quota/limits API client.
// Web cookie (kimi-auth) is primary (5h + weekly + monthly); Code API key is fallback (5h + weekly).

internal sealed class LimitWindow
{
    public string Kind = "";
    public string Label = "";
    public double RemainingPercent;
    public DateTime? ResetsAt;
    public string Detail = "";
}

internal sealed class LimitsResult
{
    public string Status = "notConfigured"; // ok | unauthorized | sourceRateLimited | unavailable | notConfigured
    public string Source = "";
    public List<LimitWindow> Windows = new();
}

internal static class LimitsClient
{
    private const string CodeUsagesUrl = "https://api.kimi.com/coding/v1/usages";
    private const string WebUsagesUrl = "https://www.kimi.com/apiv2/kimi.gateway.billing.v1.BillingService/GetUsages";
    private const string MembershipUrl = "https://www.kimi.com/apiv2/kimi.gateway.membership.v2.MembershipService/GetSubscriptionStats";

    public static string NormalizeWebToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var raw = value.Trim();
        raw = Regex.Replace(raw, "^authorization\\s*:\\s*", "", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, "^bearer\\s+", "", RegexOptions.IgnoreCase).Trim();
        var cookie = Regex.Match(raw, "(?:^|[;\\s])kimi-auth=([^;\\s'\"]+)", RegexOptions.IgnoreCase);
        if (cookie.Success) return cookie.Groups[1].Value.Trim();
        if (Regex.IsMatch(raw, "^(?:cookie\\s*:|curl\\s)", RegexOptions.IgnoreCase) || raw.Contains(';')) return "";
        return raw;
    }

    public static async Task<LimitsResult> Fetch(AppConfig config, int localPort = 0, string? localToken = null)
    {
        var result = new LimitsResult();
        var webToken = NormalizeWebToken(config.KimiAuthToken);
        var key = (config.KimiCodeApiKey ?? "").Trim();

        // 首选:本地 kimi web 的 OAuth 额度接口(CLI 登录态,零配置)
        if (localPort > 0 && !string.IsNullOrEmpty(localToken))
        {
            try
            {
                var local = await FetchLocalOAuth(localPort, localToken);
                if (local.Status == "ok")
                {
                    // 有 cookie 时补一刀每月额度(本地接口不含每月)
                    if (webToken.Length > 0 && !local.Windows.Any(w => w.Kind == "billing"))
                    {
                        try
                        {
                            var billing = (await FetchWeb(webToken)).FirstOrDefault(w => w.Kind == "billing");
                            if (billing != null) local.Windows.Add(billing);
                        }
                        catch { }
                    }
                    return local;
                }
            }
            catch { }
        }

        if (webToken.Length == 0 && key.Length == 0) return result;

        var webWindows = new List<LimitWindow>();
        var errors = new List<string>();
        if (webToken.Length > 0)
        {
            try { webWindows = await FetchWeb(webToken); }
            catch (QuotaError error) { errors.Add(error.Status); }
            catch { errors.Add("unavailable"); }
        }
        var missing = !webWindows.Any(w => w.Kind == "session") || !webWindows.Any(w => w.Kind == "weekly");
        var codeWindows = new List<LimitWindow>();
        if (key.Length > 0 && (webToken.Length == 0 || missing))
        {
            try { codeWindows = await FetchCode(key); }
            catch (QuotaError error) { errors.Add(error.Status); }
            catch { errors.Add("unavailable"); }
        }

        var byKind = new Dictionary<string, LimitWindow>();
        foreach (var w in webWindows.Concat(codeWindows))
            if (!byKind.ContainsKey(w.Kind)) byKind[w.Kind] = w;
        foreach (var kind in new[] { "session", "weekly", "billing" })
            if (byKind.TryGetValue(kind, out var win)) result.Windows.Add(win);

        result.Source = webWindows.Count > 0 ? "web" : "api";
        if (result.Windows.Count > 0) result.Status = "ok";
        else if (errors.Contains("unauthorized")) result.Status = "unauthorized";
        else if (errors.Contains("sourceRateLimited")) result.Status = "sourceRateLimited";
        else result.Status = "unavailable";
        return result;
    }

    private sealed class QuotaError : Exception
    {
        public readonly string Status;
        public QuotaError(string status) { Status = status; }
    }

    // Local kimi web server: GET /api/v1/oauth/usage uses the CLI's own OAuth login.
    // Response: { code, data: { kind:"ok", summary:{window,used,limit,reset_at}, limits:[...] } }
    private static async Task<LimitsResult> FetchLocalOAuth(int port, string token)
    {
        var result = new LimitsResult { Source = "local" };
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/v1/oauth/usage");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        using var response = await client.SendAsync(request);
        ThrowIfBad(response);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var data = Obj(body, "data");
        if (data == null || Str(data, "kind") != "ok")
        {
            result.Status = "unavailable";
            return result;
        }

        void AddEntry(JsonObject? entry)
        {
            if (entry == null) return;
            var used = Num(entry, "used");
            var limit = Num(entry, "limit");
            if (!used.HasValue || limit is not > 0) return;
            var minutes = WindowMinutes(Obj(entry, "window"));
            if (!minutes.HasValue) return;
            var kind = minutes.Value <= 360 ? "session" : minutes.Value <= 10800 ? "weekly" : "billing";
            if (result.Windows.Any(w => w.Kind == kind)) return;
            result.Windows.Add(new LimitWindow
            {
                Kind = kind,
                Label = kind == "session" ? "5 小时" : kind == "weekly" ? "每周" : "每月",
                RemainingPercent = Clamp(100 - used.Value / limit.Value * 100),
                ResetsAt = ParseTime(entry, "reset_at", "resetAt", "resetTime"),
            });
        }

        foreach (var entry in Arr(data, "limits") ?? new JsonArray()) AddEntry(entry as JsonObject);
        AddEntry(Obj(data, "summary"));
        result.Windows = result.Windows
            .OrderBy(w => w.Kind == "session" ? 0 : w.Kind == "weekly" ? 1 : 2)
            .ToList();
        result.Status = result.Windows.Count > 0 ? "ok" : "unavailable";
        return result;
    }

    private static async Task<List<LimitWindow>> FetchWeb(string token)
    {
        var windows = new List<LimitWindow>();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var membership = await Post(client, MembershipUrl, WebHeaders(token), "{}");
            windows.AddRange(ParseMembership(membership));
        }
        catch (QuotaError) { throw; }
        catch { }
        if (!windows.Any(w => w.Kind == "session") || !windows.Any(w => w.Kind == "weekly"))
        {
            var usage = await Post(client, WebUsagesUrl, WebHeaders(token), "{\"scope\":[\"FEATURE_CODING\"]}");
            windows.AddRange(ParseWebUsage(usage));
        }
        return windows;
    }

    private static Dictionary<string, string> WebHeaders(string token)
    {
        var headers = new Dictionary<string, string>
        {
            { "Authorization", "Bearer " + token },
            { "Cookie", "kimi-auth=" + token },
            { "Origin", "https://www.kimi.com" },
            { "Referer", "https://www.kimi.com/code/console" },
            { "connect-protocol-version", "1" },
            { "x-msh-platform", "web" },
        };
        var parts = token.Split('.');
        if (parts.Length == 3)
        {
            try
            {
                var payload = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(parts[1])));
                var obj = JsonNode.Parse(payload) as JsonObject;
                if (obj != null)
                {
                    var deviceId = Str(obj, "device_id"); if (deviceId.Length > 0) headers["x-msh-device-id"] = deviceId;
                    var ssid = Str(obj, "ssid"); if (ssid.Length > 0) headers["x-msh-session-id"] = ssid;
                    var sub = Str(obj, "sub"); if (sub.Length > 0) headers["x-traffic-id"] = sub;
                }
            }
            catch { }
        }
        return headers;
    }

    private static string PadBase64(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        while (padded.Length % 4 != 0) padded += "=";
        return padded;
    }

    private static async Task<JsonNode?> Post(HttpClient client, string url, Dictionary<string, string> headers, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        ThrowIfBad(response);
        var text = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(text);
    }

    private static async Task<List<LimitWindow>> FetchCode(string key)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, CodeUsagesUrl);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        using var response = await client.SendAsync(request);
        ThrowIfBad(response);
        var text = await response.Content.ReadAsStringAsync();
        return ParseUsageShape(JsonNode.Parse(text));
    }

    private static void ThrowIfBad(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var code = (int)response.StatusCode;
        throw new QuotaError(code is 401 or 403 ? "unauthorized" : code == 429 ? "sourceRateLimited" : "unavailable");
    }

    // ---- JsonNode helpers (mirror the original Dictionary<string,object> helpers) ----

    private static JsonObject? Obj(JsonNode? parent, params string[] keys)
    {
        foreach (var key in keys)
            if (parent is JsonObject o && o.TryGetPropertyValue(key, out var v) && v is JsonObject oo)
                return oo;
        return null;
    }

    private static JsonArray? Arr(JsonNode? parent, params string[] keys)
    {
        foreach (var key in keys)
            if (parent is JsonObject o && o.TryGetPropertyValue(key, out var v) && v is JsonArray a)
                return a;
        return null;
    }

    private static double? Num(JsonNode? parent, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parent is not JsonObject o || !o.TryGetPropertyValue(key, out var v) || v is not JsonValue jv) continue;
            if (jv.TryGetValue<double>(out var d)) return d;
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<string>(out var s) &&
                double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return null;
    }

    private static string Str(JsonNode? parent, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parent is not JsonObject o || !o.TryGetPropertyValue(key, out var v) || v == null) continue;
            if (v is JsonValue jv && jv.TryGetValue<string>(out var s)) return s;
            return v.ToString();
        }
        return "";
    }

    private static DateTime? ParseTime(JsonNode? parent, params string[] keys)
    {
        var raw = Str(parent, keys);
        if (raw.Length == 0) return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)) return parsed;
        return null;
    }

    private static double Clamp(double value) => Math.Max(0, Math.Min(100, value));

    private static double? UsedPercent(JsonNode? detail)
    {
        var used = Num(detail, "used", "usedValue", "currentValue", "consumed");
        var limit = Num(detail, "limit", "limitValue", "total", "quota", "max");
        if (used.HasValue && limit is > 0) return Clamp(used.Value / limit.Value * 100);
        var remaining = Num(detail, "remaining", "remainingValue");
        if (remaining.HasValue && limit is > 0) return Clamp((limit.Value - remaining.Value) / limit.Value * 100);
        var percent = Num(detail, "percent", "percentage", "usedPercent", "usagePercentage");
        if (percent.HasValue) return Clamp(percent.Value);
        return null;
    }

    private static double? Ratio(JsonNode? parent, params string[] keys)
    {
        var value = Num(parent, keys);
        if (!value.HasValue || value.Value < 0) return null;
        return Clamp(value.Value <= 1 ? value.Value * 100 : value.Value);
    }

    private static int? WindowMinutes(JsonNode? window)
    {
        var amount = Num(window, "duration", "windowDuration", "size", "value");
        if (!amount.HasValue || amount.Value <= 0) return null;
        var unit = Str(window, "timeUnit", "time_unit", "unit").ToUpperInvariant();
        if (unit.Contains("MIN")) return (int)amount.Value;
        if (unit.Contains("HOUR")) return (int)(amount.Value * 60);
        if (unit.Contains("DAY")) return (int)(amount.Value * 24 * 60);
        if (unit.Contains("WEEK")) return (int)(amount.Value * 7 * 24 * 60);
        if (unit.Contains("MONTH")) return (int)(amount.Value * 30 * 24 * 60);
        return null;
    }

    private static List<LimitWindow> ParseUsageShape(JsonNode? body)
    {
        var windows = new List<LimitWindow>();
        var seen = new HashSet<string>();
        foreach (var entry in Arr(body, "limits", "limitInfos", "rateLimits") ?? new JsonArray())
        {
            var entryObj = entry as JsonObject;
            if (entryObj == null) continue;
            var detail = Obj(entryObj, "detail", "usage", "quota") ?? entryObj;
            var usedPercent = UsedPercent(detail);
            if (!usedPercent.HasValue) continue;
            var window = Obj(entryObj, "window", "period", "rateLimit", "timeWindow") ?? entryObj;
            var minutes = WindowMinutes(window);
            var kind = minutes.HasValue && minutes.Value <= 360 ? "session" : "weekly";
            seen.Add(kind);
            windows.Add(new LimitWindow
            {
                Kind = kind,
                Label = kind == "session" ? "5-hour" : "Weekly",
                RemainingPercent = 100 - usedPercent.Value,
                ResetsAt = ParseTime(detail, "resetTime", "reset_time", "resetAt") ?? ParseTime(window, "resetTime", "resetAt"),
            });
        }
        var usage = Obj(body, "usage");
        if (usage != null)
        {
            var usedPercent = UsedPercent(usage);
            var name = Str(usage, "name", "label", "title");
            var kind = Regex.IsMatch(name, "hour|小时", RegexOptions.IgnoreCase) ? "session" : "weekly";
            if (usedPercent.HasValue && !seen.Contains(kind))
                windows.Add(new LimitWindow
                {
                    Kind = kind,
                    Label = name.Length > 0 ? name : (kind == "session" ? "5-hour" : "Weekly"),
                    RemainingPercent = 100 - usedPercent.Value,
                    ResetsAt = ParseTime(usage, "resetTime", "reset_time", "resetAt"),
                });
        }
        return windows;
    }

    private static List<LimitWindow> ParseWebUsage(JsonNode? body)
    {
        foreach (var entry in Arr(body, "usages") ?? new JsonArray())
        {
            var entryObj = entry as JsonObject;
            if (entryObj == null || Str(entryObj, "scope") != "FEATURE_CODING") continue;
            var shaped = new JsonObject();
            var detail = Obj(entryObj, "detail");
            if (detail != null) shaped["usage"] = detail.DeepClone();
            if (entryObj.TryGetPropertyValue("limits", out var limits) && limits != null) shaped["limits"] = limits.DeepClone();
            return ParseUsageShape(shaped);
        }
        return new List<LimitWindow>();
    }

    private static LimitWindow? RateWindow(JsonNode? body, string[] keys, string kind, string label)
    {
        var source = Obj(body, keys);
        if (source == null) return null;
        var usedPercent = Ratio(source, "ratio", "usedRatio", "used_ratio");
        if (!usedPercent.HasValue) return null;
        return new LimitWindow
        {
            Kind = kind,
            Label = label,
            RemainingPercent = 100 - usedPercent.Value,
            ResetsAt = ParseTime(source, "resetTime", "reset_time", "resetAt"),
        };
    }

    private static List<LimitWindow> ParseMembership(JsonNode? body)
    {
        var windows = new List<LimitWindow>();
        var session = RateWindow(body, new[] { "ratelimitCode5h", "ratelimit_code_5h", "ratelimit5h" }, "session", "5-hour");
        var weekly = RateWindow(body, new[] { "ratelimitCode7d", "ratelimit_code_7d", "ratelimit7d" }, "weekly", "Weekly");
        if (session != null) windows.Add(session);
        if (weekly != null) windows.Add(weekly);
        var balance = Obj(body, "subscriptionBalance", "subscription_balance");
        if (balance != null)
        {
            var usedPercent = Ratio(balance, "amountUsedRatio", "amount_used_ratio");
            if (usedPercent.HasValue)
            {
                var codePercent = Ratio(balance, "kimiCodeUsedRatio", "kimi_code_used_ratio");
                var detail = "";
                if (codePercent.HasValue)
                {
                    var safeCode = Math.Min(usedPercent.Value, codePercent.Value);
                    detail = string.Format(CultureInfo.InvariantCulture, "Kimi {0:0.##}% · Code {1:0.##}%", usedPercent.Value - safeCode, safeCode);
                }
                windows.Add(new LimitWindow
                {
                    Kind = "billing",
                    Label = "Monthly",
                    RemainingPercent = 100 - usedPercent.Value,
                    ResetsAt = ParseTime(balance, "expireTime", "expire_time"),
                    Detail = detail,
                });
            }
        }
        return windows;
    }
}
