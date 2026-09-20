param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts\win-x64'
$stage = Join-Path $root ('artifacts\staging-' + [Guid]::NewGuid().ToString('N'))
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (!$dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
try {
    foreach ($name in @('Config', 'Launcher')) {
        $arguments = @('publish', (Join-Path $root "src\$name\$name.csproj"), '-c', 'Release', '-o', $stage, '-r', 'win-x64', '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:IncludeNativeLibrariesForSelfExtract=true')
        if ($FrameworkDependent) { $arguments += @('--self-contained', 'false') }
        else { $arguments += @('--self-contained', 'true') }
        $quoted = $arguments | ForEach-Object { '"' + $_ + '"' }
        $process = Start-Process -FilePath $dotnet -ArgumentList ($quoted -join ' ') -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -ne 0) { throw "Publish failed: $name (exit $($process.ExitCode))" }
        if (!(Test-Path (Join-Path $stage "$name.exe"))) { throw "Missing published entry point: $name" }
    }
    # Retain previous output until both entry points have published successfully.
    if (Test-Path $output) { Move-Item $output ($output + '-previous-' + [Guid]::NewGuid().ToString('N')) }
    Move-Item $stage $output
    # Standard setup package is built separately by scripts/build-setup.ps1 (Inno Setup).
    Write-Host "Published to $output"
} finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
}
