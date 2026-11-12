# Portable-only 发布流水线（待审核，未提交）

本轮只改发布路径，不改 Phase 1 业务或既有 84 项测试。不 Commit、Push 或触发旧远程代码。Windows Runner 与真实 Portable 应用 ZIP 尚未生成/验证，Windows 11 25H2 x64 26200.x 桌面全部保持未测试。

## 单一交付与目录

唯一 Artifact：`PomodoroTimer-Portable-Windows-x64`（30天）。只含 `PomodoroTimer-Portable-Windows-x64.zip`、外部 `SHA256SUMS.txt` 和明确列出的构建/校验报告，没有 Setup 或第二失败 Artifact。

```text
PomodoroTimer-Portable-Windows-x64.zip
└─ PomodoroTimer/
   ├─ PomodoroTimer.exe
   ├─ PomodoroTimerSpike.dll
   ├─ PomodoroTimerSpike.deps.json
   ├─ PomodoroTimerSpike.runtimeconfig.json
   ├─ PomodoroTimer.Spike.Core.dll
   ├─ .NET/WPF/Windows App SDK/VC App-local DLL、winmd、资源等完整publish运行载荷
   ├─ README.md（独立 Portable 用户手册，不是开发 README）
   ├─ BUILD-INFO.txt
   ├─ TEST-SUMMARY.txt
   ├─ LOGIC-TESTS.txt、PUBLISH-TESTS.txt、INITIALIZATION-TEST.txt
   ├─ DEPLOYMENT-PROPERTIES.json、PUBLISH-MANIFEST.csv
   ├─ NATIVE-DEPENDENCIES.txt、VC-RUNTIME.csv、VC-RUNTIME.txt
   ├─ WINDOWS-SDK-INFO.txt
   └─ SHA256SUMS.txt（以上全部文件，不含自己）
```

这是预定结构，不是已产生产品 ZIP 的文件清单。首次正常运行才创建 `Data/`、`Logs/`；不打包用户数据、PDB、源码、测试、开发工具或临时构建文件。无需模板的新业务不会凭空加入配置模板。保留所有 publish 运行依赖，不做 DLL 体积裁减；报告中记录实际文件数量与哈希，没有宣称已测量包大小或最小依赖集。

内部程序集名不改：仅发布目录的 `PomodoroTimerSpike.exe` 重命名为 `PomodoroTimer.exe`，对应 DLL/deps/runtimeconfig 保留原名。校验 apphost 内嵌 DLL 名，Windows Runner 必须执行重命名 EXE 的现有 `--self-test`。这个测试只初始化 WPF XAML 和原生 AppNotificationBuilder，不注册/显示通知，不是桌面验收。

## 固定属性与构建

- Runner `windows-2025-vs2026`；三项 Actions 均固定40字符SHA，只读仓库权限。
- SDK精确 `10.0.403`；运行时 `10.0.14`；TFM `net10.0-windows10.0.26100.0`；Windows App SDK `2.6.5 Stable`；Windows SDK `10.0.26100.9457`；managed reference `10.0.26100.57`。
- Core/Tests/App locked restore，Core/Tests/App Release编译（完整WPF原生工具链）。真实 `dotnet run --no-build` 输出逐项PASS/SKIP/FAIL与TOTAL，不写死成功数量。
- 实际MSBuild求值保存JSON：`win-x64`、`Release`、`SelfContained=true`、`WindowsAppSDKSelfContained=true`、`WindowsPackageType=None`、`PublishSingleFile=false`、`PublishTrimmed=false`、原AssemblyName与TFM。
- 不向Core传播全局RID/AssemblyName，不升级NuGet或引入预览依赖。应用项目及锁文件不修改。
- 保留签名微软VC Redist目录完整复制与实际 dumpbin 依赖审计，不从System32复制；静态检查不能替代干净桌面Loader测试。

## 顺序及校验

