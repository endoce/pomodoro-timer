# Windows 11 25H2 x64 人工验收

没有实际操作及证据的行一律标记“未测试”。不要用Hosted Runner的结果替代本表。

## 环境记录

| 字段 | 实际值（由测试者填写） |
|---|---|
| 测试日期/时区 | 未填写 |
| OS | Windows 11，版本类型Home/Pro/Enterprise等另填 |
| DisplayVersion | 必须25H2 |
| OS Build | 必须完整26200.x |
| OS/进程架构 | x64/x64 |
| 权限 | 普通用户，未提升 |
| 应用版本/Commit SHA | 见BUILD-INFO，另记录下载资产 |
| ZIP/Setup SHA256 | 与SHA256SUMS比较 |
| .NET共享Runtime/开发SDK | 记录是否存在；要求无开发工具测试 |
| Windows App SDK共享Runtime | 记录是否存在，不能只记录SDK包名 |
| VC++ Redistributable | 版本、架构或未安装 |
| App-local VC版本/加载路径 | 见VC清单并核对实际Modules |
| 显示器/缩放 | 型号、100/125/150/200%，是否混合DPI |
| 主题/高对比度/减少动画 | 填写 |
| 勿扰/通知设置/节能设置 | 填写 |
| 日志位置 | %LOCALAPPDATA%\PomodoroTimerSpike\logs |

可在PowerShell查询目标OS，不使用已淘汰的WMIC：

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
  Select-Object DisplayVersion, CurrentBuildNumber, UBR, EditionID
[Runtime.InteropServices.RuntimeInformation]::OSArchitecture
Get-Process PomodoroTimerSpike -ErrorAction SilentlyContinue |
  Select-Object Id, Path
```

## 下载与便携版

1. 仓库Actions → WPF risk validation → spike分支对应**成功运行**。
2. 下载 `PomodoroTimer-WPF-Spike-Windows-x64` Artifact（30天，可能要求GitHub登录）。
3. 解压Artifact外层ZIP，取得内层应用ZIP、Setup、校验和及构建/测试报告。
4. 在该目录执行：

```powershell
Get-FileHash .\PomodoroTimer-WPF-Spike-Windows-x64.zip -Algorithm SHA256
Get-FileHash .\PomodoroTimer-WPF-Spike-Setup.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

逐项比较，不匹配就停止。哈希确认完整性，不独立证明发布者身份。预览包未签名，SmartScreen或企业策略可能阻止；不要关闭安全功能来宣称成功。

5. 将应用ZIP完整解压到普通用户可访问且包含中文/空格的测试路径。
6. 运行 `PomodoroTimerSpike\PomodoroTimerSpike.exe`，不从压缩包内部运行、不以管理员启动。
7. 对照窗口诊断信息确认Build、权限、版本和PID；启动失败记录错误与日志，不补装SDK掩盖缺依赖。

## 桌面用例

| ID | 步骤 | 预期 | 实际/证据 |
|---|---|---|---|
| W01 | 启动便携版 | 中文窗口，指定标题，30秒，Segoe UI Variable，普通权限 | 未测试 |
| W02 | 100/125/150/200%缩放及窗口缩小 | 文字清晰、操作不裁切，可滚动 | 未测试 |
| W03 | 混合DPI双屏拖动、断开显示器 | 窗口可见、无错误大小/崩溃 | 未测试 |
| W04 | 运行中切浅色/深色系统主题 | 动态资源更新，无需重启 | 未测试 |
| W05 | 打开高对比度 | 系统高对比度颜色、可读文字/焦点 | 未测试 |
| W06 | 关闭动画效果 | 无持续动画，不影响计时 | 未测试 |
| T01 | 开始并观察30秒 | 不立即扣秒，到期一次；记录外部计时误差 | 未测试 |
| T02 | 暂停10秒再继续、重复操作 | 暂停不计时，不因恢复扣秒 | 未测试 |
| T03 | X关闭、等待倒计时结束 | 窗口隐藏、托盘存在、后台结束并提交通知 | 未测试 |
| R01 | 启动检查图标、右键菜单 | 图标可见，Open/Pause/Resume/Exit | 未测试 |
| R02 | 双击/重复Open | 恢复同一窗口、PID不变 | 未测试 |
| R03 | 隐藏时从托盘暂停/继续 | 与窗口相同计时状态 | 未测试 |
| R04 | 第二次启动同一EXE | 原窗口激活，无第二个长期实例/计时器 | 未测试 |
| R05 | 托盘Exit，查进程及日志 | 进程完全退出，图标资源释放，Unregister记录 | 未测试 |
| N01 | 显示窗口时发送测试通知 | 横幅/通知中心；分别记录提交与显示 | 未测试 |
| N02 | 隐藏时等待计时完成通知 | 原生通知显示；窗口不被强制弹出 | 未测试 |
| N03 | 隐藏时点击通知 | **原有PID**窗口恢复，NotificationInvoked有记录 | 未测试 |
| N04 | 退出后点击尚存通知 | 若冷启动，记录新PID，标记冷启动而非原实例激活 | 未测试 |
| N05 | 开启勿扰后发送 | 不绕过勿扰；检查通知中心并单独记录 | 未测试 |
| N06 | 系统禁用本应用通知再发送 | 不崩溃，有失败/禁用诊断，计时继续 | 未测试 |
| P01 | 计时中睡眠/恢复 | 挂起暂停，恢复等待手动继续；记录漏失/延迟事件 | 未测试 |
| P02 | Modern Standby/节能（硬件支持时） | 不误报多次完成；如不可验证标未测试 | 未测试 |
| P03 | 锁屏、系统时间调整 | 单调时钟不随墙钟变化，记录实际行为 | 未测试 |

