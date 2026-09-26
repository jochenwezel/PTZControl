# PTZControlServer HTTP API

`PTZControlServer` exposes camera discovery and PTZ actions over HTTP. It is
designed for Bitfocus Companion's Generic HTTP Requests module, Stream Deck
integrations, scripts, browser bookmarks, and other automation clients.

The server runs on Windows and Linux and uses the same camera backend as
`PTZControlConsole`. Camera and preset support still depends on the operating
system, driver, and camera model.

## Start the server

Start with the secure local-only defaults:

```powershell
PTZControlServer.exe
```

The defaults listen on both IPv4 and IPv6 localhost:

```text
http://127.0.0.1:7070
http://[::1]:7070
```

Open `http://127.0.0.1:7070/swagger` for the interactive Swagger UI. The
OpenAPI document is available at
`http://127.0.0.1:7070/swagger/v1/swagger.json`.

## Diagnostic file logging

File logging is disabled by default. No log directory or file is created unless
logging is explicitly enabled:

```powershell
PTZControlServer.exe --log-level debug
```

`information` records requests, status codes, failures, and startup details.
`debug` additionally records camera resolution, property ranges, current values,
requested deltas, calculated targets, and values read back after writes. Request
headers and the authentication token are never written to the log.

Logs rotate daily, use names such as
`PTZControlServer-2026-09-26.log`, and are retained for 14 days. Override the
platform-specific default directory when needed:

```powershell
PTZControlServer.exe --log-level debug --log-directory C:\PTZControlLogs
```

Interactive Windows runs default to `%LOCALAPPDATA%\PTZControl\Logs`; Windows
services use `%PROGRAMDATA%\PTZControl\Logs`. Linux defaults to
`$XDG_STATE_HOME/PTZControl/logs` or `~/.local/state/PTZControl/logs`.

## Automatic startup

Every server package contains installation helpers in `scripts`.

### Windows user startup (recommended)

USB camera and DirectShow access is normally most reliable in the interactive
user session. Install an auto-start task for the current user without requiring
administrator rights:

```powershell
.\scripts\install-windows-startup.ps1
```

The task starts the server immediately and again whenever that user signs in.
Remove it with:

```powershell
.\scripts\remove-windows-startup.ps1
```

Server options can be stored in the task during installation:

```powershell
.\scripts\install-windows-startup.ps1 `
  -Listen 'http://0.0.0.0:7070' `
  -AllowIp '192.168.1.0/24' `
  -Token 'replace-with-a-long-random-secret'
```

For a temporary diagnostic installation, add `-LogLevel debug`. Logging remains
off when `-LogLevel` is omitted.

Keep the extracted server directory at a permanent local path after installing
the task. Mapped drives such as `Q:` may not be available at sign-in time.

### Windows service (optional)

The server also supports the Windows Service Control Manager. From an elevated
PowerShell window run:

```powershell
.\scripts\install-windows-service.ps1
```

Enable detailed diagnostics during installation with:

```powershell
.\scripts\install-windows-service.ps1 -LogLevel debug
```

Remove it with `remove-windows-service.ps1`. The service starts automatically
with Windows and restarts after failures. It runs as `LocalSystem` in session 0;
some USB camera drivers, privacy settings, or Logitech extensions may only work
in an interactive user session. Prefer the scheduled-task option if camera
discovery or control fails in service mode.

### Linux systemd service

The Linux beta package includes a systemd installer:

```bash
sudo ./scripts/install-linux-systemd.sh
```

It installs to `/opt/ptzcontrolserver`, enables the service, and starts it.
Override the service user, install directory, or arguments when needed:

```bash
sudo PTZCONTROL_USER=companion \
  PTZCONTROL_INSTALL_DIR=/opt/ptzcontrolserver \
  PTZCONTROL_SERVER_ARGS='--listen http://0.0.0.0:7070 --allow-ip 192.168.1.0/24' \
  ./scripts/install-linux-systemd.sh
```

Use `journalctl -u ptzcontrolserver` for logs and
`sudo ./scripts/remove-linux-systemd.sh` to remove the service definition.

### Start with Bitfocus Companion

As an alternative, create a Companion startup trigger that runs
`PTZControlServer.exe` using Companion's local system-command action. This ties
server availability to Companion and is useful for portable setups, but it
does not provide the restart and lifecycle management of Task Scheduler or
systemd. Do not combine multiple startup methods, or the second process will
fail because port 7070 is already in use.

To accept requests from a LAN, bind all interfaces and limit access with an IP
allowlist and token:

```powershell
PTZControlServer.exe `
  --listen http://0.0.0.0:7070 `
  --listen http://[::]:7070 `
  --allow-ip 192.168.1.* `
  --allow-ip 2001:db8:1234::/48 `
  --token "replace-with-a-long-random-secret"
```

Quote wildcard rules on Linux so the shell does not expand `*`:

```bash
./PTZControlServer --listen http://0.0.0.0:7070 \
  --allow-ip '192.168.1.*' --token 'replace-with-a-long-random-secret'
