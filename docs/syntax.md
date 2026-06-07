# PTZControlConsole command syntax

`PTZControlConsole` is the automation-oriented command-line interface in this
fork. It is intended for direct shell usage, scripts, and future Bitfocus
Companion integration.

## Device and information commands

```text
PTZControlConsole list-devices
PTZControlConsole cam-device-info [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
```

Example `list-devices` output:

```text
PTZ Pro 2       @device:pnp:\\?\usb#vid_046d&pid_085f&mi_00#...
OBS Virtual Camera      @device:sw:{860BB310-5D01-11D0-BD3B-00A0C911CE86}\...
```

Example `cam-device-info` output:

```text
Camera:
  Device Name: PTZ Pro 2
  Device Path: @device:pnp:\\?\usb#vid_046d&pid_085f&mi_00#...
  PTZControl Slot: 1
  Camera Slot Alias: Main camera

Zoom:
  Percent range: 0..100
  Raw min: 0
  Raw max: 500
  Raw default: 0
  Raw step size: 1
  Raw current: 120

Move X axis:
  Percent range: 0..100
  Raw min: -36000
  Raw max: 36000
  Raw default: 0
  Raw step size: 1
  Raw current: 0

Move Y axis:
  Percent range: 0..100
  Raw min: -36000
  Raw max: 36000
  Raw default: 0
  Raw step size: 1
  Raw current: 0

Available restore targets:
  Home: zoom, move, all
  Default: zoom, move, move-x, move-y, all

Presets:
  Restore range: 1..8
  Save range: 1..8
  Storage: camera Logitech extension unit
  Preset 1: name=Speaker; values=not readable
  Preset 2: name=Stage; values=not readable
  Preset 3: name=(none); values=not readable
  Preset 4: name=(none); values=not readable
  Preset 5: name=(none); values=not readable
  Preset 6: name=(none); values=not readable
  Preset 7: name=(none); values=not readable
  Preset 8: name=(none); values=not readable
```

## Preset and camera friendly names

```text
PTZControlConsole get-preset-name 1..8 [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
PTZControlConsole set-preset-name 1..8 -n|--friendlyname "Title" [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
PTZControlConsole clear-preset-name 1..8 [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
PTZControlConsole get-camera-name -s|--slot 1..3
PTZControlConsole set-camera-name -n|--friendlyname "Title" -s|--slot 1..3
PTZControlConsole clear-camera-name -s|--slot 1..3
PTZControlConsole swap-preset-names --slot-a 1..3 --slot-b 1..3
```

Friendly names are metadata used by PTZControl and automation tools. They do
not rename the physical camera device.

## Metadata config transport

```text
PTZControlConsole config --export [json-path]
PTZControlConsole config --import json-path
```

Without `json-path`, `config --export` writes the JSON document to stdout.

Windows reads and writes the original PTZControl registry values for
compatibility. Linux and macOS use JSON metadata files.

## Restore commands

```text
PTZControlConsole restore-home -t|--target zoom|move|all [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
PTZControlConsole restore-default -t|--target zoom|move|move-x|move-y|all [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
PTZControlConsole restore-preset 1..8 [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
PTZControlConsole save-preset 1..8 [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3] [-n|--friendlyname "Title"]
```

## Zoom commands

```text
PTZControlConsole zoom-absolute VALUE -m|--mode percent|raw [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
PTZControlConsole zoom-relative VALUE_DELTA -m|--mode percent|raw [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
```

`--mode percent` uses values relative to the CLI percent range. `--mode raw`
uses device/driver raw values.

## Move commands

```text
PTZControlConsole move-absolute -m|--mode percent|raw [-x|--pan VALUE] [-y|--tilt VALUE] [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
PTZControlConsole move-relative -m|--mode percent|raw [-x|--pan VALUE_DELTA] [-y|--tilt VALUE_DELTA] [-c|--camera "NamePart" | -d|--device-path "DevicePath" | -s|--slot 1..3]
```

`-x`/`--pan` controls pan. `-y`/`--tilt` controls tilt.

## Selection options

```text
-c, --camera "NamePart"
-d, --device-path "DevicePath"
-s, --slot 1..3
```

Use only one selector at a time. `-c`/`--camera` selects by camera device name
fragment. `-d`/`--device-path` selects by a concrete device path.
`-s`/`--slot` selects the currently enumerated PTZControl camera slot 1, 2, or
3 and is also used for friendly names.
