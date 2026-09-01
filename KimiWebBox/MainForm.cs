using System.Diagnostics;
using System.Text.Json;
using KimiWebBox.Quota;
using KimiWebBox.Usage;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace KimiWebBox;

internal sealed class MainForm : Form
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "KimiWebBox";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly ServerManager _server = new();
    private readonly AppConfig _config;
    private readonly StatsService _stats;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly bool _startInTray;

    private bool _allowVisible;
    private bool _reallyExit;
    private bool _webReady;
    private bool _balloonShown;
    private static Icon? _icon;

    public MainForm(bool startInTray)
    {
        _startInTray = startInTray;

        Text = "KimiWebBox";
        Width = 1320;
        Height = 840;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon();
        Controls.Add(_web);

        _config = AppConfig.Load();
        _stats = new StatsService(_config);
        _stats.Updated += OnStatsUpdated;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => ShowFromTray());
        menu.Items.Add("重启服务", null, async (_, _) => await RestartAsync());
        menu.Items.Add("额度设置…", null, (_, _) => OpenSettings());
        _autostartItem = new ToolStripMenuItem("开机自启") { CheckOnClick = true, Checked = GetAutostart() };
        _autostartItem.CheckedChanged += (_, _) => SetAutostart(_autostartItem.Checked);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出并停止服务", null, (_, _) => { _reallyExit = true; Close(); });

        _tray = new NotifyIcon
        {
            Icon = AppIcon(),
            Text = "KimiWebBox",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        Load += async (_, _) => await InitAsync();
        FormClosing += OnFormClosing;
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startInTray && !_allowVisible && !_reallyExit) value = false;
        base.SetVisibleCore(value);
    }

    public void ShowFromTray()
    {
        _allowVisible = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_balloonShown)
            {
                _tray.ShowBalloonTip(2500, "KimiWebBox", "已收进托盘，kimi web 继续在后台运行。", ToolTipIcon.Info);
                _balloonShown = true;
            }
            return;
        }
        _tray.Visible = false;
        if (_reallyExit)
        {
            _stats.Dispose();
            _server.StopOwned();
        }
    }

    private async Task RestartAsync()
    {
        _server.RestartOwned();
        await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            if (!_webReady)
            {
                var dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KimiWebBox", "webview2");
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.WebMessageReceived += (_, e) =>
                {
                    // WebMessageAsJson works for any posted value; TryGetWebMessageAsString
                    // throws for non-string (object) messages.
                    string? msg = null;
                    try { msg = e.WebMessageAsJson; }
                    catch (Exception ex) { ShellLog.Write("webmsg AsJson failed: " + ex.Message); }
                    ShellLog.Write("webmsg: " + (msg ?? "(null)"));
                    if (msg is "retry" or "\"retry\"") { _ = InitAsync(); return; }
                    try
                    {
                        using var doc = JsonDocument.Parse(msg ?? "");
                        var type = doc.RootElement.GetProperty("type").GetString();
                        if (type == "openSettings") OpenSettings();
                        else if (type == "refresh") { _stats.RefreshUsage(); _ = _stats.RefreshLimitsAsync(); }
                    }
                    catch (Exception ex) { ShellLog.Write("webmsg dispatch failed: " + ex.Message); }
                };
                // External links go to the system browser, not inside the box.
                _web.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
                };
                await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(OverlayScript.Source);
                _web.CoreWebView2.NavigationCompleted += (_, _) => PushStats();
                _webReady = true;
            }

            _web.CoreWebView2.NavigateToString(StatusHtml("正在启动 kimi web …"));
            var ok = await _server.EnsureRunningAsync(CancellationToken.None);
            if (!ok)
            {
                _web.CoreWebView2.NavigateToString(ErrorHtml(_server.LastError ?? "未知错误"));
                return;
            }

            var token = _server.ReadToken();
            var url = $"http://127.0.0.1:{_server.Port}/" + (string.IsNullOrEmpty(token) ? "" : "#token=" + token);
            _web.CoreWebView2.Navigate(url);
            _stats.LocalPort = _server.Port ?? 0;
            _stats.LocalToken = token;
            _ = _stats.RefreshLimitsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "KimiWebBox 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnStatsUpdated(StatsSnapshot s)
    {
        if (IsDisposed) return;
        try { BeginInvoke(new Action(() => { UpdateTrayTooltip(s); PushStats(); })); } catch { }
    }

    private void UpdateTrayTooltip(StatsSnapshot s)
    {
        var text = $"KimiWebBox · 今日 {FmtTokens(s.Today)}";
        var session = s.Windows.FirstOrDefault(w => w.Kind == "session");
        if (s.LimitsStatus == "ok" && session != null)
            text += $" · 5h {Math.Round(session.RemainingPercent)}%";
        _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    private void PushStats()
    {
        if (!_webReady || _web.CoreWebView2 == null) return;
        var s = _stats.Current;
        var payload = new
        {
            today = s.Today,
            week = s.Week,
            month = s.Month,
            allTime = s.AllTime,
            limitsStatus = s.LimitsStatus,
            updatedAt = s.UpdatedAt,
            windows = s.Windows.Select(w => new
            {
                kind = w.Kind,
                label = w.Label,
                remainingPercent = w.RemainingPercent,
                resetsAt = w.ResetsAt,
                detail = w.Detail,
            }),
            daily = s.Daily.Select(kv => new { date = kv.Key, tokens = kv.Value }),
            modelsToday = s.ModelsToday.Select(kv => new { id = kv.Key, tokens = kv.Value }).OrderByDescending(m => m.tokens),
            modelsMonth = s.ModelsMonth.Select(kv => new { id = kv.Key, tokens = kv.Value }).OrderByDescending(m => m.tokens),
        };
        var json = JsonSerializer.Serialize(payload);
        _ = _web.CoreWebView2.ExecuteScriptAsync($"window.KimiQuota && window.KimiQuota.update({json})");
    }

    private void OpenSettings()
    {
        try
        {
            ShellLog.Write("OpenSettings enter, Visible=" + Visible);
            using var dlg = new SettingsForm(_config) { TopMost = true, StartPosition = FormStartPosition.CenterScreen };
            var result = Visible ? dlg.ShowDialog(this) : dlg.ShowDialog();
            ShellLog.Write("OpenSettings result=" + result);
            if (result == DialogResult.OK) _ = _stats.RefreshLimitsAsync();
        }
        catch (Exception ex)
        {
            ShellLog.Write("OpenSettings failed: " + ex);
            MessageBox.Show(this, ex.ToString(), "打开设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string FmtTokens(long v) => v.ToString("N0");

    private static string StatusHtml(string text) => $$$"""
        <!doctype html><html><head><meta charset="utf-8"><style>
        body{background:#1b1b1f;color:#cfcfd6;font-family:'Segoe UI',sans-serif;margin:0;height:100vh;
             display:flex;align-items:center;justify-content:center;flex-direction:column;gap:12px}
        .spin{width:22px;height:22px;border:3px solid #444;border-top-color:#aaa;border-radius:50%;
              animation:r 1s linear infinite}@keyframes r{to{transform:rotate(360deg)}}
        </style></head><body><div class="spin"></div><div>{{{text}}}</div></body></html>
        """;

    private static string ErrorHtml(string message) => $$$"""
        <!doctype html><html><head><meta charset="utf-8"><style>
        body{background:#1b1b1f;color:#cfcfd6;font-family:'Segoe UI',sans-serif;margin:0;height:100vh;
             display:flex;align-items:center;justify-content:center;flex-direction:column;gap:16px}
        .msg{max-width:70%;color:#e0a0a0;word-break:break-all}
        button{background:#2d6cdf;color:#fff;border:0;padding:8px 22px;border-radius:6px;
               font-size:14px;cursor:pointer}button:hover{background:#3b78e7}
        </style></head><body><div>kimi web 启动失败</div><div class="msg">{{{message}}}</div>
        <button onclick="chrome.webview.postMessage('retry')">重试</button></body></html>
        """;

    private static bool GetAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) != null;
        }
        catch { return false; }
    }

    private static void SetAutostart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;
            if (enabled) key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\" --tray");
            else key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch { }
    }

    private static Icon AppIcon()
    {
        if (_icon != null) return _icon;
        try { _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application; }
        catch { _icon = SystemIcons.Application; }
        return _icon;
    }
}