## 安装与卸载

先从托盘退出并清理便携版注册，避免混淆两份可执行文件身份：

```powershell
& '.\PomodoroTimerSpike\PomodoroTimerSpike.exe' --unregister-notifications
$LASTEXITCODE
```

WPF程序是GUI应用；需要可靠等待退出码时使用 `Start-Process -Wait -PassThru`。

| ID | 步骤 | 预期 | 实际/证据 |
|---|---|---|---|
| I01 | 普通用户运行Setup | 不请求管理员，安装到LocalAppData/Programs/PomodoroTimerSpike | 未测试 |
| I02 | Start菜单启动安装版 | 无开发环境可运行，诊断正确 | 未测试 |
| I03 | 对安装版重跑N01～N06/R01～R05 | 分别确认显示、激活、单实例、退出 | 未测试 |
| I04 | 关闭应用后“已安装的应用”卸载 | SDK UnregisterAll成功后删除应用与快捷方式 | 未测试 |
| I05 | 应用运行时尝试卸载 | 清理拒绝，提示先退出；不能悄悄删除正在运行的程序 | 未测试 |
| I06 | 检查通知中心/通知设置/激活登记 | 记录是否仍有残留或无法执行的旧通知 | 未测试 |
| I07 | 检查安装目录、开始菜单、用户日志目录 | 程序/快捷方式清理；诊断日志按设计保留，明确列出 | 未测试 |
| I08 | 干净25H2 VM未安装共享.NET/SDK/VC时运行 | 同一包成功；记录实际加载App-local DLL路径 | 未测试 |
| I09 | 安全测试副本缺VC DLL负例 | 记录真实错误；若共享Runtime兜底则负例不成立 | 未测试 |

正常 `Unregister()`不永久删除COM登记；卸载/删除ZIP使用 `UnregisterAll()`。不能要求普通退出后所有通知登记消失而同时又声称测试了退出后冷启动。

卸载默认保留 `%LOCALAPPDATA%\PomodoroTimerSpike\logs` 用于验收诊断。不自动删除可能属于测试者的日志；如要清理，应先归档证据，再仅删除该PoC自己的目录。

注册残留检查只读观察SDK产生的登记/通知设置，不手工删除COM键作为验收通过的手段。附截图、日志时间段、PID、退出码与路径证据；若操作失败，该行标失败而非换环境后覆盖结果。

## 分开给出结论

| 层级 | 当前状态 |
|---|---|
| 云端编译/发布 | 必须根据实际Actions运行填写，不能预填通过 |
| 自动化逻辑测试 | 见LOGIC-TESTS与TEST-SUMMARY |
| 普通用户本地启动 | 未测试 |
| 桌面托盘/通知/原实例激活 | 未测试 |
| 安装卸载/VC干净环境 | 未测试 |

完成后将本表和证据交给评审。此阶段结束后停止，不开发正式产品、不进入第三阶段。
