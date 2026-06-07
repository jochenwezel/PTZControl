using System;
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
        Console.WriteLine("  PTZControlConsole restore-preset 0..8 [--camera \"NamePart\"]");
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
        Console.WriteLine("Preset range: restore 0..8, save 1..8");
        return 0;
    }

    static void PrintPropertyInfo(string label, string camera, UvcCameraProperty property)
    {
        try
        {
            var range = CameraBackend.GetRange(camera, property);
            Console.WriteLine($"* {label} raw min/max: {range.min}/{range.max}");
            Console.WriteLine($"  {label} raw default/step: {range.def}/{range.step}");
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

    sealed class Options
    {
        public string? Camera { get; set; }
        public string? Name { get; set; }
        public ValueMode? Mode { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool HasAnyValue =>
            !string.IsNullOrWhiteSpace(Camera) ||
            !string.IsNullOrWhiteSpace(Name) ||
            Mode.HasValue ||
            X.HasValue ||
            Y.HasValue;
    }
}
