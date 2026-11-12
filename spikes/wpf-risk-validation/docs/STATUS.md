# 当前执行状态（Portable 流水线待审核）

日期：2026-11-11。工作区 `spike/wpf-risk-validation`，HEAD `3588efa7118f1d667e47053e08a04e627e0f82dc`。Phase 1 与本轮流水线修改均未提交/推送。技术路线和 Portable Only 是已确定的产品方向，不表示 Windows 风险已经验证。

## 当前证据边界

- Linux：本轮使用精确 SDK `10.0.403` 重跑 Core/Tests locked restore、Release `-warnaserror` 编译，均为0警告/错误；既有真实测试 `TOTAL 84; PASSED 84; FAILED 0; SKIPPED 0`。Phase 1 的 ViewModel 托管诊断编译证据保留；这些都不是完整 WPF 构建。
- 本轮 Linux MSBuild 实际求值的9项部署属性符合固定基线；PowerShell `7.6.10` 实际解析8个脚本、Workflow全部10段 `run` 块成功；非可运行合成校验器正/负例 `VALIDATOR TOTAL 53; PASSED 53; FAILED 0; SKIPPED 0`，不是产品 ZIP 或 Windows 执行证据。
- 本轮原始输出保存在 `/tmp/opencode/portable-pipeline-dotnet-validation.txt`、`/tmp/opencode/portable-pipeline-logic-tests.txt`、`/tmp/opencode/portable-pipeline-deployment-properties.json`、`/tmp/opencode/portable-pipeline-powershell-parse.txt`、`/tmp/opencode/portable-pipeline-validator-tests.txt`；这些是本机审核证据，不是已上传的正式 Artifact。详情见 [PORTABLE-PIPELINE.md](PORTABLE-PIPELINE.md)。
- 本轮仅改 Workflow、发布脚本、文档和确认无用的 installer 文件，业务源码和既有 84 项测试不得改变。
- 已取消 Inno 下载/安装/编译及 Setup 生成/校验/上传；唯一正式 ZIP/Artifact 名称见 [PORTABLE-PIPELINE.md](PORTABLE-PIPELINE.md)。
- 新流水线准备 Windows 完整构建、真实测试、重命名 apphost 初始化、App-local VC 审计、报告/逐文件哈希、ZIP 解压及内外校验。脚本 fixture 测试只是非可运行合成数据的校验器测试，不是产品打包证据。
- **Windows Runner、本轮真实应用 Portable ZIP/哈希、Windows 11 25H2 x64 26200.x 桌面均 NOT TESTED。** 未提交源码不会通过旧远程 Commit 的运行得到验证，本轮不 Commit/Push/触发旧代码。
- 普通用户可写路径/NTFS、AppInstance所有权交接/立即重启、目录搬迁、无共享运行时 Loader、真实通知显示/点击、托盘、睡眠/Modern Standby、断电/磁盘满/USB拔出均未测试。

## 历史记录（2026-11-10 原 PoC，不是本轮证据或当前授权）

日期：2026-11-10。执行环境为Linux，不是正式目标Windows桌面。

## 已执行

- 原工作区 `test/opencode-smoke` 干净，未修改。
- 获取最新 `origin/master`，基线为 `d9031b28a0a9855fdef93353eda21177a90ba7d4`。
- 目标分支原先不存在，新建 `spike/wpf-risk-validation` 独立Worktree。
- .NET SDK 10.0.403官方Linux归档SHA512校验后用于还原/核心测试。
- Core与App依赖成功还原，生成实际NuGet锁文件。
- 14/14计时、暂停恢复、业务操作、通知生命周期和失败隔离测试通过。
- MSBuild实际属性求值确认：TFM指定值、win-x64、SelfContained=true、WindowsAppSDKSelfContained=true、WindowsPackageType=None；此检查不执行Windows原生构建工具。
- 完整Windows构建在Linux尝试失败：Windows App SDK manifest步骤需要原生 `mt.exe`，错误MSB3073/Exec format error。**不算WPF编译通过**。
- 另做C#/XAML编译诊断（仅命令行临时关闭Windows App SDK自包含，未修改发布配置）：编译生成程序集，无编译警告，但随后仍因Windows原生MakePRI失败。该诊断不算完整构建或发布通过，不交付该诊断产物。
- GitHub匿名只读API与Git读取可用；推送协议端点HTTP 401，当前没有可用GitHub登录授权。
- 当时用户选择先完成 PoC 本地提交；此为历史状态。当前授权明确禁止再次 Commit/Push。
- XML/JSON/YAML静态检查通过，触发条件、Runner、读取权限及40字符Action SHA检查通过。
- 当时安装器自有中文语言键已对照 Inno 官方文本核对；并未编译/安装通过，该发布路径现已删除。
- 提交范围排除bin/obj、工具下载和编译工具生成的.mono注册表镜像，不提交运行/构建缓存。
- 62个已还原x64原生DLL做了初步PE导入扫描，未发现VC v14命名的普通/延迟DLL导入；动态加载和Windows实际依赖仍未测试，保留保守App-local策略。

## 尚未执行/阻塞

- 推送与GitHub Actions：需GitHub仓库写入和Workflow授权。
- 当时 Windows Runner 编译、发布、PE/VC、ZIP/Setup、SHA256/Artifact 均未测试；Setup 现已取消，不将历史记录改成通过。
- WPF真实窗口、系统托盘、通知显示与点击激活：未测试。
- 普通用户、缺共享VC的环境、安装与卸载：未测试。
- Windows 11 25H2完整Build 26200.x桌面验收：未测试。

## 已记录风险

- SDK `Unregister()`仅结束当前进程接收通知事件；永久清理使用 `UnregisterAll()`。两者不能混同。
- AppNotificationManager API参考页的Singleton提示与部署概述存在不一致，不能仅凭文档保证无共享Windows App SDK环境的通知激活；需干净25H2实测。
- NuGet主包引入较多传递模块；未做模块裁减或包体积实测。
- 所有Windows脚本在当前Linux不能替代Windows执行；尚不能声称云端产物已生成。

**历史技术风险尚未关闭。当前用户已确定 WPF/Portable 方向，但 Windows 发布/桌面仍需独立证据，不能写成“正式采用已验证”。**
