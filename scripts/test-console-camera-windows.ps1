param(
    [string]$ExePath = ".\PTZControlConsole.exe",
    [string]$Camera,
    [string]$DevicePath,
    [int]$Slot,
    [int]$RawZoomDelta = 100,
    [int]$RawMoveDelta = 3000,
    [int]$RawMoveAbsolute = 9000,
    [string]$LogPath = (Join-Path "." ("PTZControlConsole-camera-test-windows-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss")))
)

$ErrorActionPreference = "Stop"

function Write-Log([string]$Text) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp $Text" | Out-File -FilePath $LogPath -Append -Encoding utf8
}

function ConvertTo-CameraDeviceList([string]$Text) {
    $devices = @()
    foreach ($line in ($Text -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parts = $line -split "`t", 2
        if ($parts.Count -lt 2) {
            $parts = $line -split "\s{2,}", 2
        }

        if ($parts.Count -ge 2) {
            $devices += [pscustomobject]@{
                Name = $parts[0].Trim()
                DevicePath = $parts[1].Trim()
            }
        }
    }
    return $devices
}

function Invoke-CameraCommand([string]$Title, [string[]]$Arguments, [bool]$AskForVisibleResult = $true) {
    Write-Host ""
    if ($AskForVisibleResult) {
        Write-Host "Test: $Title"
    }

    Write-Log ""
    Write-Log "COMMAND: $Title"
    Write-Log "VISIBLE-CHECK: $AskForVisibleResult"
    Write-Log "ARGS: $($Arguments -join ' ')"

    $stdoutFile = New-TemporaryFile
    $stderrFile = New-TemporaryFile
    try {
        $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        $stdout = Get-Content -LiteralPath $stdoutFile -Raw
        $stderr = Get-Content -LiteralPath $stderrFile -Raw

        Write-Log "EXIT: $($process.ExitCode)"
        Write-Log "STDOUT: $stdout"
        Write-Log "STDERR: $stderr"

        if ($AskForVisibleResult) {
            $result = Read-Host "Visible camera result OK? (y/n/skip, optional note)"
            Write-Log "USER-RESULT: $result"
        }
        else {
            Write-Log "USER-RESULT: not requested"
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutFile -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrFile -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-LoggedCommand([string]$Title, [string[]]$Arguments) {
    Write-Log ""
    Write-Log "COMMAND: $Title"
    Write-Log "VISIBLE-CHECK: False"
    Write-Log "ARGS: $($Arguments -join ' ')"

    $stdoutFile = New-TemporaryFile
    $stderrFile = New-TemporaryFile
    try {
        $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        $stdout = Get-Content -LiteralPath $stdoutFile -Raw
        $stderr = Get-Content -LiteralPath $stderrFile -Raw

        Write-Log "EXIT: $($process.ExitCode)"
        Write-Log "STDOUT: $stdout"
        Write-Log "STDERR: $stderr"
        Write-Log "USER-RESULT: not requested"

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = $stdout
            StdErr = $stderr
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutFile -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrFile -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-PreparationCommand([string]$Title, [string[]]$Arguments) {
    $result = Invoke-LoggedCommand "Prepare: $Title" $Arguments
    if ($result.ExitCode -ne 0) {
        Write-Log "PREPARE-WARNING: preparation command failed; continuing with visible test"
    }
}

function Select-VisibleCamera([object[]]$Devices) {
    if ($Devices.Count -eq 0) {
        Write-Host "No camera devices found. Visible tests will run without an explicit selector."
        return @()
    }

    Write-Host ""
    Write-Host "Select camera for visible tests:"
    for ($i = 0; $i -lt $Devices.Count; $i++) {
        Write-Host ("  {0}: {1}" -f ($i + 1), $Devices[$i].Name)
    }

    do {
        $choice = Read-Host "Camera number"
        $index = 0
        $valid = [int]::TryParse($choice, [ref]$index) -and $index -ge 1 -and $index -le $Devices.Count
    } until ($valid)

    $device = $Devices[$index - 1]
    Write-Host "Visible tests use: $($device.Name)"
    Write-Log "VISIBLE-TEST-CAMERA: $($device.Name)"
    Write-Log "VISIBLE-TEST-DEVICE-PATH: $($device.DevicePath)"
    return @("--device-path", $device.DevicePath)
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

Write-Host "PTZControlConsole guided Windows camera test"
Write-Host "Log file: $LogPath"

Write-Log "PTZControlConsole guided Windows camera test"
Write-Log "Executable: $ExePath"
Write-Log "Selector: $($selector -join ' ')"
Write-Log "RawZoomDelta: $RawZoomDelta"
Write-Log "RawMoveDelta: $RawMoveDelta"
Write-Log "RawMoveAbsolute: $RawMoveAbsolute"

$listDevicesResult = Invoke-LoggedCommand "List available camera devices" @("list-devices")
$devices = @(ConvertTo-CameraDeviceList $listDevicesResult.StdOut)

if ($selector.Count -eq 0) {
    $selector = Select-VisibleCamera $devices
}
else {
    Write-Host "Visible tests use selector: $($selector -join ' ')"
    Write-Log "VISIBLE-TEST-SELECTOR: $($selector -join ' ')"
}

foreach ($device in $devices) {
    $deviceSelector = @("--device-path", $device.DevicePath)
    Invoke-LoggedCommand "Collect camera device info and supported ranges for $($device.Name)" (@("cam-device-info") + $deviceSelector) | Out-Null
    Invoke-LoggedCommand "Collect preset names and storage details for $($device.Name)" (@("list-presets") + $deviceSelector) | Out-Null
}

Invoke-PreparationCommand "Move zoom away from absolute percent value 0" (@("zoom-absolute", "100", "--mode", "percent") + $selector)
Invoke-CameraCommand "Set zoom to absolute percent value 0" (@("zoom-absolute", "0", "--mode", "percent") + $selector)
Invoke-PreparationCommand "Move zoom away from absolute percent value 50" (@("zoom-absolute", "0", "--mode", "percent") + $selector)
Invoke-CameraCommand "Set zoom to absolute percent value 50" (@("zoom-absolute", "50", "--mode", "percent") + $selector)
Invoke-PreparationCommand "Move zoom away from absolute percent value 100" (@("zoom-absolute", "0", "--mode", "percent") + $selector)
Invoke-CameraCommand "Set zoom to absolute percent value 100" (@("zoom-absolute", "100", "--mode", "percent") + $selector)
Invoke-PreparationCommand "Move zoom to low percent value before relative +10" (@("zoom-absolute", "0", "--mode", "percent") + $selector)
Invoke-CameraCommand "Change zoom by relative percent value +10" (@("zoom-relative", "10", "--mode", "percent") + $selector)
Invoke-PreparationCommand "Move zoom to high percent value before relative -10" (@("zoom-absolute", "100", "--mode", "percent") + $selector)
Invoke-CameraCommand "Change zoom by relative percent value -10" (@("zoom-relative", "-10", "--mode", "percent") + $selector)
Invoke-PreparationCommand "Move zoom away from absolute raw value 0" (@("zoom-relative", "$RawZoomDelta", "--mode", "raw") + $selector)
Invoke-CameraCommand "Set zoom to absolute raw value 0" (@("zoom-absolute", "0", "--mode", "raw") + $selector)
Invoke-PreparationCommand "Move zoom down before relative raw +$RawZoomDelta" (@("zoom-relative", "$(-1 * $RawZoomDelta)", "--mode", "raw") + $selector)
Invoke-CameraCommand "Change zoom by relative raw value +$RawZoomDelta" (@("zoom-relative", "$RawZoomDelta", "--mode", "raw") + $selector)
Invoke-PreparationCommand "Move zoom up before relative raw -$RawZoomDelta" (@("zoom-relative", "$RawZoomDelta", "--mode", "raw") + $selector)
Invoke-CameraCommand "Change zoom by relative raw value -$RawZoomDelta" (@("zoom-relative", "$(-1 * $RawZoomDelta)", "--mode", "raw") + $selector)
Invoke-PreparationCommand "Move pan and tilt away from percent center" (@("move-absolute", "--mode", "percent", "--pan", "0", "--tilt", "0") + $selector)
Invoke-CameraCommand "Move pan and tilt to absolute percent position 50/50" (@("move-absolute", "--mode", "percent", "--pan", "50", "--tilt", "50") + $selector)
Invoke-PreparationCommand "Move pan away from absolute percent position 40" (@("move-absolute", "--mode", "percent", "--pan", "60") + $selector)
Invoke-CameraCommand "Move pan axis to absolute percent position 40" (@("move-absolute", "--mode", "percent", "--pan", "40") + $selector)
Invoke-PreparationCommand "Move tilt away from absolute percent position 60" (@("move-absolute", "--mode", "percent", "--tilt", "40") + $selector)
Invoke-CameraCommand "Move tilt axis to absolute percent position 60" (@("move-absolute", "--mode", "percent", "--tilt", "60") + $selector)
Invoke-PreparationCommand "Move pan before relative percent +10" (@("move-absolute", "--mode", "percent", "--pan", "40") + $selector)
Invoke-CameraCommand "Change pan axis by relative percent value +10" (@("move-relative", "--mode", "percent", "--pan", "10") + $selector)
Invoke-PreparationCommand "Move pan before relative percent -10" (@("move-absolute", "--mode", "percent", "--pan", "60") + $selector)
Invoke-CameraCommand "Change pan axis by relative percent value -10" (@("move-relative", "--mode", "percent", "--pan", "-10") + $selector)
Invoke-PreparationCommand "Move tilt before relative percent +10" (@("move-absolute", "--mode", "percent", "--tilt", "40") + $selector)
Invoke-CameraCommand "Change tilt axis by relative percent value +10" (@("move-relative", "--mode", "percent", "--tilt", "10") + $selector)
Invoke-PreparationCommand "Move tilt before relative percent -10" (@("move-absolute", "--mode", "percent", "--tilt", "60") + $selector)
Invoke-CameraCommand "Change tilt axis by relative percent value -10" (@("move-relative", "--mode", "percent", "--tilt", "-10") + $selector)
Invoke-PreparationCommand "Move pan and tilt away from raw center" (@("move-absolute", "--mode", "raw", "--pan", "$RawMoveAbsolute", "--tilt", "$RawMoveAbsolute") + $selector)
Invoke-CameraCommand "Move pan and tilt to absolute raw position 0/0" (@("move-absolute", "--mode", "raw", "--pan", "0", "--tilt", "0") + $selector)
Invoke-PreparationCommand "Move pan away from absolute raw +$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--pan", "0") + $selector)
Invoke-CameraCommand "Move pan axis to absolute raw position +$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--pan", "$RawMoveAbsolute") + $selector)
Invoke-PreparationCommand "Move pan away from absolute raw -$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--pan", "0") + $selector)
Invoke-CameraCommand "Move pan axis to absolute raw position -$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--pan", "$(-1 * $RawMoveAbsolute)") + $selector)
Invoke-PreparationCommand "Move tilt away from absolute raw +$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--tilt", "0") + $selector)
Invoke-CameraCommand "Move tilt axis to absolute raw position +$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--tilt", "$RawMoveAbsolute") + $selector)
Invoke-PreparationCommand "Move tilt away from absolute raw -$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--tilt", "0") + $selector)
Invoke-CameraCommand "Move tilt axis to absolute raw position -$RawMoveAbsolute" (@("move-absolute", "--mode", "raw", "--tilt", "$(-1 * $RawMoveAbsolute)") + $selector)
Invoke-PreparationCommand "Move pan before relative raw +$RawMoveDelta" (@("move-absolute", "--mode", "raw", "--pan", "0") + $selector)
Invoke-CameraCommand "Change pan axis by relative raw value +$RawMoveDelta" (@("move-relative", "--mode", "raw", "--pan", "$RawMoveDelta") + $selector)
Invoke-PreparationCommand "Move pan before relative raw -$RawMoveDelta" (@("move-absolute", "--mode", "raw", "--pan", "0") + $selector)
Invoke-CameraCommand "Change pan axis by relative raw value -$RawMoveDelta" (@("move-relative", "--mode", "raw", "--pan", "$(-1 * $RawMoveDelta)") + $selector)
Invoke-PreparationCommand "Move tilt before relative raw +$RawMoveDelta" (@("move-absolute", "--mode", "raw", "--tilt", "0") + $selector)
Invoke-CameraCommand "Change tilt axis by relative raw value +$RawMoveDelta" (@("move-relative", "--mode", "raw", "--tilt", "$RawMoveDelta") + $selector)
Invoke-PreparationCommand "Move tilt before relative raw -$RawMoveDelta" (@("move-absolute", "--mode", "raw", "--tilt", "0") + $selector)
Invoke-CameraCommand "Change tilt axis by relative raw value -$RawMoveDelta" (@("move-relative", "--mode", "raw", "--tilt", "$(-1 * $RawMoveDelta)") + $selector)
Invoke-PreparationCommand "Move pan and tilt away from home before restore-home" (@("move-absolute", "--mode", "percent", "--pan", "0", "--tilt", "0") + $selector)
Invoke-CameraCommand "Restore home position for pan and tilt" (@("restore-home", "--target", "move") + $selector)
Invoke-PreparationCommand "Move zoom away from default before restore-default" (@("zoom-absolute", "100", "--mode", "percent") + $selector)
Invoke-CameraCommand "Restore default zoom value" (@("restore-default", "--target", "zoom") + $selector)
Invoke-CameraCommand "Restore preset 1" (@("restore-preset", "1") + $selector)
Invoke-CameraCommand "Restore preset 2" (@("restore-preset", "2") + $selector)

Write-Host ""
Write-Host "Log written to $LogPath"
