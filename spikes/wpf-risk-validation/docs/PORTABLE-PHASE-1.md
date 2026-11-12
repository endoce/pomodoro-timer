# Portable 第一阶段：基础设施实施与验证

日期：2026-11-11。基线：`spike/wpf-risk-validation`，`3588efa7118f1d667e47053e08a04e627e0f82dc`。

本轮仅工作区实施，待审核；未 Commit、Push、创建 PR、合并、触发 Actions 或修改远程。最终方向为 Portable Only，本阶段不修改 EXE/ZIP/安装器/CI 交付流程，不进入第二阶段。以前的 PoC 云端结果不能证明本轮新增代码。

## 1. 文件范围

路径均相对 `spikes/wpf-risk-validation/`。

新增：

- `App/Infrastructure/PortablePaths.cs`
- `App/Infrastructure/PersistenceDiagnostic.cs`
- `App/Infrastructure/AtomicJsonStore.cs`
- `App/Infrastructure/ApplicationLog.cs`
- `App/Infrastructure/LogService.cs`
- `App/Infrastructure/StorageTransaction.cs`（审查修复新增）
- `App/Services/SettingsStore.cs`
- `App/Services/TimerStateStore.cs`
- `App/Services/TimerPersistenceSession.cs`
- `App/Services/ExitCoordinator.cs`（审查修复新增）
- `Core/Models/ApplicationSettings.cs`
- `Core/Models/TimerSnapshot.cs`
- `Tests/PortableTests.cs`
- `Tests/PortableRegressionTests.cs`（审查修复新增）
- 本文及 `PORTABLE-PHASE-1-TEST-RESULTS.txt`。

修改：

- `App/ViewModels/MainViewModel.cs`：初始化路径/设置/状态，复用现有诊断显示区域；审查修复移除 Exit 订阅，改为显式 CompletePersistence。
- `App/App.xaml.cs`：本次授权仅新增退出协调器字段及最小 OnExit 接入，先完成最终 checkpoint 再清理/释放 Key；普通启动代码不改。
- `Core/Services/TestTimer.cs`：经单独授权，仅增加快照、参数校验、恢复 API。
- `Tests/Program.cs`、`Tests/PomodoroTimer.Spike.Tests.csproj`：保留原 14 项与初次 Phase 1 的 37 项，再增加审查回归 33 项；链接实际服务源码。
- `README.md`：当前路径/测试数量与历史交付说明边界。

删除 `App/Infrastructure/SpikeLog.cs` 的旧 AppData 日志实现，正式实现改为 `ApplicationLog`/`LogService`。全局源码别名 `SpikeLog = ApplicationLog` 仅兼容本阶段禁止修改的现有调用者；没有保留旧日志类、AppData 写入或静默吞错实现。

未新增 NuGet 依赖，锁文件不需变化。通知后端、NotificationSession、托盘、单实例机制、Inno Setup、Workflow 和 ZIP 脚本不改；App.xaml.cs 仅有本次授权退出片段及协调器字段变化。

## 2. Portable 路径

正式代码只使用 `PortablePaths.Default`，根目录固定为 `AppContext.BaseDirectory`。构造函数允许注入绝对测试根目录；不接受相对目录，不使用 CurrentDirectory。文件名/数据目录集中计算，中文、空格及文化设置不会改变位置。

```text
<EXE 所在目录>/
  Data/
    settings.json
    settings.json.bak       # 第二次有效保存后出现
    state.json
    state.json.bak
  Logs/
    pomodoro-yyyy-MM-dd-0000.log
```

首次正常 UI 启动通过 ViewModel 调用 Initialize，创建 Data/Logs。写探针使用唯一 CreateNew 文件、WriteThrough、Flush(true)、DeleteOnClose，不接触已有文件。

- Data 不可写：抛出含路径和迁移整个应用目录建议的明确错误，由现有启动错误对话框显示；不提权、不回退其他目录，不覆盖已有数据。
- Logs 不可写：诊断警告并允许计时；写入失败通过 LastFailure、Failed 事件、现有诊断区和 Trace/stderr 回退输出暴露，不另选磁盘目录。
- 不扫描/迁移以前的 `%LOCALAPPDATA%` 日志。
- 本阶段不改变启动顺序，因此通知初始化仍先于 ViewModel 的 Data 检查；不是“在所有 Windows 注册之前检查 Data”的实现。

