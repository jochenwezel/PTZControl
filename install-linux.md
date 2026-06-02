# PTZControlConsole on Linux

`PTZControlConsole-linux-x64-beta.zip` is an experimental Linux preview build.
It is intended to verify packaging and basic camera discovery before the Linux
camera-control backend is fully implemented.

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

## Current Limitations

Linux camera control is not implemented yet. Commands such as `move-relative`,
`zoom-relative`, `restore-preset`, and `save-preset` currently fail with a clear
`NotSupportedException` message and an exit code other than `0`.

The next implementation step is a V4L2 backend for standard pan, tilt, and zoom
controls. Logitech preset and home support may require UVC extension-unit access
through Linux `ioctl` calls and must be validated with a real PTZ Pro 2 camera.
