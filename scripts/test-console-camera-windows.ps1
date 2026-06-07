param(
    [string]$ExePath = ".\PTZControlConsole.exe",
    [string]$Camera,
    [string]$DevicePath,
    [int]$Slot,
    [int]$RawZoomDelta = 100,
    [int]$RawMoveDelta = 3000,
    [int]$RawMoveAbsolute = 9000,
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
elseif ($Slot) {
    $selector = @("--slot", "$Slot")
}

Write-Log "PTZControlConsole guided Windows camera test"
Write-Log "Executable: $ExePath"
Write-Log "Selector: $($selector -join ' ')"
Write-Log "RawZoomDelta: $RawZoomDelta"
Write-Log "RawMoveDelta: $RawMoveDelta"
Write-Log "RawMoveAbsolute: $RawMoveAbsolute"

Invoke-CameraCommand "List devices" @("list-devices")
Invoke-CameraCommand "Camera device info" (@("cam-device-info") + $selector)
Invoke-CameraCommand "Zoom absolute percent 0" (@("zoom-absolute", "0", "--mode", "percent") + $selector)
Invoke-CameraCommand "Zoom absolute percent 50" (@("zoom-absolute", "50", "--mode", "percent") + $selector)
Invoke-CameraCommand "Zoom absolute percent 100" (@("zoom-absolute", "100", "--mode", "percent") + $selector)
Invoke-CameraCommand "Zoom relative percent +10" (@("zoom-relative", "10", "--mode", "percent") + $selector)
Invoke-CameraCommand "Zoom relative percent -10" (@("zoom-relative", "-10", "--mode", "percent") + $selector)
Invoke-CameraCommand "Zoom absolute raw 0" (@("zoom-absolute", "0", "--mode", "raw") + $selector)
Invoke-CameraCommand "Zoom relative raw +$RawZoomDelta" (@("zoom-relative", "$RawZoomDelta", "--mode", "raw") + $selector)
Invoke-CameraCommand "Zoom relative raw -$RawZoomDelta" (@("zoom-relative", "$(-1 * $RawZoomDelta)", "--mode", "raw") + $selector)
Invoke-CameraCommand "Move absolute percent center" (@("move-absolute", "--mode", "percent", "--pan", "50", "--tilt", "50") + $selector)
Invoke-CameraCommand "Move absolute percent pan 40" (@("move-absolute", "--mode", "percent", "--pan", "40") + $selector)
Invoke-CameraCommand "Move absolute percent tilt 60" (@("move-absolute", "--mode", "percent", "--tilt", "60") + $selector)
Invoke-CameraCommand "Move relative percent pan +10" (@("move-relative", "--mode", "percent", "--pan", "10") + $selector)
Invoke-CameraCommand "Move relative percent pan -10" (@("move-relative", "--mode", "percent", "--pan", "-10") + $selector)
Invoke-CameraCommand "Move relative percent tilt +10" (@("move-relative", "--mode", "percent", "--tilt", "10") + $selector)
Invoke-CameraCommand "Move relative percent tilt -10" (@("move-relative", "--mode", "percent", "--tilt", "-10") + $selector)
Invoke-CameraCommand "Move absolute raw center" (@("move-absolute", "--mode", "raw", "--pan", "0", "--tilt", "0") + $selector)
Invoke-CameraCommand "Move absolute raw pan +$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--pan", "$RawMoveAbsolute") + $selector)
Invoke-CameraCommand "Move absolute raw pan -$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--pan", "$(-1 * $RawMoveAbsolute)") + $selector)
Invoke-CameraCommand "Move absolute raw tilt +$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--tilt", "$RawMoveAbsolute") + $selector)
Invoke-CameraCommand "Move absolute raw tilt -$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--tilt", "$(-1 * $RawMoveAbsolute)") + $selector)
Invoke-CameraCommand "Move relative raw pan +$RawMoveDelta" (@("move-relative", "--mode", "raw", "--pan", "$RawMoveDelta") + $selector)
Invoke-CameraCommand "Move relative raw pan -$RawMoveDelta" (@("move-relative", "--mode", "raw", "--pan", "$(-1 * $RawMoveDelta)") + $selector)
Invoke-CameraCommand "Move relative raw tilt +$RawMoveDelta" (@("move-relative", "--mode", "raw", "--tilt", "$RawMoveDelta") + $selector)
Invoke-CameraCommand "Move relative raw tilt -$RawMoveDelta" (@("move-relative", "--mode", "raw", "--tilt", "$(-1 * $RawMoveDelta)") + $selector)
Invoke-CameraCommand "Restore home move" (@("restore-home", "--target", "move") + $selector)
Invoke-CameraCommand "Restore default zoom" (@("restore-default", "--target", "zoom") + $selector)
Invoke-CameraCommand "Restore preset 1" (@("restore-preset", "1") + $selector)
Invoke-CameraCommand "Restore preset 2" (@("restore-preset", "2") + $selector)
Invoke-CameraCommand "List presets" (@("list-presets") + $selector)

Write-Host ""
Write-Host "Log written to $LogPath"