## 3. 设置持久化

现有 PoC 没有可编辑的时长、主题或通知开关。`ApplicationSettings` 只含 `schemaVersion: 1`，不制造没有业务/UI 的字段，也不宣称新增了用户设置页面。

`SettingsStore` 是独立读取/保存/显式恢复服务。首次成功启动写入默认设置；已有有效文件加载时不反复重写。

共享 `AtomicJsonStore<T>` 策略：

1. 最多 64 KiB JSON、深度 32，校验对象、整数 schemaVersion、必需字段、重复/未知字段及业务参数。
2. 主文件及备份均不存在才返回可保存的默认值。
3. 同目录唯一临时文件 CreateNew，写入后 Flush(true)；已有主文件用 File.Replace 并保留上一份有效主文件为 `.bak`，首次用不覆盖的 File.Move。
4. 失败不采用其他目录；临时文件清理失败有警告。进程突然退出留下的临时文件不被启动读取为有效数据。
5. 主文件损坏/不可读时保留原件；可读取有效备份，但 CanSave=false。无有效备份时以默认值在内存运行并明确诊断，不自动覆盖损坏文件。
6. 高版本主文件不读取旧备份，不允许保存或恢复降级；高版本或不可读/损坏的备份也禁止保存覆盖。即便主文件有效，异常备份也要明确诊断并保持保存禁用，需先归档处理备份。
7. 显式 RestoreBackup 校验备份后，将原主文件逐字节复制到唯一 `.preserved-<GUID>` 并 Flush(true)，再原子恢复；正常 `.bak` 保持不变。
8. 诊断回调异常不能掩盖原始存储错误或打断计时；回退诊断只输出事件/异常类型，不输出任意凭据文本。
9. 审查修复后，Load/Save/Restore 整个读校验、临时写入、提交及读回过程均持有同规范化路径的命名 Mutex；包括首次创建。锁超时不写盘，不直接覆盖、不重试、不回退。
10. 提交器返回后仍需重新读取并校验目标模型；不能仅凭返回报告 Saved。提交后抛异常仍报告失败，不虚构回滚或硬件断电保证。

RestoreBackup 当前是服务 API，不是 UI/CLI 命令。读取备份后开启的计时 session 本次生命周期保持禁止保存；外部显式恢复后需重新启动以重新判定 CanSave。不能通过手工直接覆盖损坏文件来代替此 API。

## 4. 运行状态恢复

当前只有固定 30 秒测试计时器。`TimerSnapshot` 保存 schemaVersion、Ready/Running/Paused/Completed 状态、固定 durationTicks 和 remainingTicks；不虚构工作/休息模式、周期或历史，不保存 Stopwatch 值、启动时间或墙钟时间戳。

- Running/Paused 且剩余时间大于零：恢复为 Paused，保留亚秒精度；关机、停机、睡眠期间的时间不累加。
- 零剩余 Running：恢复为 Completed，不回放 Completed 事件/完成通知。
- Ready/Completed：保持原状态。非法状态、持续时长、剩余范围及状态/剩余组合拒绝采用。
- TimerPersistenceSession 监听 Changed，但仅 TimerState 发生变化才提交；普通 250ms Tick 不写盘。
- 启动把 Running 转为 Paused 时写一次恢复 checkpoint（仅可保存文件）；手工 checkpoint 和 Dispose/正常退出保存最新剩余时间。
- 审查修复后不再订阅 Application.Exit；App.OnExit 同步调用 ViewModel.CompletePersistence → session.Complete，结果确定后才释放单实例 Key。Dispose 委托同一个一次性入口。
- 保存失败有错误诊断，计时/完成事件仍可继续。异常强杀可能恢复上一次状态节点，而不是最新 Tick；这是避免高频落盘的明确取舍。

## 5. 日志

`ApplicationLog` 提供进程级入口，`LogService` 是实际可注入时钟/小容量限制测试的服务。现有字符串调用兼容为 Information / application.event；新持久化诊断使用明确级别、事件及异常。

