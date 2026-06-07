param(
    [string]$ExePath = ".\PTZControlConsole.exe",
    [string]$Camera,
    [string]$DevicePath,
    [string]$LogPath = ".\PTZControlConsole-camera-test-windows.log"
)

$ErrorActionPreference = "Stop"

function Write-Log([string]$Text) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp $Text" | Tee-Object -FilePath $LogPath -Append
}

function Invoke-CameraCommand([string]$Title, [string[]]$Arguments) {
    Write-Host ""
    Write-Host "== $Title =="
    Write-Host "$ExePath $($Arguments -join ' ')"
    Write-Log ""
    Write-Log "COMMAND: $Title"
    Write-Log "ARGS: $($Arguments -join ' ')"

    $stdoutFile = New-TemporaryFile
    $stderrFile = New-TemporaryFile
    try {
        $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        $stdout = Get-Content -LiteralPath $stdoutFile -Raw
        $stderr = Get-Content -LiteralPath $stderrFile -Raw

        Write-Host "Exit code: $($process.ExitCode)"
        if ($stdout) { Write-Host "STDOUT:`n$stdout" }
        if ($stderr) { Write-Host "STDERR:`n$stderr" }

        Write-Log "EXIT: $($process.ExitCode)"
        Write-Log "STDOUT: $stdout"
        Write-Log "STDERR: $stderr"

        $result = Read-Host "Visible camera result OK? (y/n/skip, optional note)"
        Write-Log "USER-RESULT: $result"
    }
    finally {
        Remove-Item -LiteralPath $stdoutFile -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrFile -Force -ErrorAction SilentlyContinue
    }
}

if (Test-Path -LiteralPath $LogPath) {
    Remove-Item -LiteralPath $LogPath -Force
}

$selector = @()
if ($Camera) {
    $selector = @("--camera", $Camera)
}
elseif ($DevicePath) {
    $selector = @("--device-path", $DevicePath)
}

Write-Log "PTZControlConsole guided Windows camera test"
Write-Log "Executable: $ExePath"
Write-Log "Selector: $($selector -join ' ')"

Invoke-CameraCommand "List devices" @("list-devices")
Invoke-CameraCommand "Camera device info" (@("cam-device-info") + $selector)
Invoke-CameraCommand "Zoom absolute percent 0" (@("zoom-absolute", "0", "--mode", "percent") + $selector)
Invoke-CameraCommand "Zoom absolute percent 50" (@("zoom-absolute", "50", "--mode", "percent") + $selector)
Invoke-CameraCommand "Zoom relative percent +10" (@("zoom-relative", "10", "--mode", "percent") + $selector)
Invoke-CameraCommand "Zoom relative percent -10" (@("zoom-relative", "-10", "--mode", "percent") + $selector)
Invoke-CameraCommand "Move relative percent X +10" (@("move-relative", "--mode", "percent", "--x", "10") + $selector)
Invoke-CameraCommand "Move relative percent X -10" (@("move-relative", "--mode", "percent", "--x", "-10") + $selector)
Invoke-CameraCommand "Move relative percent Y +10" (@("move-relative", "--mode", "percent", "--y", "10") + $selector)
Invoke-CameraCommand "Move relative percent Y -10" (@("move-relative", "--mode", "percent", "--y", "-10") + $selector)
Invoke-CameraCommand "Restore home move" (@("restore-home", "--target", "move") + $selector)
Invoke-CameraCommand "Restore default zoom" (@("restore-default", "--target", "zoom") + $selector)
Invoke-CameraCommand "Restore preset 1" (@("restore-preset", "1") + $selector)
Invoke-CameraCommand "Restore preset 2" (@("restore-preset", "2") + $selector)
Invoke-CameraCommand "List presets" (@("list-presets") + $selector)

Write-Host ""
Write-Host "Log written to $LogPath"
