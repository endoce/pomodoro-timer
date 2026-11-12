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
| Portable ZIP SHA256 | 与外部SHA256SUMS比较，再校验解压后的内部逐文件SHA256 |
| .NET共享Runtime/开发SDK | 记录是否存在；要求无开发工具测试 |
| Windows App SDK共享Runtime | 记录是否存在，不能只记录SDK包名 |
| VC++ Redistributable | 版本、架构或未安装 |
| App-local VC版本/加载路径 | 见VC清单并核对实际Modules |
| 显示器/缩放 | 型号、100/125/150/200%，是否混合DPI |
| 主题/高对比度/减少动画 | 填写 |
| 勿扰/通知设置/节能设置 | 填写 |
| 数据/日志位置 | EXE目录下Data/、Logs/；不回退AppData |

可在PowerShell查询目标OS，不使用已淘汰的WMIC：

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
  Select-Object DisplayVersion, CurrentBuildNumber, UBR, EditionID
[Runtime.InteropServices.RuntimeInformation]::OSArchitecture
Get-Process PomodoroTimer -ErrorAction SilentlyContinue |
  Select-Object Id, Path
```

## 下载与便携版

1. 仓库Actions → WPF risk validation → spike分支对应**成功运行**。
2. 下载 `PomodoroTimer-Portable-Windows-x64` Artifact（30天，可能要求GitHub登录），核对对应 Commit 和 BUILD-INFO。当前待审核源码未提交/推送，旧远程运行不能验证它。
3. 解压Artifact外层ZIP，取得唯一内层应用ZIP、校验和及构建/测试报告；没有安装程序。
4. 在该目录执行：

```powershell
Get-FileHash .\PomodoroTimer-Portable-Windows-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

逐项比较，不匹配就停止。哈希确认完整性，不独立证明发布者身份。预览包未签名，SmartScreen或企业策略可能阻止；不要关闭安全功能来宣称成功。

5. 将应用ZIP完整解压到普通用户可写且包含中文/空格的测试路径；按内层 SHA256SUMS 逐项核对文件。不得删减 DLL，不补装共享运行时掩盖打包缺陷。
6. 运行 `PomodoroTimer\PomodoroTimer.exe`，不从压缩包内部运行、不以管理员启动。
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

## Portable 数据、搬迁与删除（均未测试）

不再提供安装器，历史安装/卸载用例取消，并不表示这些用例曾通过。先从托盘退出，归档数据与日志，并按需清理旧路径的通知注册，避免混淆两份可执行文件身份：

```powershell
$process = Start-Process '.\PomodoroTimer\PomodoroTimer.exe' -ArgumentList '--unregister-notifications' -Wait -PassThru
$process.ExitCode
```

WPF程序是GUI应用；需要可靠等待退出码时使用 `Start-Process -Wait -PassThru`。

| ID | 步骤 | 预期 | 实际/证据 |
|---|---|---|---|
| D01 | 普通用户首次启动ZIP | 在EXE目录创建Data/Logs，无AppData回退/提权 | 未测试 |
| D02 | 保存设置、计时中退出后重启 | 设置保留；恢复为暂停，不后台继续计时 | 未测试 |
| D03 | 只读目录、拒绝写入ACL | Data明确报错，不能静默写入AppData；记录退出行为 | 未测试 |
| D04 | 退出并搬迁整个目录（含Data/Logs） | 在新路径保留设置/暂停状态；重跑N01～N06/R01～R05 | 未测试 |
| D05 | 旧实例退出后立即重新启动/启动第二副本 | AppInstance所有权交接、最终持久化及单实例行为正确 | 未测试 |
| D06 | 退出后清理注册并归档/删除目录 | SDK清理退出码正确；记录通知中心/设置/激活登记残留 | 未测试 |
| D07 | 应用运行时请求注册清理 | 明确拒绝，先退出；不手动删COM键制造成功 | 未测试 |
| D08 | 干净25H2 VM未安装共享.NET/SDK/VC时运行 | 同一ZIP成功；记录实际加载App-local DLL路径 | 未测试 |
| D09 | 安全测试副本缺VC DLL负例 | 记录真实错误；若共享Runtime兜底则负例不成立 | 未测试 |
| D10 | 安全环境断电/磁盘满/USB拔出、NTFS实际写入 | 记录文件一致性与错误；不能用Linux锁/模拟磁盘替代 | 未测试 |

正常 `Unregister()`不永久删除COM登记；搬迁/删除ZIP前的清理使用既有 `UnregisterAll()`。不能要求普通退出后所有通知登记消失而同时又声称测试了退出后冷启动。上述清理与搬迁行为尚未实测。

删除整个 Portable 目录会同时删除其中 Data/Logs，应先归档。旧PoC曾写入AppData的历史日志不自动迁移或清理；如要手工清理，先归档证据，仅处理本应用自己的文件。

注册残留检查只读观察SDK产生的登记/通知设置，不手工删除COM键作为验收通过的手段。附截图、日志时间段、PID、退出码与路径证据；若操作失败，该行标失败而非换环境后覆盖结果。

## 分开给出结论

| 层级 | 当前状态 |
|---|---|
| 云端编译/发布 | 必须根据实际Actions运行填写，不能预填通过 |
| 自动化逻辑测试 | 见LOGIC-TESTS与TEST-SUMMARY |
| 普通用户本地启动 | 未测试 |
| 桌面托盘/通知/原实例激活 | 未测试 |
| Portable数据/搬迁/VC干净环境 | 未测试 |
| 历史安装/卸载 | 未测试；发布方向取消，不提供安装程序 |

完成后将本表和证据交给评审。此阶段结束后停止，不开发正式产品、不进入第三阶段。
