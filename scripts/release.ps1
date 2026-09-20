param([string]$Version = '1.0.0', [string]$Repo = 'XiantraceStudio/UACToolBox')
# ASCII only in code: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
# Chinese release notes live in installer\release-notes.md (UTF-8, read explicitly below).
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
$notesTemplate = [System.IO.File]::ReadAllText((Join-Path $root 'installer\release-notes.md'), (New-Object System.Text.UTF8Encoding($false)))
$notes = $notesTemplate.Replace('{TAG}', $tag)
$notesPath = Join-Path $env:TEMP "uactoolbox-release-notes-$Version.md"
[System.IO.File]::WriteAllText($notesPath, $notes, (New-Object System.Text.UTF8Encoding($false)))
gh release create $tag --repo $Repo --title "UACToolBox $tag" --notes-file $notesPath $setup
if ($LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
Remove-Item $notesPath -ErrorAction SilentlyContinue
Write-Host "Released $tag with UACToolBox-Setup.exe attached."
