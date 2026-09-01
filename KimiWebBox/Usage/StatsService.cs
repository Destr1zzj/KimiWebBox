namespace KimiWebBox.Usage;

internal sealed class StatsSnapshot
{
    public long Today, Week, Month, AllTime;
    public string LimitsStatus = "notConfigured";
    public List<LimitWindow> Windows = new();
    public SortedDictionary<string, long> Daily = new();
    public Dictionary<string, long> ModelsToday = new();
    public Dictionary<string, long> ModelsMonth = new();
    public DateTime UpdatedAt = DateTime.Now;
}

/// <summary>
/// Periodic stats: local usage scan every 60s (cached, cheap),
/// quota API every 5min (only when credentials are configured).
/// </summary>
internal sealed class StatsService : IDisposable
{
    private readonly AppConfig _config;
    private readonly System.Threading.Timer _usageTimer;
    private readonly System.Threading.Timer _limitsTimer;
    private int _limitsBusy;

    public StatsSnapshot Current { get; } = new();
    public event Action<StatsSnapshot>? Updated;

    // Local kimi web server coordinates — set by MainForm once the server is up.
    public int LocalPort;
    public string? LocalToken;

    public StatsService(AppConfig config)
    {
        _config = config;
        _usageTimer = new System.Threading.Timer(_ => RefreshUsage(), null, 1500, 60_000);
        _limitsTimer = new System.Threading.Timer(_ => _ = RefreshLimitsAsync(), null, 4000, 300_000);
    }

    public void RefreshUsage()
    {
        Task.Run(() =>
        {
            try
            {
                var s = UsageScanner.Scan();
                Current.Today = s.Today.TotalTokens;
                Current.Week = s.WeekTokens();
                Current.Month = s.Month.TotalTokens;
                Current.AllTime = s.AllTime.TotalTokens;
                Current.Daily = new SortedDictionary<string, long>(s.Daily);
                Current.ModelsToday = new Dictionary<string, long>(s.Today.Models);
                Current.ModelsMonth = new Dictionary<string, long>(s.Month.Models);
                Current.UpdatedAt = DateTime.Now;
                Updated?.Invoke(Current);
            }
            catch { }
        });
    }

    private DateTime _lastImportTry = DateTime.MinValue;

    public async Task RefreshLimitsAsync()
    {
        if (Interlocked.Exchange(ref _limitsBusy, 1) != 0) return;
        try
        {
            var r = await LimitsClient.Fetch(_config, LocalPort, LocalToken);
            // 每月额度走 kimi.com access_token（会过期）——缺失时经 WebBridge 自动重取，最多 10 分钟一次
            if (r.Windows.All(w => w.Kind != "billing") && DateTime.Now - _lastImportTry > TimeSpan.FromMinutes(10))
            {
                _lastImportTry = DateTime.Now;
                var fresh = await WebBridgeToken.TryFetchAsync(allowOpenTab: string.IsNullOrEmpty(_config.KimiAuthToken));
                if (!string.IsNullOrEmpty(fresh) && fresh != _config.KimiAuthToken)
                {
                    ShellLog.Write("webbridge: imported kimi.com access_token");
                    _config.KimiAuthToken = fresh;
                    try { _config.Save(); } catch { }
                    r = await LimitsClient.Fetch(_config, LocalPort, LocalToken);
                }
            }
            ShellLog.Write($"limits: status={r.Status} source={r.Source} windows={r.Windows.Count}");
            Current.LimitsStatus = r.Status;
            Current.Windows = r.Windows;
            Current.UpdatedAt = DateTime.Now;
            Updated?.Invoke(Current);
        }
        catch { }
        finally { Interlocked.Exchange(ref _limitsBusy, 0); }
    }

    public void Dispose()
    {
        _usageTimer.Dispose();
        _limitsTimer.Dispose();
    }
}
