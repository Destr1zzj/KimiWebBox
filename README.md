# KimiWebBox

Kimi Code CLI `kimi web` 的 Windows 桌面包装器——双击即用，不再需要打开并保持 PowerShell。

> 非官方个人工具，与 Moonshot AI / Kimi 官方无关。

## 它解决什么问题

Kimi Code CLI 自带 `kimi web`（本地 Web UI），但每次使用都要开一个终端并保持开着。KimiWebBox 把它包成一个普通桌面应用：

- **双击启动**：自动探测已在运行的 kimi web；没有则以完全隐藏的方式拉起，全程无控制台窗口
- **关窗进托盘**：服务在后台常驻，下次打开秒开
- **单实例**：重复启动只唤起已有窗口，不会起第二个服务
- **托盘菜单**：打开 / 重启服务 / 额度设置 / 开机自启 / 退出并停止服务
- **会话保持**：WebView2 使用独立数据目录（`%APPDATA%\KimiWebBox\webview2`），登录状态不丢
- **用量与额度面板**：窗口右下角悬浮 chip，点开显示 今日/近7天/本月/累计 token 用量，以及 5 小时/每周/每月额度条（5 小时 + 每周零配置自动可读，见下）；深色/浅色自适应。托盘 tooltip 同步显示今日用量与 5 小时剩余

## 用量与额度

- **Token 用量**：只读扫描本机会话文件（`~/.kimi-code/sessions` 与 `~/.kimi/sessions`)，无需任何凭据
- **额度**：默认**无需任何凭据**——自动走本地 kimi web 的 OAuth 额度接口（使用本机 Kimi Code 登录态），可见 5 小时 + 每周：
  - 配置 kimi.com 的 `access_token`（托盘右键 → 额度设置；浏览器登录 kimi.com → F12 → 控制台执行 `localStorage.getItem('access_token')` 复制）可额外解锁每月额度
  - 或 Kimi Code API Key 作为降级源（仅 5 小时 + 每周）
  - 凭据只保存在本机 `config.local.json`（与 exe 同目录），不上传

用量与额度的数据层实现参考自 [KimiTokenMonitor](https://github.com/Destr1zzj/KimiTokenMonitor)（其接口探测逻辑参考自 [token-monitor](https://github.com/Javis603/token-monitor))，均为 MIT 协议，在此致谢。

所有流量只走本机回环（`127.0.0.1`)，额度消耗的是你本机 Kimi Code CLI 的账号。

## 运行要求

- Windows 10 / 11
- .NET 8 桌面运行时（运行编译产物时）；.NET 8 SDK（自行构建时）
- WebView2 运行时（Windows 11 一般已内置）
- 已安装 [Kimi Code CLI](https://www.kimi.com/code/) 并完成登录（默认读取 `%USERPROFILE%\.kimi-code\bin\kimi.exe`)

## 使用

1. 构建或取得 `KimiWebBox.exe`，放到任意位置
2. 双击启动；首次会静默拉起 `kimi web`（默认 `127.0.0.1:58627`，端口被占自动顺延，探测范围 58627–58637)
3. 需要开机常驻：托盘右键 → 开机自启（写入 `HKCU\...\Run`，参数 `--tray`)

日志位于 `%APPDATA%\KimiWebBox\logs\`（仅当服务由本应用拉起时记录）。

## 构建

```powershell
cd KimiWebBox
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../dist
```

重新生成应用图标（多分辨率 `.ico`):

```powershell
cd IcoGen
dotnet run -- ../KimiWebBox/app.ico
```

## 工作原理

1. 启动时探测 58627–58637 端口，找到在跑的 kimi web 就直接接入（不会重复起服务）
2. 没找到则以 `CreateNoWindow` 方式 spawn `kimi web --no-open`，等待端口就绪（30 秒超时，失败给出错误页与重试）
3. 读取 `%USERPROFILE%\.kimi-code\server.token`,WebView2 加载 `http://127.0.0.1:<port>/#token=<token>`
4. 关闭主窗口时只隐藏到托盘；从托盘菜单「退出并停止服务」才会终止由本应用拉起的服务进程

## macOS 版

`macos/` 目录提供零构建的 Mac 版（`.app` 包装，无需编译）:

```bash
cd macos
./install.sh            # 生成图标并安装到 ~/Applications
./install.sh --login    # 同时注册登录项（开机自启）
```

原理与 Windows 版一致：探测/拉起 `kimi web` 后，用 Chrome（或 Edge）的 `--app=` 模式打开，窗口无浏览器元素。要求：macOS 12+、已安装并登录 Kimi Code CLI、Chrome 或 Edge（都没有则退回默认浏览器）。

注意：Mac 版 v1 不含 Windows 版的悬浮用量面板（那层依赖 WebView2 注入）。

## License

[MIT](LICENSE)
