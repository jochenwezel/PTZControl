# PTZControl Bridge v2 (C++/CLI-first)

Diese Version verdrahtet die **C#-Apps ausschließlich zur C++/CLI-Bridge**.
Die Bridge kapselt:
- **Standard-PTZ** (UVC) via DirectShow/IAMCameraControl (Fallback auf `PTZControl.Uvc` intern).
- **Logitech XU** (IKsControl) als **Platzhalter**: `UseLogitechMotionControl`, `SavePreset`, `RecallPreset` (TODO).

## Projekte
- `PTZControlBridge` (C++/CLI): Öffentliche API → `LogitechPtz` (Enumerate, GetRange, SetPTZ, Presets/Motion stubs).
- `PTZControl.Uvc` (C# Class Library): Managed-Fallback für Standard-PTZ.
- `PTZControlGUI` (WinForms): Referenziert **nur** die Bridge.
- `PTZControlConsole` (Console): Referenziert **nur** die Bridge.

## Build
1. **PTZControl.Uvc** (Debug) bauen.
2. **PTZControlBridge** (Debug) bauen — referenziert die Uvc-DLL per HintPath.
3. **PTZControlGUI** und **PTZControlConsole** (Debug) bauen — referenzieren die Bridge-DLL per HintPath.

> Passen Sie bei Bedarf die HintPaths an Ihre Ausgabeordner/Konfiguration an.

## Verwendung
```powershell
# Liste
PTZControlConsole --list

# Standard-PTZ
PTZControlConsole --camera "Rally" --pan 0 --tilt 50 --zoom 120

# Presets (Stubs -> NotSupportedException, bis XU implementiert)
PTZControlConsole --preset --camera "Rally" --save 1
PTZControlConsole --preset --camera "Rally" --recall 1
```

## Nächste Schritte (Logitech XU)
- In `PTZControlBridge` echte Implementierung via **IKsControl**:
  - Gerät über Moniker öffnen, **Extension Unit Node** finden (Logitech GUIDs).
  - `KSP_NODE` + `IKsControl::KsProperty` mit passenden `KSPROPERTY_*` & Control-IDs.
  - Mapping der Upstream-Konstanten (aus Ihrem Altcode) in `LogitechXuGuids.h` einfügen.
- Danach die GUI-Checkbox **„Motion Control“** und Preset-Buttons aktivieren.

Lizenz-Hinweis: Wenn Sie Code aus dem GPL-3.0-Upstream übernehmen, bleibt die Veröffentlichung GPL-kompatibel.