- 目录仅 Logs；文件名使用 invariant Gregorian UTC 日期与分片序号。
- UTF-8 JSON Lines，每条含 timestampUtc（显式 +00:00）、level、event、message、exception。异常可包含类型/消息/堆栈，经脱敏。
- 禁止调用方传入 API Key/密码/凭据；额外脱敏已标记的 API Key、password、secret、access/refresh token、Bearer/Basic 内容。它不是识别任意未标记秘密的安全边界。
- 每日轮转；默认单文件 5 MiB、同命名日志总量 50 MiB、保留最近 14 个 UTC 日。测试注入更小容量，执行相同真实服务路径；默认值另有断言。
- 按实际编码字节计数，超大条目替换为合法 JSON 省略说明，不把文件写到上限之外。
- 每次写入短暂打开文件，禁止共享删除；清理排除当前写入文件，其他候选必须取得独占访问才 DeleteOnClose。不处理无关文件名。
- 锁定/权限错误的旧文件保留并警告，因此无法安全清理时总量或保留天数可能暂时超限；绝不为强制满足限制而影响正在使用的文件。
- 日志/诊断订阅者失败不使计时器崩溃；LastFailure 保留警告，即便先于 ViewModel 发生也能显示。
- 审查修复采用 NonBacktracking 字段识别及线性转义扫描；引号值中的转义引号、奇偶反斜杠均整体省略。未闭合、悬空反斜杠或紧邻非法后缀时保守省略剩余文本，可能连带省略后续非敏感文本。
- event/message/exception.ToString 使用同一脱敏入口；脱敏/异常序列化失败发生在打开日志文件之前，本条不写盘；回退输出仅固定事件及异常类型。

## 6. 初次 Phase 1 自动化验证（历史记录）

环境：Linux x64，非 root 用户，.NET SDK 10.0.403 / Runtime 10.0.14。临时目录 `TMPDIR=/tmp/opencode`；非默认 PATH 的 SDK 入口及命令见测试记录。

- Core/Tests locked restore 成功；Release 编译启用 warn-as-error，0 警告 / 0 错误。
- 原 14 项 + 新增 37 项 = **51 通过，0 失败，0 跳过**。
- 测试编译 App 实际 PortablePaths、AtomicJsonStore、SettingsStore、TimerStateStore、TimerPersistenceSession、ApplicationLog、LogService 源码，不复制实现，不依赖完整 WPF 工程。
- 覆盖生产路径、中文/空格、CurrentDirectory、目录创建、设置读写/默认/校验、损坏、高 schema、备份及精确保留原件。
- 原子保存测试覆盖提交前故障、提交后报错、首存故障、真实子进程在临时文件 Flush 后/Commit 前 Environment.Exit（绕过 finally），验证主/备份完整且孤儿不被采用。
- 非 root Unix 目录真实去掉写权限，验证 Data 明确失败、Logs 警告而不阻止运行；另用路径被文件占用作跨平台阻塞测试。
- 覆盖重启亚秒暂停/不累加停机时间、状态校验/零剩余、完成不回放、Tick 不写盘、状态节点/退出提交及保存失败隔离。
- 覆盖 UTC 日轮转、14 天保留、分片/总量、超大条目、异常/凭据脱敏、锁定日志及无关文件保护。
- 补充：在 `/tmp/opencode/portable-phase1-managed-diagnostics/` 用真实 WPF managed reference assemblies 编译实际 ViewModel/Infrastructure/持久化服务，仅检查 C# 类型/引用，0 警告 / 0 错误。该临时诊断项目不是产品构建、不生成 XAML、不调用通知 SDK，也不执行 WPF。
- XML/JSON、项目源码引用、修改范围/禁止文件、敏感信息及 diff whitespace 静态检查完成；无新增依赖或锁文件改动。

## 7. 尚未完成的测试

当前 Linux 环境没有执行以下项目，不能声称通过：

- 完整 WPF/App 编译、XAML 编译、Windows App SDK 构建/自包含发布与产物验证。
- Windows 11 25H2 x64 26200.x 实机启动、Data 错误对话框、Logs 警告显示。
- Windows ACL/UAC、NTFS 原子替换/备份及独占句柄删除行为。
- Windows CI 的完整 WPF/XAML 构建及真实 Application.OnExit/Exit 事件验证；Windows 11 25H2 两进程 AppInstance Key 交接、阻塞/失败保存、立即重启状态验证。
- 中文/空格/改变工作目录的实际 EXE 双击启动、搬迁后的读取恢复。
- 真断电、磁盘满、USB拔出、FAT/exFAT/网络盘原子替换与持久性；自动化故障注入不是硬件断电验证。
- 通知、托盘、单实例、睡眠/Modern Standby 和现有 ZIP/安装流程回归。本阶段没有改动这些后端，也没有本轮云端/桌面验证证据。

