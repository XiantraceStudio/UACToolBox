param([string]$Version = '1.0.0', [string]$Repo = 'XiantraceStudio/UACToolBox')
# ASCII only in code: executed by Windows PowerShell 5.1 (ANSI for BOM-less).
# Release notes below are written to a UTF-8 file without BOM via WriteAllText.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ([string]::IsNullOrWhiteSpace($Version)) { throw 'Version is required.' }
$tag = "v$Version"

# 1. Working tree must be clean so the tag matches what is committed.
$status = git status --porcelain
if ($status) { throw "Uncommitted changes present. Commit or stash first.`n$status" }

# 2. Integration checks must pass.
Write-Host 'Running integration checks...'
& dotnet run -c Release --project tests/WindowsIntegration.Tests/WindowsIntegration.Tests.csproj | Tee-Object -Variable testOutput | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (($testOutput | Select-Object -Last 1) -match '\d+ Windows checks passed')) { throw 'Integration checks failed.' }

# 3. Fresh publish and setup package.
& (Join-Path $PSScriptRoot 'publish.ps1') -FrameworkDepended
if ($LASTEXITCODE -ne 0) { throw 'publish.ps1 failed.' }
& (Join-Path $PSScriptRoot 'build-setup.ps1')
if ($LASTEXITCODE -ne 0) { throw 'build-setup.ps1 failed.' }
$setup = Join-Path $root 'artifacts\UACToolBox-Setup.exe'
if (!(Test-Path $setup)) { throw 'Setup package missing.' }

# 4. Tag and push.
if (git rev-parse -q --verify "refs/tags/$tag" *> $null) { throw "Tag $tag already exists." }
git tag $tag
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw 'Failed to push tag.' }

# 5. GitHub release with the setup package attached.
$notes = @"
UACToolBox $tag

免 UAC 的 Windows 快捷方式启动与配置工具。

安装
- 下载 UACToolBox-Setup.exe 运行（标准 Inno Setup 向导，简体中文）
- 默认安装到 C:\Program Files\XianTrace\UACToolBox
- 安装时勾选：计划任务与环境变量（必选）、右键菜单（可选，默认不勾）、桌面快捷方式（可选，默认勾选）
- 需已安装 .NET 10 Desktop Runtime x64

主要功能
- 计划任务后台执行端：日常打开、编辑、保存、删除配置全程免 UAC
- 快捷方式通过 %XianTrace_UAC_ToolBox% 指向启动器
- 资源管理器右键菜单：通过 UACToolBox 运行 / 添加到 UACToolBox
- 首次启动引导与系统页一键注册；组件状态以红绿圆点展示
- 在 Windows“应用和功能”中注册并完整卸载

本版本为开发预览性质。
"@
$notesPath = Join-Path $env:TEMP "uactoolbox-release-notes-$Version.md"
[System.IO.File]::WriteAllText($notesPath, $notes, (New-Object System.Text.UTF8Encoding($false)))
gh release create $tag --repo $Repo --title "UACToolBox $tag" --notes-file $notesPath $setup
if ($LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
Remove-Item $notesPath -ErrorAction SilentlyContinue
Write-Host "Released $tag with UACToolBox-Setup.exe attached."
