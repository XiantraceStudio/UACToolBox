param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# ASCII only: executed by Windows PowerShell 5.1 which reads BOM-less scripts as ANSI.
$iscc = $IsccPath
if (!$iscc) {
    $candidates = @(
        'D:\Apps\Tools\Inno Setup 7\ISCC.exe',
        'D:\Apps\Tools\InnoSetup\ISCC.exe',
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (!$iscc) { $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source }
if (!$iscc) { throw 'ISCC.exe not found. Install Inno Setup to D:\Apps\Tools\InnoSetup or pass -IsccPath.' }
if (!(Test-Path (Join-Path $root 'artifacts\win-x64\Config.exe')) -or !(Test-Path (Join-Path $root 'artifacts\win-x64\Launcher.exe'))) {
    throw 'artifacts\win-x64\Config.exe / Launcher.exe missing. Run scripts\publish.ps1 first.'
}
& $iscc (Join-Path $root 'installer\UACToolBox.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed (exit $LASTEXITCODE)." }
Write-Host 'Setup package: artifacts\UACToolBox-Setup.exe'
