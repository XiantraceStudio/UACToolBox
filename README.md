# UACToolBox

Windows 免 UAC 快捷方式启动与配置工具。

通过一个管理员授权注册的计划任务作为常驻提权执行端，日常创建、编辑、保存、删除快捷方式配置全程不弹 UAC；只有首次注册 / 修复系统组件时需要一次管理员授权。

## 功能

- **免 UAC 配置**：普通权限的配置界面（WPF UI），保存与删除经后台任务写入受保护的配置文件。
- **免 UAC 启动**：快捷方式指向 `%XianTrace_UAC_ToolBox%` 环境变量解析的启动器，由计划任务以授权身份启动目标程序。
- **右键菜单**：资源管理器中「通过 UACToolBox 运行」「添加到 UACToolBox…」。
- **组件状态面板**：任务、配置文件、环境变量、右键菜单红绿圆点实时显示，支持一键注册。
- **首次启动引导**：无配置且无任何注册时弹窗引导完成注册。

## 安装

下载 [Release](https://github.com/XiantraceStudio/UACToolBox/releases) 中的安装包运行（Inno Setup 标准向导，简体中文）：

- **标准版 UACToolBox-Setup.exe**（约 4 MB）：需已安装 [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0)。
- **自包含版 UACToolBox-Setup-SelfContained.exe**：内含运行时，无需额外依赖，体积较大。
- 两版安装相同应用，任选其一；默认安装到 `C:\Program Files\XianTrace\UACToolBox`，配置文件位于 `C:\ProgramData\XianTrace\UACToolBox`。
- 安装时勾选：计划任务与环境变量（必选）、右键菜单（可选，默认不勾）、桌面快捷方式（可选，默认勾选）。
- 在 Windows「应用和功能」中注册，卸载时一并移除注册项与配置。

> 自定义安装目录：安装位置必须整条父目录链都由管理员控制（如 Program Files）。安装时会自动加固所选目录本身的权限；若父目录链普通用户可删除或改名（数据盘默认授权即如此），注册会失败并指出具体路径。

## 安全模型

- 计划任务执行端指向的 `Launcher.exe` 及其目录经过 ACL 校验：所有者必须是管理员 / 系统，普通用户不可写入。
- 配置文件带所有者校验与修订号乐观锁，仅当前用户的管理员授权可写，保存请求经命名管道验签调用方。
- 任何父目录链上普通用户可删除 / 改名的层级，都能通过「改名替换」劫持提权执行端，因此校验不放宽、不静默修改数据盘根目录权限。

## 开发

```
dotnet build UACToolBox.slnx -c Release
dotnet run --project tests/Contracts.Tests -c Release
dotnet run --project tests/WindowsIntegration.Tests -c Release
```

| 目录 | 说明 |
|---|---|
| `src/Config` | 配置界面（WPF UI，普通权限） |
| `src/Launcher` | 无窗口客户端与计划任务执行端 |
| `src/WindowsIntegration` | 计划任务、快捷方式、ACL 与通信封装 |
| `src/Contracts` | 配置模型与通信协议 |
| `installer/` | Inno Setup 安装脚本与发行说明模板 |
| `scripts/` | 发布（`publish.ps1`）、打包（`build-setup.ps1`）、发版（`release.ps1`）、图标生成 |
| `tests/` | 协议检查与 Windows 集成检查（53 项） |

详细设计见 `docs/design.md`，实现进度与已知缺口见 `docs/development.md`。
