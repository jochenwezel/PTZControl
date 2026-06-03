# PTZControlConsole on Linux

`PTZControlConsole-linux-x64-beta.zip` is an experimental Linux release-candidate
build. It is intended to verify packaging, camera discovery, and standard V4L2
pan, tilt, and zoom controls before the Linux backend is marked stable.

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

Download `PTZControlConsole-linux-x64-beta.zip` from the GitHub release page and
unpack it:

```bash
unzip PTZControlConsole-linux-x64-beta.zip
cd PTZControlConsole-v2.4.2.1-linux-x64-beta
chmod +x PTZControlConsole
```

Run the device discovery command:

```bash
./PTZControlConsole list-devices
```

The preview backend lists `/dev/video*` devices and tries to read display names
from `/sys/class/video4linux/<device>/name`.

Test standard V4L2 camera controls:

```bash
./PTZControlConsole zoom-relative 10 --camera "PTZ"
./PTZControlConsole zoom-relative -10 --camera "PTZ"
./PTZControlConsole move-relative --x 10 --camera "PTZ"
./PTZControlConsole move-relative --x -10 --camera "PTZ"
./PTZControlConsole move-relative --y 10 --camera "PTZ"
./PTZControlConsole move-relative --y -10 --camera "PTZ"
./PTZControlConsole zoom-absolute 0 --camera "PTZ"
./PTZControlConsole zoom-absolute 50 --camera "PTZ"
./PTZControlConsole zoom-absolute 100 --camera "PTZ"
```

`--camera` may be either a name fragment from `list-devices` or a device path
such as `/dev/video0`.

## Current Limitations

Linux camera control currently uses standard V4L2 absolute pan, tilt, and zoom
controls:

- `V4L2_CID_PAN_ABSOLUTE`
- `V4L2_CID_TILT_ABSOLUTE`
- `V4L2_CID_ZOOM_ABSOLUTE`

If a camera or driver does not expose one of these controls, the related command
fails with a clear error and an exit code other than `0`.

Preset and home support is not implemented on Linux yet. `restore-preset` and
`save-preset` currently fail with a clear `NotSupportedException` message.

The next implementation step for Linux preset and home support is Logitech UVC
extension-unit access through Linux `ioctl` calls. This must be validated with a
real PTZ Pro 2 camera.
