param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts\win-x64'
$stage = Join-Path $root ('artifacts\staging-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($name in @('Config', 'Launcher')) {
        $arguments = @('publish', (Join-Path $root "src\$name\$name.csproj"), '-c', 'Release', '-o', $stage, '-r', 'win-x64', '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:IncludeNativeLibrariesForSelfExtract=true')
        # Array splat below; do not revert to Start-Process with a joined quoted string.
        $arguments += if ($FrameworkDependent) { @('--self-contained', 'false') } else { @('--self-contained', 'true') }
        Write-Host ('dotnet ' + ($arguments -join ' '))
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name (exit $LASTEXITCODE)." }
        $entry = Join-Path $stage "$name.exe"
        if (!(Test-Path $entry)) { throw "Missing published entry point: $name" }
        Write-Host ('{0}: {1:N1} MB' -f $name, ((Get-Item $entry).Length / 1MB))
    }
    if ($FrameworkDependent) {
        foreach ($name in @('Config', 'Launcher')) {
            $entry = Join-Path $stage "$name.exe"
            if ((Get-Item $entry).Length -gt 20MB) { throw "$name.exe exceeds 20 MB: framework-dependent publish unexpectedly produced a self-contained image." }
        }
    }
    if (Test-Path $output) { Move-Item $output ($output + '-previous-' + [Guid]::NewGuid().ToString('N')) }
    Move-Item $stage $output
    Write-Host "Published to $output"
} finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
}
