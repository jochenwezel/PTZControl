# PTZControlConsole on Linux

`PTZControlConsole-linux-x64-beta.tar.gz` is an experimental Linux
release-candidate build. It is intended to verify packaging, camera discovery,
and standard V4L2 pan, tilt, and zoom controls before the Linux backend is
marked stable.

## Requirements

- Linux x64
- .NET 8 Runtime or a later major .NET runtime installed
- Access to the camera device, usually `/dev/video*`

On Ubuntu, install the .NET 8 runtime with:

```bash
sudo apt-get update
sudo apt-get install -y dotnet-runtime-8.0
```

If your Ubuntu installation does not provide `dotnet-runtime-8.0` from its
configured package sources, follow Microsoft's .NET installation instructions
for your Ubuntu version:

- .NET 8 downloads: https://dotnet.microsoft.com/download/dotnet/8.0
- Install .NET on Ubuntu: https://learn.microsoft.com/dotnet/core/install/linux-ubuntu

The app targets .NET 8 and is configured to roll forward to later major .NET
runtime versions. This means that an installed .NET 10 runtime should be enough
to start the app, even without .NET 8 installed. If runtime selection still fails
on a specific machine, set `DOTNET_ROLL_FORWARD=Major` before starting the app.

## Installation

Download `PTZControlConsole-linux-x64-beta.tar.gz` from the GitHub release page and
unpack it:

```bash
tar -xzf PTZControlConsole-linux-x64-beta.tar.gz
cd PTZControlConsole-v2.4.2.4-linux-x64-beta
```

Run the device discovery command:

```bash
./PTZControlConsole list-devices
```

The preview backend lists `/dev/video*` devices and tries to read display names
from `/sys/class/video4linux/<device>/name`.

Test standard V4L2 camera controls:

```bash
./PTZControlConsole zoom-relative 10 --mode percent --camera "PTZ"
./PTZControlConsole zoom-relative -10 --mode percent --camera "PTZ"
./PTZControlConsole move-relative --mode percent --pan 10 --camera "PTZ"
./PTZControlConsole move-relative --mode percent --pan -10 --camera "PTZ"
./PTZControlConsole move-relative --mode percent --tilt 10 --camera "PTZ"
./PTZControlConsole move-relative --mode percent --tilt -10 --camera "PTZ"
./PTZControlConsole zoom-absolute 0 --mode percent --camera "PTZ"
./PTZControlConsole zoom-absolute 50 --mode percent --camera "PTZ"
./PTZControlConsole zoom-absolute 100 --mode percent --camera "PTZ"
```

`-c`/`--camera` matches a device name fragment from `list-devices`.
`-d`/`--device-path` selects a concrete device path such as `/dev/video0`.
`-x`/`--pan` controls pan. `-y`/`--tilt` controls tilt.

## Metadata Configuration

On Linux, preset names and camera slot aliases are stored in:

```text
~/.config/PTZControl/ptzcontrol.json
```

If `XDG_CONFIG_HOME` is set, the app uses:

```text
$XDG_CONFIG_HOME/PTZControl/ptzcontrol.json
```

Export the current metadata configuration to stdout:

```bash
./PTZControlConsole config --export
```

Export it to a file:

```bash
./PTZControlConsole config --export ptzcontrol-config.json
```

Import a metadata configuration file:

```bash
./PTZControlConsole config --import ptzcontrol-config.json
```

## Guided Camera Test

The release package includes `scripts/test-console-camera-linux.sh`. It runs a
guided command sequence, records exit codes, stdout, stderr, `cam-device-info`,
`list-devices`, `list-presets`, and asks the tester to confirm the visible
camera result after each action.

Example:

```bash
scripts/test-console-camera-linux.sh --exe ./PTZControlConsole --camera "PTZ"
```

Or select by device path:

```bash
scripts/test-console-camera-linux.sh --exe ./PTZControlConsole --device-path /dev/video0
```

## Current Limitations

Linux camera control currently uses standard V4L2 absolute pan, tilt, and zoom
controls:

- `V4L2_CID_PAN_ABSOLUTE`
- `V4L2_CID_TILT_ABSOLUTE`
- `V4L2_CID_ZOOM_ABSOLUTE`

If a camera or driver does not expose one of these controls, the related command
fails with a clear error and an exit code other than `0`.

Preset and home support is not implemented on Linux yet. `restore-home`,
`restore-preset`, and `save-preset` currently fail with a clear
`NotSupportedException` message.

The next implementation step for Linux preset and home support is Logitech UVC
extension-unit access through Linux `ioctl` calls. This must be validated with a
real PTZ Pro 2 camera.
