using System;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PTZControl.Uvc;
using PTZControlConsole;
using UvcCameraProperty = PTZControl.Uvc.CameraProperty;

class Program
{
    private static readonly ICameraBackend CameraBackend = CameraBackendFactory.Create();

    static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (NotSupportedException ex)
        {
            return Fail(ex, 3);
        }
        catch (Exception ex)
        {
            return Fail(ex, 2);
        }
    }

    static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "list-devices":
                EnsureNoOptions(ParseOptions(args[1..]), "list-devices");
                return ListDevices();
            case "cam-device-info":
            {
                var options = ParseOptions(args[1..]);
                return PrintCameraDeviceInfo(ResolveCamera(options));
            }
            case "list-presets":
            {
                var options = ParseOptions(args[1..]);
                return ListPresets(ResolveCamera(options));
            }
            case "set-preset-name":
            {
                var options = ParseOptions(args[2..]);
                return SetPresetName(options, ParsePreset(args, 1));
            }
            case "swap-preset-names":
            {
                var options = ParseOptions(args[1..]);
                return SwapPresetNames(RequireSlot(options.SlotA, "--slot-a"), RequireSlot(options.SlotB, "--slot-b"));
            }
            case "restore-home":
            {
                var options = ParseOptions(args[1..]);
                var target = RequireTarget(options, "restore-home");
                return RestoreHome(ResolveCamera(options), target);
            }
            case "restore-default":
            {
                var options = ParseOptions(args[1..]);
                var target = RequireTarget(options, "restore-default");
                return RestoreDefault(ResolveCamera(options), target);
            }
            case "restore-preset":
            {
                var options = ParseOptions(args[2..]);
                CameraBackend.RestorePreset(ResolveCamera(options), ParsePreset(args, 1));
                return Ok();
            }
            case "save-preset":
            {
                var options = ParseOptions(args[2..]);
                WarnUnsupportedPresetName(options);
                CameraBackend.SavePreset(ResolveCamera(options), ParsePreset(args, 1));
                return Ok();
            }
            case "zoom-absolute":
            {
                var options = ParseOptions(args[2..]);
                return SetAbsoluteZoom(ResolveCamera(options), ParseValue(args, 1), RequireMode(options, "zoom-absolute"));
            }
            case "zoom-relative":
            {
                var options = ParseOptions(args[2..]);
                return SetRelativeZoom(ResolveCamera(options), ParseValue(args, 1), RequireMode(options, "zoom-relative"));
            }
            case "move-absolute":
            {
                var options = ParseOptions(args[1..]);
                return MoveAbsolute(ResolveCamera(options), options, RequireMode(options, "move-absolute"));
            }
            case "move-relative":
            {
                var options = ParseOptions(args[1..]);
                return MoveRelative(ResolveCamera(options), options, RequireMode(options, "move-relative"));
            }
            default:
                throw new ArgumentException($"Unknown command '{args[0]}'.");
        }
    }

    static int Fail(Exception ex, int exitCode)
    {
        Console.Error.WriteLine($"Error: {GetErrorMessage(ex)}");
        if (ex.InnerException is not null)
            Console.Error.WriteLine($"Cause: {GetErrorMessage(ex.InnerException)}");
        return exitCode;
    }

    static string GetErrorMessage(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;

    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  PTZControlConsole list-devices");
        Console.WriteLine("  PTZControlConsole cam-device-info [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole list-presets [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole set-preset-name 1..8 --name \"Title\" [--camera \"NamePart\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole swap-preset-names --slot-a 1..3 --slot-b 1..3");
        Console.WriteLine("  PTZControlConsole restore-home --target zoom|move|all [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole restore-default --target zoom|move|move-x|move-y|all [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole restore-preset 1..8 [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole save-preset 1..8 [--camera \"NamePart\"] [--name \"Title\"]");
        Console.WriteLine("  PTZControlConsole zoom-absolute VALUE --mode percent|raw [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole zoom-relative VALUE_DELTA --mode percent|raw [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole move-absolute --mode percent|raw [--x VALUE] [--y VALUE] [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole move-relative --mode percent|raw [--x VALUE_DELTA] [--y VALUE_DELTA] [--camera \"NamePart\"]");
    }

    static int ListDevices()
    {
        foreach (var cam in CameraBackend.Enumerate())
            Console.WriteLine(string.IsNullOrWhiteSpace(cam.MonikerString) ? cam.Name : $"{cam.Name}\t{cam.MonikerString}");
        return 0;
    }

    static int SetPresetName(Options options, int preset)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
            throw new ArgumentException("set-preset-name requires --name.");

        var slotIndex = ResolvePresetNameSlot(options);
        WritePresetName(slotIndex, preset, options.Name);
        return Ok();
    }

    static int SwapPresetNames(int slotA, int slotB)
    {
        if (slotA == slotB)
            throw new ArgumentException("--slot-a and --slot-b must be different.");

        EnsurePresetNamesSupported();

        for (var preset = 1; preset <= 8; preset++)
        {
            var nameA = ReadPresetName(slotA - 1, preset);
            var nameB = ReadPresetName(slotB - 1, preset);
            WritePresetName(slotA - 1, preset, nameB ?? "");
            WritePresetName(slotB - 1, preset, nameA ?? "");
        }

        return Ok();
    }

    static int ListPresets(string camera)
    {
        var (cameraName, slotIndex) = ResolveCameraSlot(camera);
        Console.WriteLine($"Camera Device Name: {cameraName}");
        if (slotIndex is not null)
            Console.WriteLine($"PTZControl app camera slot: {slotIndex.Value + 1}");
        else
            Console.WriteLine("PTZControl app camera slot: not available");
        Console.WriteLine("Preset storage: camera Logitech extension unit");
        Console.WriteLine("Preset values: not readable by the known PTZControl Logitech extension-unit API");
        Console.WriteLine("Preset names: PTZControl app-side tooltip metadata");

        for (var preset = 1; preset <= 8; preset++)
        {
            var name = slotIndex is null ? null : ReadPresetName(slotIndex.Value, preset);
            var displayName = string.IsNullOrWhiteSpace(name) ? "(none)" : name;
            Console.WriteLine($"* Preset {preset}: name={displayName}; values=not readable");
        }

        return 0;
    }

    static int PrintCameraDeviceInfo(string camera)
    {
        var match = CameraBackend.Enumerate()
            .FirstOrDefault(cam => cam.Name.Contains(camera, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"Camera Device Name: {match?.Name ?? camera}");
        if (!string.IsNullOrWhiteSpace(match?.MonikerString))
            Console.WriteLine($"Camera Device Path: {match.MonikerString}");
        Console.WriteLine("CLI absolute percent range: 0..100");
        PrintPropertyInfo("Zoom", camera, UvcCameraProperty.Zoom);
        PrintPropertyInfo("Move X axis", camera, UvcCameraProperty.Pan);
        PrintPropertyInfo("Move Y axis", camera, UvcCameraProperty.Tilt);
        Console.WriteLine("Home restore targets: zoom, move, all");
        Console.WriteLine("Default restore targets: zoom, move, move-x, move-y, all");
        Console.WriteLine("Preset range: restore 1..8, save 1..8");
        Console.WriteLine("Preset values: not readable by the known PTZControl Logitech extension-unit API");
        Console.WriteLine("Preset names: PTZControl app-side tooltip metadata, per camera slot");
        return 0;
    }

    static void PrintPropertyInfo(string label, string camera, UvcCameraProperty property)
    {
        try
        {
            var range = CameraBackend.GetRange(camera, property);
            Console.WriteLine($"* {label} raw min/max: {range.min}/{range.max}");
            Console.WriteLine($"  {label} raw default: {range.def}");
            Console.WriteLine($"  {label} raw step: {range.step}");
            PrintCurrentPropertyValue(label, camera, property);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"* {label}: not available ({GetErrorMessage(ex)})");
            if (ex.InnerException is not null)
                Console.WriteLine($"  Cause: {GetErrorMessage(ex.InnerException)}");
        }
    }

    static void PrintCurrentPropertyValue(string label, string camera, UvcCameraProperty property)
    {
        try
        {
            var current = CameraBackend.GetValue(camera, property);
            Console.WriteLine($"  {label} raw current: {current}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {label} raw current: not available ({GetErrorMessage(ex)})");
            if (ex.InnerException is not null)
                Console.WriteLine($"  Cause: {GetErrorMessage(ex.InnerException)}");
        }
    }

    static void EnsureNoOptions(Options options, string command)
    {
        if (options.HasAnyValue)
            throw new ArgumentException($"{command} does not accept options.");
    }

    static int RestoreHome(string camera, Target target)
    {
        var (zoom, move) = target switch
        {
            Target.Zoom => (true, false),
            Target.Move => (false, true),
            Target.All => (true, true),
            Target.MoveX or Target.MoveY => throw new ArgumentException("restore-home supports --target zoom, move, or all. Separate move-x/move-y home restore is not supported by the Logitech home command."),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

        CameraBackend.RestoreHome(camera, zoom, move);
        return Ok();
    }

    static int RestoreDefault(string camera, Target target)
    {
        var (zoom, pan, tilt) = target switch
        {
            Target.Zoom => (true, false, false),
            Target.Move => (false, true, true),
            Target.MoveX => (false, true, false),
            Target.MoveY => (false, false, true),
            Target.All => (true, true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

        CameraBackend.RestoreDefault(camera, zoom, pan, tilt);
        return Ok();
    }

    static int SetAbsoluteZoom(string camera, int value, ValueMode mode)
    {
        var zoom = mode == ValueMode.Percent
            ? ScalePercentToValue(camera, UvcCameraProperty.Zoom, value)
            : ClampRawValue(camera, UvcCameraProperty.Zoom, value);
        CameraBackend.SetPanTiltZoom(camera, zoom: zoom);
        return Ok();
    }

    static int SetRelativeZoom(string camera, int delta, ValueMode mode)
    {
        var value = mode == ValueMode.Percent
            ? AddPercentDelta(camera, UvcCameraProperty.Zoom, delta)
            : AddRawDelta(camera, UvcCameraProperty.Zoom, delta);
        CameraBackend.SetPanTiltZoom(camera, zoom: value);
        return Ok();
    }

    static int MoveAbsolute(string camera, Options options, ValueMode mode)
    {
        var pan = options.X is null ? (int?)null : ConvertAbsoluteValue(camera, UvcCameraProperty.Pan, options.X.Value, mode);
        var tilt = options.Y is null ? (int?)null : ConvertAbsoluteValue(camera, UvcCameraProperty.Tilt, options.Y.Value, mode);
        if (pan is null && tilt is null) throw new ArgumentException("move-absolute requires --x and/or --y.");
        CameraBackend.SetPanTiltZoom(camera, pan, tilt);
        return Ok();
    }

    static int MoveRelative(string camera, Options options, ValueMode mode)
    {
        if (options.X is null && options.Y is null) throw new ArgumentException("move-relative requires --x and/or --y.");
        if (mode == ValueMode.Percent)
        {
            CameraBackend.MoveRelativePanTilt(camera, options.X, options.Y);
        }
        else
        {
            var pan = options.X is null ? (int?)null : AddRawDelta(camera, UvcCameraProperty.Pan, options.X.Value);
            var tilt = options.Y is null ? (int?)null : AddRawDelta(camera, UvcCameraProperty.Tilt, options.Y.Value);
            CameraBackend.SetPanTiltZoom(camera, pan, tilt);
        }
        return Ok();
    }

    static int Ok()
    {
        Console.WriteLine("OK");
        return 0;
    }

    static string ResolveCamera(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.Camera))
            return options.Camera;

        var cameras = CameraBackend.Enumerate();
        if (cameras.Count == 0)
            throw new InvalidOperationException("No camera found.");
        return cameras[0].Name;
    }

    static (string cameraName, int? slotIndex) ResolveCameraSlot(string camera)
    {
        var cameras = CameraBackend.Enumerate();
        for (var i = 0; i < cameras.Count; i++)
        {
            if (cameras[i].Name.Contains(camera, StringComparison.OrdinalIgnoreCase))
                return (cameras[i].Name, i < 3 ? i : null);
        }

        return (camera, null);
    }

    static int ResolvePresetNameSlot(Options options)
    {
        if (options.Slot.HasValue && !string.IsNullOrWhiteSpace(options.Camera))
            throw new ArgumentException("Use either --slot or --camera, not both.");

        if (options.Slot.HasValue)
            return RequireSlot(options.Slot, "--slot") - 1;

        var camera = ResolveCamera(options);
        var (_, slotIndex) = ResolveCameraSlot(camera);
        return slotIndex ?? throw new InvalidOperationException($"Camera '{camera}' is not mapped to a PTZControl app slot 1..3.");
    }

    static int RequireSlot(int? slot, string optionName)
    {
        if (!slot.HasValue)
            throw new ArgumentException($"{optionName} is required.");
        if (slot.Value < 1 || slot.Value > 3)
            throw new ArgumentOutOfRangeException(optionName, "Slot must be between 1 and 3.");
        return slot.Value;
    }

    static string? ReadPresetName(int cameraSlotIndex, int preset)
    {
        if (OperatingSystem.IsWindows())
            return ReadPresetNameFromRegistry(cameraSlotIndex, preset);

        throw PresetNamesNotSupported();
    }

    static void WritePresetName(int cameraSlotIndex, int preset, string name)
    {
        if (OperatingSystem.IsWindows())
        {
            WritePresetNameToRegistry(cameraSlotIndex, preset, name);
            return;
        }

        throw PresetNamesNotSupported();
    }

    [SupportedOSPlatform("windows")]
    static string? ReadPresetNameFromRegistry(int cameraSlotIndex, int preset)
    {
        var valueName = $"Tooltip{preset + cameraSlotIndex * 100}";
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\MRi-Software\PTZControl\Window");
        return key?.GetValue(valueName) as string;
    }

    [SupportedOSPlatform("windows")]
    static void WritePresetNameToRegistry(int cameraSlotIndex, int preset, string name)
    {
        var valueName = $"Tooltip{preset + cameraSlotIndex * 100}";
        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\MRi-Software\PTZControl\Window");
        key.SetValue(valueName, name, RegistryValueKind.String);
    }

    static void EnsurePresetNamesSupported()
    {
        if (!OperatingSystem.IsWindows())
            throw PresetNamesNotSupported();
    }

    static NotSupportedException PresetNamesNotSupported() =>
        new("PTZControl preset names are stored in the Windows registry and are only supported on Windows.");

    static int ScalePercentToValue(string camera, UvcCameraProperty property, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var range = CameraBackend.GetRange(camera, property);
        return range.min + (int)Math.Round((range.max - range.min) * (percent / 100.0));
    }

    static int AddPercentDelta(string camera, UvcCameraProperty property, int deltaPercent)
    {
        var range = CameraBackend.GetRange(camera, property);
        var current = CameraBackend.GetValue(camera, property);
        var delta = (int)Math.Round((range.max - range.min) * (deltaPercent / 100.0));
        return Math.Clamp(current + delta, range.min, range.max);
    }

    static int ClampRawValue(string camera, UvcCameraProperty property, int value)
    {
        var range = CameraBackend.GetRange(camera, property);
        return Math.Clamp(value, range.min, range.max);
    }

    static int AddRawDelta(string camera, UvcCameraProperty property, int delta)
    {
        var range = CameraBackend.GetRange(camera, property);
        var current = CameraBackend.GetValue(camera, property);
        return Math.Clamp(current + delta, range.min, range.max);
    }

    static int ConvertAbsoluteValue(string camera, UvcCameraProperty property, int value, ValueMode mode) =>
        mode == ValueMode.Percent
            ? ScalePercentToValue(camera, property, value)
            : ClampRawValue(camera, property, value);

    static int ParsePreset(string[] args, int index)
    {
        if (args.Length <= index || !int.TryParse(args[index], out var preset))
            throw new ArgumentException("Preset command requires a preset number.");
        if (preset < 1 || preset > 8)
            throw new ArgumentOutOfRangeException(nameof(preset), "Preset number must be between 1 and 8. Use restore-home for the Logitech home position.");
        return preset;
    }

    static int ParseValue(string[] args, int index)
    {
        if (args.Length <= index || !int.TryParse(args[index], out var value))
            throw new ArgumentException("Command requires a numeric value.");
        return value;
    }

    static ValueMode RequireMode(Options options, string command)
    {
        if (options.Mode is null)
            throw new ArgumentException($"{command} requires --mode percent or --mode raw.");
        return options.Mode.Value;
    }

    static Target RequireTarget(Options options, string command)
    {
        if (options.Target is null)
            throw new ArgumentException($"{command} requires --target.");
        return options.Target.Value;
    }

    static void WarnUnsupportedPresetName(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.Name))
            Console.Error.WriteLine("Preset names are not supported yet; ignoring --name.");
    }

    static Options ParseOptions(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--camera":
                    options.Camera = ReadValue(args, ref i);
                    break;
                case "--name":
                    options.Name = ReadValue(args, ref i);
                    break;
                case "--x":
                    options.X = int.Parse(ReadValue(args, ref i));
                    break;
                case "--y":
                    options.Y = int.Parse(ReadValue(args, ref i));
                    break;
                case "--mode":
                    options.Mode = ParseMode(ReadValue(args, ref i));
                    break;
                case "--target":
                    options.Target = ParseTarget(ReadValue(args, ref i));
                    break;
                case "--slot":
                    options.Slot = int.Parse(ReadValue(args, ref i));
                    break;
                case "--slot-a":
                    options.SlotA = int.Parse(ReadValue(args, ref i));
                    break;
                case "--slot-b":
                    options.SlotB = int.Parse(ReadValue(args, ref i));
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }
        return options;
    }

    static string ReadValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{args[index]} requires a value.");
        index++;
        return args[index];
    }

    static ValueMode ParseMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "percent" => ValueMode.Percent,
            "raw" => ValueMode.Raw,
            _ => throw new ArgumentException("--mode must be 'percent' or 'raw'.")
        };

    enum ValueMode
    {
        Percent,
        Raw
    }

    static Target ParseTarget(string value) =>
        value.ToLowerInvariant() switch
        {
            "zoom" => Target.Zoom,
            "move" => Target.Move,
            "move-x" => Target.MoveX,
            "move-y" => Target.MoveY,
            "all" => Target.All,
            _ => throw new ArgumentException("--target must be 'zoom', 'move', 'move-x', 'move-y', or 'all'.")
        };

    enum Target
    {
        Zoom,
        Move,
        MoveX,
        MoveY,
        All
    }

    sealed class Options
    {
        public string? Camera { get; set; }
        public string? Name { get; set; }
        public ValueMode? Mode { get; set; }
        public Target? Target { get; set; }
        public int? Slot { get; set; }
        public int? SlotA { get; set; }
        public int? SlotB { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool HasAnyValue =>
            !string.IsNullOrWhiteSpace(Camera) ||
            !string.IsNullOrWhiteSpace(Name) ||
            Mode.HasValue ||
            Target.HasValue ||
            Slot.HasValue ||
            SlotA.HasValue ||
            SlotB.HasValue ||
            X.HasValue ||
            Y.HasValue;
    }
}
