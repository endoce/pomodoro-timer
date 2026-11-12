# WPF 技术风险验证

这是 Pomodoro Timer 2.0 的独立 PoC，不是正式产品。唯一桌面验收平台为 **Windows 11 25H2 x64，OS Build 26200.x**。SQLite、历史、周期和正式产品页面均未引入。

## Portable 第一阶段（工作区待审核）

已加入 EXE 根目录下的 `Data/`、`Logs/`、原子 JSON 存储和重启暂停恢复。实现、异常策略及本轮测试边界见 [PORTABLE-PHASE-1.md](docs/PORTABLE-PHASE-1.md)。三项审查修复后，本轮 84 项逻辑/基础设施测试不代表 Windows 桌面通过。

最终产品方向为 **Portable Only**，唯一应用 ZIP 为 `PomodoroTimer-Portable-Windows-x64.zip`。本轮只改发布流水线、脚本和文档，取消安装器；Phase 1 业务代码及既有测试保持不变。Windows Runner、真实应用 ZIP 和 25H2 桌面均尚未验证。详见 [PORTABLE-PIPELINE.md](docs/PORTABLE-PIPELINE.md)。

产品ZIP的 `README.md` 使用独立 [Portable 用户手册](docs/PORTABLE-USER-GUIDE.md)，不复制本开发README或内部验收文档。正式测试解析按实际宿主平台执行：Linux不得SKIP，Windows仅允许两项名称/原因精确匹配的Unix权限SKIP；原始报告及汇总保留SKIP和Windows ACL `NOT TESTED`。

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

2026-11-10查阅官方资料时，Windows App SDK / 26100系列SDK没有比指定基线更新的稳定版本；.NET 10当前稳定SDK为10.0.403。TFM中的 `windows10.0` 不是产品支持范围。所有实际NuGet依赖见各项目提交的 `packages.lock.json`，没有浮动直接依赖。

## 结构

```text
App/                 WPF应用及Windows原生适配
  Views/             中文窗口、布局
  ViewModels/        通知状态、诊断、计时显示和命令
  Services/          NotifyIcon、AppNotificationManager、系统主题
  Models/            OS Build、权限和版本诊断
  Infrastructure/    RelayCommand、Portable路径、原子JSON、正式日志
Core/
  Models/            计时状态、快照、设置schema、通知请求
  Services/          单调时钟计时、托盘业务操作、通知生命周期
Tests/               84项无第三方测试框架的可执行逻辑/基础设施测试
scripts/             固定Windows SDK、VC App-local、Portable打包/完整校验/校验器负例测试
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
- 不提供安装/卸载程序。搬迁/删除前先退出，按桌面验收文档执行既有 `--unregister-notifications` 命令；应用仍在运行时拒绝清理。此 SDK 永久清理及搬迁后通知身份必须实测。
- 系统主题/高对比度通过动态资源响应；没有动画，不使用实验性WPF ThemeMode API。
- 休眠挂起事件暂停计时，恢复后手动继续；Modern Standby与漏失事件必须实机验证。
- 数据与日志：`AppContext.BaseDirectory` 下的 `Data/`、`Logs/`。不回退到 AppData，不上传日志；Data 不可写会明确报错，Logs 失败不阻止计时。历史日志不自动迁移。

## 构建与测试

在本目录执行。完整Windows构建需要Windows工具，不应在Linux通过关闭自包含来声称发布验证完成。

```powershell
dotnet --version
dotnet restore Tests/PomodoroTimer.Spike.Tests.csproj --locked-mode
dotnet run --project Tests/PomodoroTimer.Spike.Tests.csproj -c Release --no-restore
dotnet restore App/PomodoroTimer.Spike.csproj --locked-mode
dotnet build App/PomodoroTimer.Spike.csproj -c Release --no-restore
dotnet publish App/PomodoroTimer.Spike.csproj -c Release --no-restore -o artifacts/publish
Rename-Item artifacts/publish/PomodoroTimerSpike.exe -NewName PomodoroTimer.exe
```

CI使用 `.github/workflows/wpf-spike.yml`，仅spike分支push和workflow_dispatch触发，Windows Server 2025 `windows-2025-vs2026`构建。固定Action SHA、读取权限、30天Artifact保留。它不是Windows 11桌面验收。

CI增加签名微软VC App-local DLL、审计PE导入、对重命名后的 `PomodoroTimer.exe` 运行 `--self-test`（仅WPF XAML/原生通知构建，不显示窗口/通知、不注册），生成真实测试汇总、BUILD-INFO、ZIP 内全文件哈希，然后打包并校验内外 SHA256。任何步骤失败都不上传正式 Artifact；失败诊断保留在 Actions 日志中，没有第二个 Artifact。

应用项目固定 `RuntimeIdentifier=win-x64`、`.NET SelfContained=true` 和 `WindowsAppSDKSelfContained=true`，发布命令不向 Core 库传播全局 RID 或 AssemblyName。只重命名发布目录里的 apphost，内部 `PomodoroTimerSpike.dll/.deps.json/.runtimeconfig.json` 名称保持不变。

唯一 Artifact：`PomodoroTimer-Portable-Windows-x64`。

- `PomodoroTimer-Portable-Windows-x64.zip`
- `SHA256SUMS.txt`
- `BUILD-INFO.txt`
- `TEST-SUMMARY.txt`
- VC来源、PE依赖和发布清单
- 原始 LOGIC/PUBLISH/INITIALIZATION 报告、部署属性、固定 SDK 报告及独立 ARTIFACT/PIPELINE-SCRIPT 校验报告

完整解压 ZIP 后运行 `PomodoroTimer\PomodoroTimer.exe`，不直接在压缩包内运行。首次正常启动自动在程序目录创建 `Data/`、`Logs/`；没有 AppData 回退。应用退出后可以搬迁整个目录（含 Data/Logs），相关注册/单实例/恢复行为仍待 25H2 实机验证。ZIP 不包含用户数据、调试符号、源码、测试或开发工具；完整保留发布运行依赖。

**当前已执行证据与未测试项目见 [STATUS.md](docs/STATUS.md)。桌面验收按 [DESKTOP-ACCEPTANCE.md](docs/DESKTOP-ACCEPTANCE.md)，VC部署说明见 [VC-RUNTIME.md](docs/VC-RUNTIME.md)。** 不将源码存在、成功还原或逻辑测试通过标为桌面通过。

## 官方资料

- [固定.NET 10发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
- [Windows SDK](https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads)
- [Windows App SDK稳定与生命周期](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)
- [WPF原生通知与注册顺序](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet)
- [自包含Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [AppInstance](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing)
- [Runner工具与OS](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-VS2026-Readme.md)

NuGet主包会引入WinUI/WebView2/AI等传递包，本PoC不调用这些功能；不将它们的存在误写成应用需要用户使用AI或安装浏览器。是否能在后续只引用必要模块，属于另行审核的优化，不在本阶段偷偷替换用户指定主包。分发须遵守依赖及 Visual Studio Redist 许可；本PoC不替仓库决定开源许可证。
