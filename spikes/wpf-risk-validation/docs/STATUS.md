# 当前执行状态（第二阶段未完成）

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
- 用户已选择先完成本地提交；推送、Actions及Artifact保持阻塞，不尝试绕过授权。
- XML/JSON/YAML静态检查通过，触发条件、Runner、读取权限及40字符Action SHA检查通过。
- 安装器自有中文语言键已对照Inno Setup 6.7.4官方Default.isl核对；这不是安装器编译或安装通过。
- 提交范围排除bin/obj、工具下载和编译工具生成的.mono注册表镜像，不提交运行/构建缓存。
- 62个已还原x64原生DLL做了初步PE导入扫描，未发现VC v14命名的普通/延迟DLL导入；动态加载和Windows实际依赖仍未测试，保留保守App-local策略。

## 尚未执行/阻塞

- 推送与GitHub Actions：需GitHub仓库写入和Workflow授权。
- Windows Runner的实际编译、发布、PE/VC依赖审计、ZIP、Setup、SHA256与Artifact检查：未测试。
- WPF真实窗口、系统托盘、通知显示与点击激活：未测试。
- 普通用户、缺共享VC的环境、安装与卸载：未测试。
- Windows 11 25H2完整Build 26200.x桌面验收：未测试。

## 已记录风险

- SDK `Unregister()`仅结束当前进程接收通知事件；永久清理使用 `UnregisterAll()`。两者不能混同。
- AppNotificationManager API参考页的Singleton提示与部署概述存在不一致，不能仅凭文档保证无共享Windows App SDK环境的通知激活；需干净25H2实测。
- NuGet主包引入较多传递模块；未做模块裁减或包体积实测。
- 所有Windows脚本在当前Linux不能替代Windows执行；尚不能声称云端产物已生成。

**WPF技术路线当前只能暂时保留为候选，不能在Windows发布/桌面风险尚未验证时给出“正式采用已验证”的结论。**
