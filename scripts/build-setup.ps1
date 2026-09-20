param(
    [string]$IsccPath,
    [ValidateSet('All', 'FrameworkDependent', 'SelfContained')]
    [string]$Variant = 'All'
)
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
if (!$iscc) { throw 'ISCC.exe not found. Install Inno Setup to D:\Apps\Tools\Inno Setup 7 or pass -IsccPath.' }
$iss = Join-Path $root 'installer\UACToolBox.iss'
$variants = @(
    @{ Key = 'FrameworkDependent'; Payload = 'artifacts\win-x64'; Output = 'UACToolBox-Setup.exe' },
    @{ Key = 'SelfContained'; Payload = 'artifacts\win-x64-selfcontained'; Output = 'UACToolBox-Setup-SelfContained.exe' }
) | Where-Object { $Variant -eq 'All' -or $_.Key -eq $Variant }
foreach ($build in $variants) {
    $payloadDir = Join-Path $root $build.Payload
    foreach ($name in @('Config.exe', 'Launcher.exe')) {
        if (!(Test-Path (Join-Path $payloadDir $name))) { throw "$($build.Payload)\$name missing. Run scripts\publish.ps1 first." }
    }
    Write-Host "Compiling $($build.Output) from $($build.Payload)..."
    & $iscc "/DPayloadDir=..\$($build.Payload)" "/DOutputName=$([IO.Path]::GetFileNameWithoutExtension($build.Output))" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed for $($build.Output) (exit $LASTEXITCODE)." }
    $package = Join-Path $root ('artifacts\' + $build.Output)
    if (!(Test-Path $package)) { throw "Setup package missing: $($build.Output)" }
    Write-Host ('{0}: {1:N1} MB' -f $build.Output, ((Get-Item $package).Length / 1MB))
}
