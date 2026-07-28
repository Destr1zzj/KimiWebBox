using System.Text.RegularExpressions;

namespace KimiWebBox.Usage;

// Ported from KimiTokenMonitor (MIT) — local session-file usage scanning.
// Kimi Code: $KIMI_CODE_HOME/sessions/**/wire.jsonl "usage.record" lines (exact).
// Kimi CLI : ~/.kimi/sessions/**/context*.jsonl "_usage" lines (max token_count, approximate).

internal sealed class UsageRecord
{
    public long TimeMs;
    public string Model = "";
    public long Tokens;
}

internal sealed class PeriodStats
{
    public long TotalTokens;
    public Dictionary<string, long> Models = new();
}

internal sealed class UsageStats
{
    public PeriodStats Today = new();
    public PeriodStats Month = new();
    public PeriodStats AllTime = new();
    public SortedDictionary<string, long> Daily = new();

    public long WeekTokens()
    {
        var cutoff = DateTime.Today.AddDays(-6);
        long sum = 0;
        foreach (var day in Daily)
            if (DateTime.TryParse(day.Key, out var d) && d >= cutoff) sum += day.Value;
        return sum;
    }
}

internal static class UsageScanner
{
    private sealed class CacheEntry
    {
        public long MtimeTicks;
        public long Size;
        public List<UsageRecord> Records = new();
    }

    private static readonly Dictionary<string, CacheEntry> Cache = new();

    private static readonly Regex UsageLine = new(
        "\"type\":\"usage\\.record\".*?\"model\":\"(?<model>[^\"]+)\".*?\"usage\":\\{(?<usage>[^}]*)\\}.*?\"time\":(?<time>\\d+)",
        RegexOptions.Compiled);
    private static readonly Regex NumField = new(
        "\"(?:inputOther|output|inputCacheRead|inputCacheCreation)\":(?<value>\\d+)",
        RegexOptions.Compiled);
    private static readonly Regex CliUsage = new(
        "\"role\":\\s*\"_usage\".*?\"token_count\":\\s*(?<count>\\d+)",
        RegexOptions.Compiled);

    private static string KimiCodeSessionsRoot()
    {
        var home = Environment.GetEnvironmentVariable("KIMI_CODE_HOME");
        if (string.IsNullOrEmpty(home))
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi-code");
        return Path.Combine(home, "sessions");
    }

    private static string KimiCliSessionsRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi", "sessions");

    public static UsageStats Scan()
    {
        var records = new List<UsageRecord>();
        foreach (var file in EnumerateFiles(KimiCodeSessionsRoot(), 5, name => name == "wire.jsonl"))
            records.AddRange(Cached(file, ParseKimiCodeFile));
        foreach (var file in EnumerateFiles(KimiCliSessionsRoot(), 3, name => name.StartsWith("context") && name.EndsWith(".jsonl")))
            records.AddRange(Cached(file, ParseKimiCliFile));
        return Aggregate(records);
    }

    private static IEnumerable<string> EnumerateFiles(string root, int maxDepth, Func<string, bool> match)
    {
        var result = new List<string>();
        if (!Directory.Exists(root)) return result;
        var stack = new Stack<Tuple<string, int>>();
        stack.Push(Tuple.Create(root, 1));
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(item.Item1);
                dirs = item.Item2 < maxDepth ? Directory.GetDirectories(item.Item1) : Array.Empty<string>();
            }
            catch { continue; }
            foreach (var f in files) if (match(Path.GetFileName(f))) result.Add(f);
            foreach (var d in dirs) stack.Push(Tuple.Create(d, item.Item2 + 1));
        }
        return result;
    }

    private static List<UsageRecord> Cached(string path, Func<string, DateTime, List<UsageRecord>> parser)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return new List<UsageRecord>();
        if (Cache.TryGetValue(path, out var hit) && hit.MtimeTicks == info.LastWriteTimeUtc.Ticks && hit.Size == info.Length)
            return hit.Records;
        List<UsageRecord> records;
        try { records = parser(path, info.LastWriteTime); }
        catch { records = new List<UsageRecord>(); }
        Cache[path] = new CacheEntry { MtimeTicks = info.LastWriteTimeUtc.Ticks, Size = info.Length, Records = records };
        return records;
    }

    private static List<UsageRecord> ParseKimiCodeFile(string path, DateTime mtime)
    {
        var records = new List<UsageRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.Contains("\"usage.record\"")) continue;
            var m = UsageLine.Match(line);
            if (!m.Success) continue;
            long tokens = 0;
            foreach (Match field in NumField.Matches(m.Groups["usage"].Value))
                tokens += long.Parse(field.Groups["value"].Value);
            if (tokens <= 0) continue;
            records.Add(new UsageRecord
            {
                TimeMs = long.Parse(m.Groups["time"].Value),
                Model = m.Groups["model"].Value,
                Tokens = tokens,
            });
        }
        return records;
    }

    private static List<UsageRecord> ParseKimiCliFile(string path, DateTime mtime)
    {
        long max = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (!line.Contains("\"_usage\"")) continue;
            var m = CliUsage.Match(line);
            if (m.Success)
            {
                var count = long.Parse(m.Groups["count"].Value);
                if (count > max) max = count;
            }
        }
        var records = new List<UsageRecord>();
        if (max > 0)
            records.Add(new UsageRecord { TimeMs = new DateTimeOffset(mtime).ToUnixTimeMilliseconds(), Model = "kimi-cli", Tokens = max });
        return records;
    }

    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static UsageStats Aggregate(List<UsageRecord> records)
    {
        var stats = new UsageStats();
        var todayKey = DateTime.Now.ToString("yyyy-MM-dd");
        var monthKey = todayKey[..7];
        foreach (var record in records)
        {
            var dateKey = Epoch.AddMilliseconds(record.TimeMs).ToLocalTime().ToString("yyyy-MM-dd");
            AddPeriod(stats.AllTime, record);
            if (dateKey == todayKey) AddPeriod(stats.Today, record);
            if (dateKey.StartsWith(monthKey, StringComparison.Ordinal)) AddPeriod(stats.Month, record);
            stats.Daily[dateKey] = (stats.Daily.TryGetValue(dateKey, out var v) ? v : 0) + record.Tokens;
        }
        return stats;
    }

    private static void AddPeriod(PeriodStats period, UsageRecord record)
    {
        period.TotalTokens += record.Tokens;
        period.Models[record.Model] = (period.Models.TryGetValue(record.Model, out var v) ? v : 0) + record.Tokens;
    }
}