## 8. 技术风险与取舍

- 同目录原子替换在不支持的文件系统上报错，不降级为非原子覆盖；Flush(true) 不等于所有硬件/文件系统断电保证。
- IO 同步且只在状态/恢复/退出节点执行；慢盘可能影响一次 UI 命令响应，需 Windows/USB 实测。
- 强杀与保存失败可能恢复较旧的剩余时间；仍保持暂停，避免停机时间被计入。
- 损坏数据的 `.preserved-*` 与孤儿临时文件不自动删除；保护用户数据优先，反复人工恢复可能需要人工归档。
- 日志被锁/不可删除时安全清理优先于强制容量；警告明确可见。
- 没有配置编辑 UI/恢复按钮，也没有新增正式工作/休息模式；这些不属于本阶段。
- 原通知路径注册与跨目录单实例风险仍在；不将路径服务实现误写成这些风险已解决。
- 当前 EXE 仍为 PomodoroTimerSpike.exe，历史交付仍含 Setup；唯一正式 Portable ZIP、命名和安装器退出是后续审核范围，本轮未调整。

## 9. Git 状态与审核边界

HEAD/分支保持指定基线；所有交付修改未暂存，新增文件未跟踪。包含上述新增文件在内的工作区 Diff 才是本轮完整内容（普通 git diff --stat 不显示未跟踪文件）。未更改 master、未切换分支、未启动 Subagent，未 Reset/Clean 或丢弃用户修改。

完成后停止等待审核，不执行第二阶段。

## 10. 三项审查修复与再次验证

### 根因及修复位置

| 问题 | 根因 | 实际修复（行号以本轮最终源码为准） |
| --- | --- | --- |
| P1-01 | 原 OnExit 先 UnregisterKey，再 base.OnExit 触发 ViewModel Exit 订阅保存；新主实例可能先取得 Key 并读到旧快照。 | `App/App.xaml.cs:168` 显式接入；`App/ViewModels/MainViewModel.cs:87` CompletePersistence；`App/Services/TimerPersistenceSession.cs:45,70` 返回/缓存结果并退订；`App/Services/ExitCoordinator.cs:21` 一次性同步排序与异常隔离。 |
| P2-01 | 每个 Store 的 gate 只协调自身，两个 Store 的校验与提交可交错；File.Replace 本身不保护整个事务。 | `App/Infrastructure/StorageTransaction.cs:26,34` 规范化路径及命名 Mutex；`AtomicJsonStore.cs:61,96,121,156` 在 Load/Save/Restore 全事务内持锁、提交读回；两个 Store 构造器支持有限测试超时。 |
| P2-02 | 原引号正则在转义引号处提前终止，留下敏感值后缀。 | `App/Infrastructure/LogService.cs:28,115` NonBacktracking 字段识别与 CredentialEnd 线性转义扫描；异常文本同样处理，失败不落盘原文。 |

### 实际退出控制流

```text
App.OnExit / exiting=true
  → ExitCoordinator.Run（重复/重入请求不再次执行）
  → MainViewModel.CompletePersistence
      → session.Complete（退订 Changed、一次最终保存、缓存结果）
      → Store 取得事务 → 校验主/备份 → Write/Flush(true)/Commit → 读回校验
      → MainViewModel finally 退订日志失败事件
  → 记录 Saved / Failed / Disabled / NotRequired
  → wakeup.Stop（DispatcherTimer）→ power 退订 → tray.Dispose → theme.Dispose → notifications.Dispose
  → instance.Activated 退订 → instance.UnregisterKey
  → base.OnExit（此时才引发 WPF Exit 事件；无 ViewModel 保存订阅）
```

保存错误不会跳过资源清理；每个清理步骤的错误单独报告并继续后续步骤。Completed 只表示所有步骤已尝试，不等于全部成功。若释放 Key 自身失败，报告失败、不声称交接成功，由进程退出时 OS 回收。InProgress 不清理/释放 Key，允许结束后的请求重试；正常 App 路径在同一 Dispatcher 同步运行，不使用 await、消息泵或后台保存，不把 InProgress 当作成功或结束。协调器/session 不取代 WPF 本身生命周期，实际框架退出仍需 Windows 验证。

