$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Publishes both flavors: framework-dependent (win-x64) and self-contained (win-x64-selfcontained).
$targets = @(
    @{ Name = 'win-x64';               SelfContained = $false },
    @{ Name = 'win-x64-selfcontained'; SelfContained = $true }
)
foreach ($target in $targets) {
    $output = Join-Path $root ('artifacts\' + $target.Name)
    $stage = Join-Path $root ('artifacts\staging-' + [Guid]::NewGuid().ToString('N'))
    try {
        foreach ($name in @('Config', 'Launcher')) {
            $arguments = @('publish', (Join-Path $root "src\$name\$name.csproj"), '-c', 'Release', '-o', $stage, '-r', 'win-x64', '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:IncludeNativeLibrariesForSelfExtract=true')
            # Array splat below; do not revert to Start-Process with a joined quoted string.
            $arguments += if ($target.SelfContained) { @('--self-contained', 'true') } else { @('--self-contained', 'false') }
            Write-Host ('dotnet ' + ($arguments -join ' '))
            & dotnet @arguments
            if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name (exit $LASTEXITCODE)." }
            $entry = Join-Path $stage "$name.exe"
            if (!(Test-Path $entry)) { throw "Missing published entry point: $name" }
            Write-Host ('{0}: {1:N1} MB' -f $name, ((Get-Item $entry).Length / 1MB))
        }
        if (-not $target.SelfContained) {
            foreach ($name in @('Config', 'Launcher')) {
                $entry = Join-Path $stage "$name.exe"
                if ((Get-Item $entry).Length -gt 20MB) { throw "$name.exe exceeds 20 MB: framework-dependent publish unexpectedly produced a self-contained image." }
            }
        }
        if (Test-Path $output) { Move-Item $output ($output + '-previous-' + [Guid]::NewGuid().ToString('N')) }
        Move-Item $stage $output
        Write-Host "Published to $output"
    }
    finally {
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    }
}
