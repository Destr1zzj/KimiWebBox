using KimiWebBox.Usage;

namespace KimiWebBox;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _cookieBox;
    private readonly TextBox _apiKeyBox;

    public SettingsForm(AppConfig config)
    {
        Text = "额度设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Width = 560;
        Height = 300;

        var hint = new Label
        {
            Left = 16, Top = 12, Width = 510, Height = 56,
            Text = "无需任何配置即可自动读取 5 小时 + 每周额度（来自本机 Kimi Code 登录态）。\n解锁每月额度：浏览器登录 kimi.com → F12 → 控制台(Console) 输入 localStorage.getItem('access_token') → 复制结果粘贴到下方。（kimi-auth cookie 已下线）",
        };
        var cookieLabel = new Label { Left = 16, Top = 76, Width = 140, Text = "kimi.com access_token" };
        _cookieBox = new TextBox
        {
            Left = 160, Top = 72, Width = 366,
            UseSystemPasswordChar = true,
            Text = config.KimiAuthToken,
        };
        var keyLabel = new Label { Left = 16, Top = 110, Width = 140, Text = "Kimi Code API Key" };
        _apiKeyBox = new TextBox
        {
            Left = 160, Top = 106, Width = 366,
            UseSystemPasswordChar = true,
            Text = config.KimiCodeApiKey,
        };
        var note = new Label
        {
            Left = 16, Top = 140, Width = 510, Height = 34,
            ForeColor = Color.Gray,
            Text = "凭据只保存在本机 config.local.json（与 exe 同目录），不会上传。留空则只显示本地 token 用量。",
        };
        var save = new Button { Left = 346, Top = 186, Width = 86, Text = "保存", DialogResult = DialogResult.OK };
        var cancel = new Button { Left = 440, Top = 186, Width = 86, Text = "取消", DialogResult = DialogResult.Cancel };

        save.Click += (_, _) =>
        {
            config.KimiAuthToken = LimitsClient.NormalizeWebToken(_cookieBox.Text);
            config.KimiCodeApiKey = _apiKeyBox.Text.Trim();
            config.Save();
        };

        Controls.AddRange(new Control[] { hint, cookieLabel, _cookieBox, keyLabel, _apiKeyBox, note, save, cancel });
        AcceptButton = save;
        CancelButton = cancel;
    }
}