### Store 支持范围与边界

- 同进程多 Store/线程、同一 OS 用户及会话中使用相同协调器的合作进程、同一 lexical FullPath：共同串行化 Load/Save/Restore/首次创建；不同路径独立。
- 路径转 SHA256 Mutex 名称；Windows 身份转大写，Linux 大小写区分。不持有永久磁盘锁文件，finally 释放、Dispose 句柄；OS 可回收异常退出所有者，AbandonedMutexException 接管后重新校验。
- 默认等待 2 秒，可注入 1ms–5s；同线程同路径嵌套拒绝。不同路径嵌套的循环等待也仅有有限超时，无无限重试。锁等待上限不等于同步文件系统 IO 的时间上限。
- 不保证符号链接/硬链接/设备路径等别名、跨用户/会话、恶意命名对象干扰或绕过协调器的外部写入。外部修改可在校验与替换间制造竞态；没有宣称“再次读取即可防御一切”。不支持的原子替换/锁环境明确失败，不降级为覆盖。
- 本轮线程竞争在 owner 的真实 Flush 后/Commit 前屏障内调用 contender 并等待其超时结束，确认操作实际重叠且不写入；释放 owner 后独立显式重试并验证最新有效备份。没有随机 Sleep、无隐式无限重试，也没有把“两个同时提交成功”作为这组测试结论。

### 新增测试（33 项）及结果

`Tests/PortableRegressionTests.cs` 注册：

- **9 项退出/session**：顺序、一次性/重入、多次请求、真实阻塞成功/失败保存期间所有权保持、异常清理继续、交接立即读取亚秒快照、Disabled、InProgress、transition 保存中重入 Complete。
- **11 项线程/真实文件 IO 与边界**：规范化路径两 Store Save、Save/Restore 双向竞争、首次创建、Load 超时、独立路径、嵌套拒绝、非法等待参数、no-op 提交不能 Saved、提交前/后异常。
- **3 项真实子进程/真实文件 IO**：合作持锁与释放、父/子首存竞争、Kill 持锁进程后锁恢复及孤儿不采用。父进程还验证 Save/Restore/Load 全部遵守同一锁。旧 51 项另有 Environment.Exit(73) 绕过 finally 的提交前中断测试。
- **10 项真实 LogService**：普通凭据/Bearer/Basic、JSON 转义引号、1–12 个连续反斜杠、混合单/双引号、多个/嵌入字段、实际抛出异常及 inner/event、损坏/未闭合/悬空/非法后缀、非敏感内容、长输入、异常序列化失败不写原文。全为人工无效示例凭据。

再次 locked restore Core/Tests、独立 Core/Tests Release warn-as-error、实际 ViewModel 托管诊断编译：均成功，0 警告 / 0 错误。原 51 项正文保留，再次全部通过；新增 33 项全部通过，**总计 84 通过 / 0 失败 / 0 跳过**。完整测试重复三次均同结果；最后格式修正后，再次完成上述 restore/编译和完整 84 项，结果不变。不把重复运行算为新增测试数量。命令和原始输出见 `PORTABLE-PHASE-1-TEST-RESULTS.txt` 追加记录。

### 验收建议

建议接受这三项问题的源码/Linux 修复，Portable Phase 1 整体验收仍以 Windows 门槛为条件，暂不宣称完整通过：

| 证据层级 | 本轮状态 |
| --- | --- |
| 源码、完整 Diff、XML/JSON/引用/敏感信息检查 | 已检查；App 除退出接入外与基线一致，其余 10 个禁止文件逐字节一致 |
| Linux 自动化 | 84/84，0 失败、0 跳过；Core/Tests、实际 ViewModel managed C# 编译成功 |
| Windows CI | 未执行本轮完整 WPF/XAML/App 构建或真实 Application.Exit；禁止修改/触发 Workflow |
| Windows 11 25H2 x64 26200.x 实机 | 未执行；AppInstance 两进程交接、NTFS/ACL/共享句柄及桌面回归待测 |

真实断电/满盘/USB 故障仍未测试，提交边界 IOException 不等于真实满盘。所有工作区变化包含初次 Phase 1 与本次修复，未暂存、未 Commit/Push/PR/合并、未进入 Phase 2。
