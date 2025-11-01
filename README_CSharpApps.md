# PTZControl Bridge Skeleton

**Ziel:** Weg 1 (C++/CLI Bridge + C#-UI). Dieses Skeleton liefert:
- `PTZControl.Uvc` (C# Class Library): UVC-PTZ (Pan/Tilt/Zoom) via DirectShow/IAMCameraControl.
- `PTZControlGUI` (C# WinForms): einfache GUI zum Testen (Kamera wählen, PTZ setzen).
- `PTZControlConsole` (C# Console): CLI: `--list` und `--camera ... --pan/--tilt/--zoom`.
- `PTZControlBridge` (C++/CLI DLL): Forwarder, der aktuell auf `PTZControl.Uvc` aufsetzt. Später kann hier die Logitech-spezifische Logik implementiert werden.

## Build
- Visual Studio 2022, .NET 8, Windows 10 SDK (19041) oder neuer.
- Erst `PTZControl.Uvc` bauen (NuGet `DirectShowLib` wird wiederhergestellt).
- Danach `PTZControlGUI` / `PTZControlConsole` / `PTZControlBridge` bauen.

> Hinweis: Der C++/CLI-Projektpfad referenziert die Debug-Ausgabe von `PTZControl.Uvc`. Passen Sie ggf. den Pfad im `.vcxproj` an die gewünschte Konfiguration an.

## Nutzung
### Console
```powershell
PTZControlConsole --list
PTZControlConsole --camera "Rally" --pan 0 --tilt 50 --zoom 100
```

### GUI
- Starten, Kamera im Dropdown wählen, Regler anpassen, **Anwenden**.

## Weiterer Plan
- Logitech-„Motion Control“ & Presets im `PTZControlBridge` nativ implementieren.
- Danach die GUI/CLI statt `PTZControl.Uvc` direkt die Bridge verwenden lassen.

> Lizenz: Achten Sie auf GPL-3.0 des Upstreams, falls Sie den Code übernehmen/veröffentlichen.