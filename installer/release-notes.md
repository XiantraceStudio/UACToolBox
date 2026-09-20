UACToolBox {TAG}

免 UAC 的 Windows 快捷方式启动与配置工具。

安装
- 标准版 UACToolBox-Setup.exe：约 4 MB，需已安装 .NET 10 Desktop Runtime x64
- 自包含版 UACToolBox-Setup-SelfContained.exe：内含运行时，无需额外依赖，体积较大
- 两个版本安装相同的应用，任选其一；默认安装到 C:\Program Files\XianTrace\UACToolBox
- 安装时勾选：计划任务与环境变量（必选）、右键菜单（可选，默认不勾）、桌面快捷方式（可选，默认勾选）
- 自定义安装目录会自动尝试加固目录权限；父目录链普通用户可删除或改名时注册会失败并指出具体路径

主要功能
- 计划任务后台执行端：日常打开、编辑、保存、删除配置全程免 UAC
- 快捷方式通过 %XianTrace_UAC_ToolBox% 指向启动器
- 资源管理器右键菜单：通过 UACToolBox 运行 / 添加到 UACToolBox
- 首次启动引导与系统页一键注册；组件状态以红绿圆点展示
- 在 Windows“应用和功能”中注册并完整卸载

本版本为开发预览性质。
