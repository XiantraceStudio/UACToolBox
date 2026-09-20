# UACToolBox：开发预览


- src/Config：普通权限配置界面，列表与创建/编辑分页面；同一用户会话复用一个管理窗口，导入覆盖已有填写前询问。
- src/Launcher：无窗口客户端及计划任务执行端。
- installer/UACToolBox.iss：Inno Setup 标准安装包脚本（简体中文向导、可选桌面快捷方式、正规卸载注册）。
- src/WindowsIntegration：计划任务、快捷方式、权限和通信封装。
- src/Contracts：授权配置及通信协议。
- tests/Contracts.Tests：可直接运行的协议检查程序。

本版本已通过 Release 编译和 25 项配置、协议检查及 53 项 Windows 集成检查，尚未完成 Windows 端到端验收，不应当作正式安装版本使用。
实际验证范围见 docs/development.md。

使用 .NET 10 SDK：

    dotnet build UACToolBox.slnx -c Release
    dotnet run --project tests/Contracts.Tests -c Release
    dotnet run --project tests/WindowsIntegration.Tests -c Release

发布脚本：scripts/publish.ps1。详细设计见 docs/design.md，已实现范围及缺口见 docs/development.md。

预览包：artifacts/win-x64（Config.exe、Launcher.exe）与 artifacts/UACToolBox-Setup.exe（标准安装包），需安装 .NET 10 Desktop Runtime x64。

安装：运行 UACToolBox-Setup.exe，Inno 标准向导（简体中文），默认安装到 Program Files\XianTrace\UACToolBox；安装过程中自动注册计划任务、环境变量与右键菜单；可选创建桌面快捷方式；在 Windows“应用和功能”中注册并可完整卸载（卸载时一并移除注册项与配置目录）。配置文件位于 ProgramData\XianTrace\UACToolBox。无迁移：旧目录、旧变量不属于新版本职责，需要清理请手动处理或先卸载旧版。

配置界面默认不请求 UAC；打开、导入、保存及删除条目通过已安装的后台任务完成。首次安装 / 修复任务、修改机器环境变量和系统右键菜单时才请求管理员授权。系统页提供“一键注册”，一次授权同时完成计划任务、环境变量和右键菜单；首次启动若无配置且无任何注册，弹窗引导前往注册。

快捷方式名称同时作为列表名称；导入或填写目标时默认生成“原名称_UACTB”。创建位置可选桌面或自定义文件夹，自定义位置随条目保存；同名文件覆盖前提示。启用开关取消并保存后保留配置、暂停该条目的启动。

配置界面已接入 WPF UI 4.3.0，采用侧栏导航和浅色卡片。快捷方式创建成功后自动清空创建表单；失败或取消时保留填写内容。界面依赖已合并进 Config.exe。

界面使用中性色和直角侧栏；列表右键提供打开、编辑和删除。编辑页分为程序与启动、输出位置、更多设置三个标签；设置页分为系统和关于，按组件分组展示状态（绿色/红色圆点）及各自操作。快捷方式使用 %XianTrace_UAC_ToolBox%。