1. 执行真实逻辑测试，按实际宿主OS解析结果；构建、发布、重命名、VC审计。此时尚不生成publish manifest。
2. 真实初始化成功后聚合原始LOGIC/PUBLISH/INITIALIZATION输出；动态计数、原始报告SHA及所有NOT TESTED保留。
3. 校验器独立正/负例测试使用**非可运行的合成PE/ZIP fixture**，清楚标注非产品、非Windows证据。既有84项测试逻辑不变。
4. Windows打包脚本检查真实SDK、干净checkout与完整Commit SHA，复核汇总；将 `docs/PORTABLE-USER-GUIDE.md` 复制为publish根的 `README.md`，验证链接后生成/复核publish manifest，再生成BUILD-INFO并绑定SDK报告/部署属性哈希；Linux禁止产品打包。
5. 独立临时目录完整复制运行载荷和报告，核对publish/VC manifest，再生成ZIP内全文件SHA256，最后创建ZIP。
6. 外部SHA256覆盖ZIP与全部内外报告；`verify-artifacts.ps1 -ExpectedCommit <完整SHA>`拒绝空ZIP/空文件、缺EXE/运行依赖、不符运行时/RID/部署属性/身份、危险路径、重复条目、链接、文件目录冲突、禁入内容、哈希覆盖不全/内容不符、伪造简略成功报告及内外证据差异。先预检再安全解压，限制20,000条目、单文件1GiB、解压总4GiB，仅清理本次唯一临时目录。
7. 成功校验生成独立 `ARTIFACT-TESTS.txt`（含ZIP SHA与NOT TESTED），再纳入外部哈希并用 `-RequireVerificationReport`复核；`PIPELINE-SCRIPT-TESTS.txt`也纳入最终外部哈希。TEST-SUMMARY和ZIP不在打包后追加内容，避免循环/陈旧哈希。
8. 全部成功才上传唯一明确命名Artifact，失败只保留Actions控制台诊断，不发布不完整ZIP。

内部SHA256保证实际每个解压文件与清单一致，外部SHA256保证ZIP/报告传输完整性。原始结果与动态计数/汇总/报告哈希严格比对，空报告、只写“84通过”、缺逐项结果、丢失NOT TESTED或汇总篡改会失败。**未签名哈希不能认证发布者，也无法阻止攻击者同时重写全部二进制、报告及哈希；执行真实性来自受信任Runner的实际命令/退出码与日志，不是文本报告自己证明。** 合成fixture验证结构逻辑，绝不交付作产品包。

## 验证边界

2026-11-11 本轮 Linux 实际验证（不是 Windows 产品证据）：

| 检查 | 实际结果 | 本机原始证据 |
|---|---|---|
| SDK、Core/Tests locked restore、Release `-warnaserror` | `10.0.403`；两项目编译0警告/错误 | `/tmp/opencode/portable-pipeline-dotnet-validation.txt` |
| 既有真实逻辑/基础设施测试 | `TOTAL 84; PASSED 84; FAILED 0; SKIPPED 0` | `/tmp/opencode/portable-pipeline-logic-tests.txt` |
| Linux MSBuild部署属性求值 | 9项符合固定基线；不等于Windows发布 | `/tmp/opencode/portable-pipeline-deployment-properties.json` |
| PowerShell `7.6.10` 语法解析 | 8个脚本、Workflow全部10段 `run` 块成功 | `/tmp/opencode/portable-pipeline-powershell-parse.txt` |
| 非可运行合成校验器正/负例 | `VALIDATOR TOTAL 53; PASSED 53; FAILED 0; SKIPPED 0` | `/tmp/opencode/portable-pipeline-validator-tests.txt` |

合成校验器绑定的真实84项原始逻辑报告SHA256为 `c87e8f6c3c74ae802bba7c774ef982cc4a66859a75c36f69b4f8f849b9667fc7`；这不是产品 ZIP 的SHA256。本机临时证据尚未上传为正式Artifact，Phase 1源码和既有测试逻辑保持不变。

