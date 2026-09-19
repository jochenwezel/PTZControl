[CmdletBinding()]
param(
    [string[]] $Listen,
    [string[]] $AllowIp,
    [string] $Token,
    [switch] $NoSwagger,
    [string] $ServiceName = 'PTZControlServer'
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}

$server = (Resolve-Path (Join-Path $PSScriptRoot '..\PTZControlServer.exe')).Path
$arguments = [System.Collections.Generic.List[string]]::new()
foreach ($url in $Listen) { $arguments.Add("--listen `"$url`"") }
foreach ($rule in $AllowIp) { $arguments.Add("--allow-ip `"$rule`"") }
if ($Token) { $arguments.Add("--token `"$($Token.Replace('"', '\"'))`"") }
if ($NoSwagger) { $arguments.Add('--no-swagger') }
$binaryPath = "`"$server`" $($arguments -join ' ')".Trim()

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service '$ServiceName' already exists. Remove it before reinstalling."
}

New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName 'PTZControl HTTP Server' -Description 'HTTP control API for PTZ cameras and Bitfocus Companion.' -StartupType Automatic | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
Start-Service -Name $ServiceName
Write-Host "Installed and started Windows service '$ServiceName'."
