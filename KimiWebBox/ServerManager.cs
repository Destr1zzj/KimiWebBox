using System.Diagnostics;

namespace KimiWebBox;

/// <summary>
/// Finds or starts the local "kimi web" server and hands out its URL.
/// Probe-first design: any already-running server is adopted, never duplicated.
/// </summary>
internal sealed class ServerManager
{
    private const int PortStart = 58627;
    private const int PortEnd = 58637;

    public int? Port { get; private set; }
    public string? LastError { get; private set; }

    private readonly string _kimiExe;
    private readonly string _logPath;
    private Process? _ownedProcess;

    public ServerManager()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _kimiExe = Path.Combine(home, ".kimi-code", "bin", "kimi.exe");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KimiWebBox", "logs");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "kimi-web.log");
    }

    public async Task<bool> EnsureRunningAsync(CancellationToken ct)
    {
        var alive = await ProbeAsync(ct);
        if (alive != null)
        {
            Port = alive;
            return true;
        }

        if (!File.Exists(_kimiExe))
        {
            LastError = $"找不到 kimi CLI：{_kimiExe}";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _kimiExe,
                Arguments = "web --no-open",
                // New sessions created from the UI land in the server's cwd workspace —
                // pin it to the user home instead of inheriting exe dir / System32 (autostart).
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var p = Process.Start(psi);
            if (p == null)
            {
                LastError = "无法启动 kimi web（Process.Start 返回空）";
                return false;
            }
            _ownedProcess = p;
            var log = new StreamWriter(_logPath, append: true) { AutoFlush = true };
            log.WriteLine($"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} started pid={p.Id} ====");
            p.OutputDataReceived += (_, e) => { if (e.Data != null) { try { log.WriteLine(e.Data); } catch { } } };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) { try { log.WriteLine(e.Data); } catch { } } };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            LastError = "启动失败：" + ex.Message;
            return false;
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var port = await ProbeAsync(ct);
            if (port != null)
            {
                Port = port;
                return true;
            }
            if (_ownedProcess.HasExited)
            {
                LastError = $"kimi web 进程已退出（代码 {_ownedProcess.ExitCode}），日志：{_logPath}";
                return false;
            }
            await Task.Delay(500, ct);
        }

        LastError = $"kimi web 30 秒内未就绪，日志：{_logPath}";
        return false;
    }

    private static async Task<int?> ProbeAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };
        for (int port = PortStart; port <= PortEnd; port++)
        {
            try
            {
                using var resp = await http.GetAsync($"http://127.0.0.1:{port}/", ct);
                if ((int)resp.StatusCode < 500) return port;
            }
            catch { }
        }
        return null;
    }

    public string? ReadToken()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var f = Path.Combine(home, ".kimi-code", "server.token");
            return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
        }
        catch { return null; }
    }

    public void StopOwned()
    {
        try { if (_ownedProcess is { HasExited: false }) _ownedProcess.Kill(entireProcessTree: true); } catch { }
    }

    public void RestartOwned()
    {
        StopOwned();
        _ownedProcess = null;
        Port = null;
    }
}