Windows Runner尚未执行；实际publish文件集合、重命名Loader、VC签名/导入、真实ZIP和SHA256尚未验证。普通用户启动/写入/搬迁、真实通知/托盘/单实例交接、休眠、干净运行时机器及NTFS/硬件故障均按 [DESKTOP-ACCEPTANCE.md](DESKTOP-ACCEPTANCE.md) 保持未测试。

## P1/P3 修复（2026-11-12，待 Commit 审核）

- P1：不修改84项业务测试、两项Unix权限测试或原始输出。正式 `Get-LogicTestResult` 从真实OS确定规则，无环境变量/命令行平台覆盖。纯解析器的显式平台只供合成fixture使用。
- Linux必须零FAIL/零SKIP且两项权限测试均有PASS；Windows必须零FAIL且恰好下面两项SKIP，名称、大小写和原因逐字匹配：
  - `Actual read-only Data directory rejects saves without changing data`
  - `Actual read-only Logs directory warns without blocking initialization`
  - 两项原因均为 `Unix permissions test; Windows ACL validation is separate`。
- 动态核对逐项记录与TOTAL/PASSED/FAILED/SKIPPED、统计等式、名称唯一性、缺失/重复统计、缺失/未知结果行；仅认可受保护 `DiagnosticReporter.Fallback` 的精确stderr诊断格式。任何不一致抛出终止错误，不将SKIP当PASS。
- Workflow保留原始输出并附加Windows ACL `NOT TESTED`。TEST-SUMMARY保留真实计数、原始SKIP名称/原因、报告哈希及Windows ACL未验状态。
- P3：ZIP不再复制开发README或平铺桌面验收文档。用户手册独立、无本地或外部链接；不加入STATUS、Phase 1或流水线内部文档。用户功能、可写目录、备份/退出/搬迁和通知限制见 [PORTABLE-USER-GUIDE.md](PORTABLE-USER-GUIDE.md)。
- 链接检查验证相对目标是包内非空文件（含引用式、图片、编码路径、大小写），拒绝出界路径、不支持的链接语法和缺失目标；HTTP/HTTPS单独分类，不将外链当包内文件或在发布中联网获取。再次核对staging和解压ZIP的README链接。
- `verify-publish.ps1`仅移出manifest生成/复核，原有载荷检查、VC清单及dumpbin审计全部保留。必要顺序为真实测试→宿主解析→原始报告汇总→用户手册→链接→manifest→BUILD-INFO→内哈希→ZIP→外哈希/ZIP两次校验→唯一上传。

本轮Linux证据位于 `/tmp/opencode/portable-p1p3/`（仅本机诊断，不是产品Artifact）：

| 检查 | 结果 | 证据 |
|---|---|---|
| SDK、Core/Tests locked restore、Release `-warnaserror` | SDK `10.0.403`，两项目0警告/错误 | `dotnet-validation.txt` |
| 原有真实84项业务/基础设施测试 | `TOTAL 84; PASSED 84; FAILED 0; SKIPPED 0`；两项权限测试实际PASS | `logic-tests.txt` |
| 非可运行校验器fixture | `VALIDATOR TOTAL 121; PASSED 121; FAILED 0; SKIPPED 0`；保留原53项，新增68项 | `validator-tests.txt` |

Windows的82PASS/2SKIP只来自当前84项真实Linux报告的**合成平台输出模拟**，不是Windows执行；计数仍从报告动态推导。真实Windows Runner、真实产品ZIP/Loader与Windows 11 25H2桌面继续 `NOT TESTED`。未Commit/Push/PR/合并，不修改其他分支，所有Phase 1代码/测试/项目及锁文件保持本轮开始时的指纹。

Phase 1历史文档和原始输出保留原日期/范围，旧名称/未改交付流程的描述仅是历史证据，不代表当前Portable发布定义。
