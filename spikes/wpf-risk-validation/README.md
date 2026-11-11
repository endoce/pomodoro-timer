# WPF 技术风险验证

这是 Pomodoro Timer 2.0 的独立 PoC，不是正式产品。唯一桌面验收平台为 **Windows 11 25H2 x64，OS Build 26200.x**。SQLite、历史、周期和正式产品页面均未引入。

## 固定版本

| 项目 | 基线 |
|---|---|
| .NET SDK | 10.0.403，`global.json` 禁止 roll-forward / prerelease |
| .NET 运行时 | 此 SDK 对应10.0.14；发布目录另行审计 |
| Windows SDK servicing | 10.0.26100.9457 |
| Windows SDK BuildTools NuGet | 10.0.26100.9457，精确引用 |
| Windows 平台API版本 | 10.0.26100.0 |
| Windows managed reference package | 10.0.26100.57；独立于 SDK 安装包版本 |
| TFM | net10.0-windows10.0.26100.0 |
| Windows App SDK | Microsoft.WindowsAppSDK 2.6.5 Stable |
| RID | win-x64 |
| 安装器编译器 | Inno Setup 6.7.4，固定 SHA256 与签名校验 |

2026-11-10查阅官方资料时，Windows App SDK / 26100系列SDK没有比指定基线更新的稳定版本；.NET 10当前稳定SDK为10.0.403。TFM中的 `windows10.0` 不是产品支持范围。所有实际NuGet依赖见各项目提交的 `packages.lock.json`，没有浮动直接依赖。

## 结构

```text
App/                 WPF应用及Windows原生适配
  Views/             中文窗口、布局
  ViewModels/        通知状态、诊断、计时显示和命令
  Services/          NotifyIcon、AppNotificationManager、系统主题
  Models/            OS Build、权限和版本诊断
  Infrastructure/    RelayCommand、本地日志
Core/
  Models/            计时状态、通知请求
  Services/          单调时钟计时、托盘业务操作、通知生命周期
Tests/               14项无第三方测试框架的可执行逻辑测试
scripts/             SDK/安装器准备、VC App-local、发布与产物检查
installer/           用户级 Inno Setup
docs/                桌面验收与VC依赖说明
```

## 行为

- 开始30秒计时；暂停/继续保留毫秒，不使用每秒减一作为时间真值。
- 关闭窗口或“隐藏到托盘”仅Hide，DispatcherTimer仍唤醒独立计时模型。
- 托盘提供打开、暂停/继续、退出，双击打开同一窗口。
- 通知事件在SDK `Register()`前订阅，API异常隔离，UI激活通过Dispatcher。
- SDK AppInstance重定向重复启动，无手写COM注册。
- 通知提交成功 **不等于** 通知横幅显示成功。
- 退出调用 `Unregister()`；这只是撤销当前进程接收事件，**不等于永久删除注册**。退出后的旧通知可能冷启动新进程。
- 卸载执行 `--unregister-notifications`，用SDK `UnregisterAll()`永久清理；应用仍在运行时拒绝清理。ZIP搬迁/删除前也应退出后执行该命令。
- 系统主题/高对比度通过动态资源响应；没有动画，不使用实验性WPF ThemeMode API。
- 休眠挂起事件暂停计时，恢复后手动继续；Modern Standby与漏失事件必须实机验证。
- 日志：`%LOCALAPPDATA%\PomodoroTimerSpike\logs`。不写EXE目录，不上传日志。

## 构建与测试

在本目录执行。完整Windows构建需要Windows工具，不应在Linux通过关闭自包含来声称发布验证完成。

```powershell
dotnet --version
dotnet restore Tests/PomodoroTimer.Spike.Tests.csproj --locked-mode
dotnet run --project Tests/PomodoroTimer.Spike.Tests.csproj -c Release --no-restore
dotnet restore App/PomodoroTimer.Spike.csproj --locked-mode
dotnet build App/PomodoroTimer.Spike.csproj -c Release --no-restore
dotnet publish App/PomodoroTimer.Spike.csproj -c Release --no-restore -o artifacts/publish
```

CI使用 `.github/workflows/wpf-spike.yml`，仅spike分支push和workflow_dispatch触发，Windows Server 2025 `windows-2025-vs2026`构建。固定Action SHA、读取权限、30天Artifact保留。它不是Windows 11桌面验收。

CI增加签名微软VC App-local DLL、审计PE导入、运行发布EXE的 `--self-test`（仅WPF XAML/原生通知构建，不显示窗口/通知、不注册），构建ZIP和用户级Setup，计算与再次验证SHA256。

应用项目固定 `RuntimeIdentifier=win-x64`、`.NET SelfContained=true` 和 `WindowsAppSDKSelfContained=true`，发布命令不向Core库传播全局RID。安装器正常流程使用PoC自有中文翻译，未覆盖的底层错误继承Inno官方默认文本；不依赖编译器不存在的ChineseSimplified语言文件。

Artifact：`PomodoroTimer-WPF-Spike-Windows-x64`。

- `PomodoroTimer-WPF-Spike-Windows-x64.zip`
- `PomodoroTimer-WPF-Spike-Setup.exe`
- `SHA256SUMS.txt`
- `BUILD-INFO.txt`
- `TEST-SUMMARY.txt`
- VC来源、PE依赖和发布清单

完整解压ZIP后运行 `PomodoroTimerSpike\PomodoroTimerSpike.exe`。普通用户级安装路径为 `%LOCALAPPDATA%\Programs\PomodoroTimerSpike`。两个版本不要同时运行或交叉测试通知身份。

**当前已执行证据与未测试项目见 [STATUS.md](docs/STATUS.md)。桌面验收按 [DESKTOP-ACCEPTANCE.md](docs/DESKTOP-ACCEPTANCE.md)，VC部署说明见 [VC-RUNTIME.md](docs/VC-RUNTIME.md)。** 不将源码存在、成功还原或逻辑测试通过标为桌面通过。

## 官方资料

- [固定.NET 10发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
- [Windows SDK](https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads)
- [Windows App SDK稳定与生命周期](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)
- [WPF原生通知与注册顺序](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet)
- [自包含Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [AppInstance](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing)
- [Runner工具与OS](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-VS2026-Readme.md)

NuGet主包会引入WinUI/WebView2/AI等传递包，本PoC不调用这些功能；不将它们的存在误写成应用需要用户使用AI或安装浏览器。是否能在后续只引用必要模块，属于另行审核的优化，不在本阶段偷偷替换用户指定主包。分发须遵守依赖、Visual Studio Redist及安装器许可；本PoC不替仓库决定开源许可证。
