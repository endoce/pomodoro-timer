# VC++ Runtime 风险与部署

## 已核实的官方结论

- Windows App SDK的未打包桌面部署有VC++ Redistributable要求；不能将 `.NET SelfContained` 或 `WindowsAppSDKSelfContained` 当成VC依赖也已满足。
- 微软支持应用本地目录分发可再分发VC DLL；中央 `vc_redist.x64.exe` 安装通常要求管理员权限。
- UCRT自Windows 10起是OS组件。它不等于VC++ v14的 `msvcp140.dll`、`vcruntime140.dll` 等组件。
- 不能从“Windows 11 25H2”标签推断所有VC Runtime已装，必须记录现场安装与实际加载模块。
- 缺DLL可能显示“找不到 xxx.dll”；DLL版本过旧也可能出现入口点缺失。这里是官方说明的错误类别，不是本PoC已经复现的现场错误。

## 本PoC方案

本地对已还原微软包中的62个x64原生DLL做了PE普通/延迟导入初步扫描，未发现以 `msvcp`、`vcruntime`、`concrt`、`vccorlib`、`vcamp` 开头的DLL导入。示例 `Microsoft.WindowsAppRuntime.dll` 包含 `api-ms-win-crt-*` UCRT导入。**静态导入扫描不能排除动态加载、初始化和其他组件依赖，不足以推翻官方部署要求或宣布VC完全不需要。** 最终发布目录与真实Loader仍需审计。

CI从已安装的微软Visual Studio **Redist/MSVC/x64/Microsoft.VC*.CRT**目录复制整个匹配CRT目录中的DLL，并验证微软Authenticode签名，记录文件版本、来源和SHA256。不复制System32、不下载第三方DLL、不分发Debug运行库。

这是保守的依赖部署选择，不将预先列出的三个DLL名称冒充已证实的最小依赖集。确切VC最低版本与加载路径目前尚未通过Windows运行核实。

发布审计要求存在 `msvcp140.dll`、`vcruntime140.dll`、`vcruntime140_1.dll`，再扫描实际PE导入检查其他VC DLL依赖。这是文件/导入检查，尚不等同实际无共享VC机器上的Loader验证。具体DLL集合以构建产生的 `VC-RUNTIME.csv` 和 `NATIVE-DEPENDENCIES.txt` 为准。

便携ZIP和Setup均携带同一份App-local依赖。Setup `PrivilegesRequired=lowest`，不静默运行需要提升权限的中央VC安装器。若App-local不能满足实际组件，必须记录失败并重新审核，不能吞掉错误或宣布普通用户运行成功。

App-local DLL不会被Windows Update替本应用自动更新。安全修复需要重新构建、重新交付。再分发权利受微软Visual Studio许可约束，需核对用户/项目适用许可，不能将“来自微软”当成无限分发许可。

## 人工依赖验证

1. 使用实际Windows 11 25H2 x64普通用户，无.NET/Visual Studio开发工具。
2. 记录完整OS Build、日期、权限、已装VC Redistributable版本。
3. 在干净VM快照上验证未装中央VC的场景；不要卸载日常工作机的共享VC。
4. 完整ZIP启动、通知、点击激活和退出全部测试。
5. 通过Process Explorer或PowerShell进程Modules记录VC DLL加载路径，区分应用目录与系统目录。
6. 缺依赖负例：只在**另一份测试副本**移除App-local DLL；若系统仍有共享VC，不能将启动成功当成负例通过。
7. 记录具体Loader提示、错误码、日志及事件查看器证据；测试后恢复VM快照。
8. 用同一干净快照验证Setup不要求管理员、卸载成功。

原生依赖缺失可能在SDK模块初始化、WPF OnStartup之前失败，因而没有应用日志。此时记录Windows Loader/事件查看器错误，不将“无日志”解释为没有错误。

## 官方资料

- [Windows App SDK未打包依赖](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)
- [VC中央/本地部署](https://learn.microsoft.com/en-us/cpp/windows/deployment-in-visual-cpp)
- [DLL再分发、许可与UAC](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files)
- [当前VC版本和架构规则](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)

**当前缺少真实25H2桌面。上述干净环境、缺失DLL、提权及安装卸载场景均为未测试。**
