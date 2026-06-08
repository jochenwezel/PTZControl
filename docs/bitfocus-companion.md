# Using PTZControlConsole with Bitfocus Companion

`PTZControlConsole` can be used from Bitfocus Companion buttons by running
command-line actions or scripts.

## Current status

Native Companion module work is tracked separately. The current integration path
is:

1. Install or extract `PTZControlConsole`.
2. Verify camera control from a terminal.
3. Configure Companion buttons to call scripts or command-line actions that
   invoke `PTZControlConsole`.

Until the native module exists, configure actions in the same practical style as
Stream Deck: run `PTZControlConsole.exe` with the desired arguments.

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

## Prepared Companion page

A prepared page export is available here:

```text
docs/assets/PTZControlConsole-companion-page.companionconfig
```

It contains buttons for presets, home movement, zoom, pan, and tilt using
Companion's internal `System: Run shell command (local)` action.

The page assumes:

```text
C:\Tools\PTZControlConsole\PTZControlConsole.exe
--camera "PTZ Pro 2"
```

After importing the page, adjust the executable path or camera selector when
needed.

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