```

Multiple `--listen` and `--allow-ip` options are supported. Allowlist entries
may be exact IPv4/IPv6 addresses, trailing IPv4 wildcards such as
`192.168.1.*`, or CIDR networks such as `192.168.1.0/24` and
`2001:db8::/32`. If no allowlist is configured, every client able to reach a
configured listen address is accepted.

When `--token` is used, send this header with every `/api` and `/action`
request:

```text
X-PTZControl-Token: replace-with-a-long-random-secret
```

Swagger and `/health` remain readable so the service can be diagnosed. Use
`--no-swagger` to disable both Swagger UI and the OpenAPI document.

## Information endpoints

```text
GET /health
GET /api/devices
GET /api/camera/2/info
GET /api/camera/info?slot=2
```

`/api/devices` returns the slot, device name, camera friendly name, and device
path needed for Companion dropdowns and configuration. Camera information
also returns zoom, pan, and tilt ranges/current values plus preset names.

## REST-style action endpoints

The action endpoints accept both `POST` and `PUT`. Parameters are query-string
values so they can be entered directly in Companion's Generic HTTP Requests
configuration.

```text
POST|PUT /api/camera/2/zoom-absolute?mode=percent&value=50
POST|PUT /api/camera/2/zoom-relative?mode=raw&value=1
POST|PUT /api/camera/2/move-absolute?mode=percent&pan=50&tilt=50
POST|PUT /api/camera/2/move-relative?mode=raw&pan=-10&tilt=5
POST|PUT /api/camera/2/restore-preset/1
POST|PUT /api/camera/2/save-preset/1
POST|PUT /api/camera/2/restore-home?target=move
POST|PUT /api/camera/2/restore-default?target=all
```

Modes are `percent` and `raw`. Absolute percent values use `0..100`; relative
percent values use `-100..100`. Raw limits are camera-specific and can be read
from the camera information endpoint.

Home targets are `zoom`, `move`, and `all`. Driver-default targets are `zoom`,
`move`, `move-x`, `move-y`, and `all`. Preset numbers are `1..8`.

## Simple GET action URLs

These convenience endpoints are useful for browser bookmarks and clients that
can only invoke a URL. A successful request changes camera state, so do not use
them for monitoring or automatic link previews. Responses include
`Cache-Control: no-store`.

```text
GET /action/zoom-absolute?slot=2&mode=percent&value=50
GET /action/zoom-relative?slot=2&mode=raw&value=1
GET /action/move-absolute?slot=2&mode=percent&pan=50&tilt=50
GET /action/move-relative?slot=2&mode=raw&pan=-10&tilt=5
GET /action/restore-preset?slot=2&preset=1
GET /action/save-preset?slot=2&preset=1
GET /action/restore-home?slot=2&target=move
GET /action/restore-default?slot=2&target=all
```

## Bitfocus Companion

A prepared Companion 5 page is included in every server package and is also
available as a standalone release asset:

```text
PTZControlServer-Companion-Page.companionconfig
```

The icon-focused 5x3 page provides presets 1-5, zoom, pan, tilt, home,
driver-default restore, and navigation back to page 1. Zoom, pan, and tilt use
Companion's `Logic: While loop`, so they repeat for as long as the button is
held. Presets, home, and driver-default restore remain single actions.

The template assumes:

```text
Server: http://127.0.0.1:7070
Camera slot: 1
```

After importing, edit the Generic HTTP URLs if Companion and PTZControlServer
run on different computers or the camera uses another slot. When token
authentication is enabled, add the `X-PTZControl-Token` header to every HTTP
action. The page imports a Generic HTTP Requests connection dependency, which
Companion may ask you to map to an existing connection.

In Generic HTTP Requests, configure the PTZControlServer computer as the host
and create a request action with one of the REST-style URLs above. Select
`POST` or `PUT`. If token authentication is enabled, add the
`X-PTZControl-Token` request header.

Example preset button:

```text
Method: POST
URL: http://192.168.1.20:7070/api/camera/2/restore-preset/1
Header: X-PTZControl-Token: replace-with-a-long-random-secret
```

Use the Swagger UI from a browser on the Companion computer to verify network,
allowlist, token, and camera behavior before configuring all buttons.

## Command-line options

```text
--listen URL       Bind an HTTP address; repeat for multiple addresses.
--allow-ip RULE    Allow an exact IP, IPv4 wildcard, or CIDR network; repeatable.
--token SECRET     Require the X-PTZControl-Token header.
--no-swagger       Disable Swagger UI and OpenAPI JSON.
--log-level LEVEL  Enable information/debug file logging, or use off.
--log-directory    Override the directory used for daily log files.
-h, --help, -?     Display server help.
```

On Windows, allow inbound TCP port 7070 in Windows Firewall only for the
network profiles and remote addresses that should reach the server. On Linux,
the framework-dependent package requires the .NET 8 runtime or a newer .NET
runtime with major-version roll-forward support.
