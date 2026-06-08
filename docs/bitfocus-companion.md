# Using PTZControlConsole with Bitfocus Companion

`PTZControlConsole` is intended to become the backend for a future native
Bitfocus Companion module. Until that module exists, it can still be useful as a
command-line bridge in Companion setups that can run external commands or
scripts.

## Current status

Native Companion module work is tracked separately. The current integration path
is:

1. Install or extract `PTZControlConsole`.
2. Verify camera control from a terminal.
3. Configure Companion buttons to call scripts or command-line actions that
   invoke `PTZControlConsole`.

The native module target UI is intentionally not documented here yet. Until the
module exists, configure actions in the same practical style as Stream Deck:
run `PTZControlConsole.exe` with the desired arguments.

## Preparation

Download and extract a console package from the GitHub release page.

Recommended Windows folder:

```text
C:\Tools\PTZControlConsole
```

Verify the camera:

```powershell
.\PTZControlConsole.exe list-devices
.\PTZControlConsole.exe cam-device-info --camera "PTZ Pro 2"
```

For ambiguous camera names, prefer `--device-path` or rename the Windows
DirectShow camera name first.

## Companion button command examples

Restore preset 1:

```text
C:\Tools\PTZControlConsole\PTZControlConsole.exe restore-preset 1 --camera "PTZ Pro 2"
```

Save preset 1:

```text
C:\Tools\PTZControlConsole\PTZControlConsole.exe save-preset 1 --camera "PTZ Pro 2"
```

Move home:

```text
C:\Tools\PTZControlConsole\PTZControlConsole.exe restore-home --target move --camera "PTZ Pro 2"
```

Zoom in:

```text
C:\Tools\PTZControlConsole\PTZControlConsole.exe zoom-relative 10 --mode percent --camera "PTZ Pro 2"
```

Pan right:

```text
C:\Tools\PTZControlConsole\PTZControlConsole.exe move-relative --mode percent --pan 10 --camera "PTZ Pro 2"
```

## Future native module expectations

A native Companion module should provide camera dropdowns instead of requiring
manual command strings. The dropdown metadata should include:

- camera device name,
- Windows DirectShow camera friendly name where available,
- PTZControl camera slot,
- PTZControl camera slot alias,
- concrete device path.

The module should let the user decide whether a button targets the camera by
name, slot, or device path.

## Suggested Companion actions

Initial module actions should cover:

- restore preset,
- save preset,
- restore home,
- restore default,
- zoom absolute,
- zoom relative,
- move absolute,
- move relative,
- move seek,
- get or expose preset names,
- camera information refresh.

## Screenshot checklist

Add screenshots later for:

1. Current workaround: Companion command/script action configuration.
2. Current workaround: full `PTZControlConsole.exe` command-line invocation.
3. Future native module: module/connection configuration.
4. Future native module: console path setting.
5. Future native module: camera dropdown with device name, slot, alias, and
   device path information.
6. Future native module: button action configured for `restore-preset`.
7. Future native module: button action configured for zoom or movement.
8. Future native module: Companion button grid with camera preset buttons.
