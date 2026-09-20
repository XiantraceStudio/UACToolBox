$ErrorActionPreference = 'Stop'
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($scope in @('Process','User','Machine')) {
 $value = [Environment]::GetEnvironmentVariable('XianTrace_UAC_ToolBox', $scope)
 Write-Output "$scope variable: $value"
 if ($value) { Write-Output "Exists: $(Test-Path -LiteralPath $value)" }
}
$file = Join-Path $env:ProgramData "XianTrace\UACToolBox\$sid.json"
if (Test-Path $file) {
 $config = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
 $entry = $config.entries | Where-Object id -eq '0590b413-9852-4545-9f74-c5b00274d952'
 if ($entry) { $entry | Select-Object id,enabled,executablePath,workingDirectory | Format-List } else { Write-Output 'Entry not found' }
} else { Write-Output 'Configuration not found' }
$service = New-Object -ComObject Schedule.Service
$service.Connect()
try {
 $task = $service.GetFolder('\').GetTask("LaunchManager-$sid")
 Write-Output "Task state: $($task.State); last result: $($task.LastTaskResult)"
 $definition = $task.Definition
 Write-Output "Task action: $($definition.Actions.Item(1).Path) $($definition.Actions.Item(1).Arguments)"
 Write-Output "Task logon: $($definition.Principal.LogonType); level: $($definition.Principal.RunLevel)"
} catch { Write-Output "Task query: $($_.Exception.Message)" }
