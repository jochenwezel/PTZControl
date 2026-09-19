[CmdletBinding()]
param(
    [string[]] $Listen,
    [string[]] $AllowIp,
    [string] $Token,
    [switch] $NoSwagger,
    [string] $TaskName = 'PTZControlServer'
)

$ErrorActionPreference = 'Stop'
$server = (Resolve-Path (Join-Path $PSScriptRoot '..\PTZControlServer.exe')).Path
$arguments = [System.Collections.Generic.List[string]]::new()
foreach ($url in $Listen) { $arguments.Add("--listen `"$url`"") }
foreach ($rule in $AllowIp) { $arguments.Add("--allow-ip `"$rule`"") }
if ($Token) { $arguments.Add("--token `"$($Token.Replace('"', '\"'))`"") }
if ($NoSwagger) { $arguments.Add('--no-swagger') }

$action = New-ScheduledTaskAction -Execute $server -Argument ($arguments -join ' ') -WorkingDirectory (Split-Path $server)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Start PTZControlServer when this user signs in.' -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName
Write-Host "Installed and started scheduled task '$TaskName'."
