using System;
using PTZControl.Uvc;
using UvcCameraProperty = PTZControl.Uvc.CameraProperty;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args[1..]);

            switch (command)
            {
                case "list-devices":
                case "--list":
                    return ListDevices();
                case "restore-preset":
                    _ = ResolveCamera(options);
                    _ = ParsePreset(args, 1);
                    throw new NotSupportedException("Logitech preset recall is not implemented yet.");
                case "save-preset":
                    _ = ResolveCamera(options);
                    _ = ParsePreset(args, 1);
                    WarnUnsupportedPresetName(options);
                    throw new NotSupportedException("Logitech preset save is not implemented yet.");
                case "zoom-absolute":
                    return SetAbsoluteZoom(ResolveCamera(options), ParsePercent(args, 1));
                case "zoom-relative":
                    return SetRelativeZoom(ResolveCamera(options), ParsePercent(args, 1));
                case "move-absolute":
                    return MoveAbsolute(ResolveCamera(options), options);
                case "move-relative":
                    return MoveRelative(ResolveCamera(options), options);
                default:
                    return LegacyCommand(args);
            }
        }
        catch (NotSupportedException nse)
        {
            Console.Error.WriteLine(nse.Message);
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  PTZControlConsole list-devices");
        Console.WriteLine("  PTZControlConsole restore-preset N [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole save-preset N [--camera \"NamePart\"] [--name \"Title\"]");
        Console.WriteLine("  PTZControlConsole zoom-absolute PERCENT [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole zoom-relative PERCENT_DELTA [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole move-absolute [--x PERCENT] [--y PERCENT] [--camera \"NamePart\"]");
        Console.WriteLine("  PTZControlConsole move-relative [--x PERCENT_DELTA] [--y PERCENT_DELTA] [--camera \"NamePart\"]");
        Console.WriteLine();
        Console.WriteLine("Legacy:");
        Console.WriteLine("  PTZControlConsole --list");
        Console.WriteLine("  PTZControlConsole --camera \"NamePart\" [--pan N] [--tilt N] [--zoom N]");
        Console.WriteLine("  PTZControlConsole --preset --camera \"NamePart\" --save N|--recall N");
    }

    static int ListDevices()
    {
        foreach (var cam in UvcCamera.Enumerate())
            Console.WriteLine(string.IsNullOrWhiteSpace(cam.MonikerString) ? cam.Name : $"{cam.Name}\t{cam.MonikerString}");
        return 0;
    }

    static int SetAbsoluteZoom(string camera, int percent)
    {
        var zoom = ScalePercentToValue(camera, UvcCameraProperty.Zoom, percent);
        UvcCamera.SetPanTiltZoom(camera, zoom: zoom);
        return Ok();
    }

    static int SetRelativeZoom(string camera, int deltaPercent)
    {
        var value = AddPercentDelta(camera, UvcCameraProperty.Zoom, deltaPercent);
        UvcCamera.SetPanTiltZoom(camera, zoom: value);
        return Ok();
    }

    static int MoveAbsolute(string camera, Options options)
    {
        var pan = options.X is null ? (int?)null : ScalePercentToValue(camera, UvcCameraProperty.Pan, options.X.Value);
        var tilt = options.Y is null ? (int?)null : ScalePercentToValue(camera, UvcCameraProperty.Tilt, options.Y.Value);
        if (pan is null && tilt is null) throw new ArgumentException("move-absolute requires --x and/or --y.");
        UvcCamera.SetPanTiltZoom(camera, pan, tilt);
        return Ok();
    }

    static int MoveRelative(string camera, Options options)
    {
        var pan = options.X is null ? (int?)null : AddPercentDelta(camera, UvcCameraProperty.Pan, options.X.Value);
        var tilt = options.Y is null ? (int?)null : AddPercentDelta(camera, UvcCameraProperty.Tilt, options.Y.Value);
        if (pan is null && tilt is null) throw new ArgumentException("move-relative requires --x and/or --y.");
        UvcCamera.SetPanTiltZoom(camera, pan, tilt);
        return Ok();
    }

    static int LegacyCommand(string[] args)
    {
        if (Array.Exists(args, a => a.Equals("--list", StringComparison.OrdinalIgnoreCase)))
            return ListDevices();

        var options = ParseOptions(args);
        var camera = ResolveCamera(options);

        if (options.Preset)
        {
            if (options.Save.HasValue) throw new NotSupportedException("Logitech preset save is not implemented yet.");
            else if (options.Recall.HasValue) throw new NotSupportedException("Logitech preset recall is not implemented yet.");
            else throw new ArgumentException("--preset requires --save N or --recall N.");
        }
        else
        {
            if (options.Pan is null && options.Tilt is null && options.Zoom is null)
                throw new ArgumentException("Unknown command or missing PTZ arguments.");
            UvcCamera.SetPanTiltZoom(camera, options.Pan, options.Tilt, options.Zoom);
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

        var cameras = UvcCamera.Enumerate();
        if (cameras.Count == 0)
            throw new InvalidOperationException("No camera found.");
        return cameras[0].Name;
    }

    static int ScalePercentToValue(string camera, UvcCameraProperty property, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var range = UvcCamera.GetRange(camera, property);
        return range.min + (int)Math.Round((range.max - range.min) * (percent / 100.0));
    }

    static int AddPercentDelta(string camera, UvcCameraProperty property, int deltaPercent)
    {
        var range = UvcCamera.GetRange(camera, property);
        var current = UvcCamera.GetValue(camera, property);
        var delta = (int)Math.Round((range.max - range.min) * (deltaPercent / 100.0));
        return Math.Clamp(current + delta, range.min, range.max);
    }

    static int ParsePreset(string[] args, int index)
    {
        if (args.Length <= index || !int.TryParse(args[index], out var preset))
            throw new ArgumentException("Preset command requires a preset number.");
        return preset;
    }

    static int ParsePercent(string[] args, int index)
    {
        if (args.Length <= index || !int.TryParse(args[index], out var percent))
            throw new ArgumentException("Command requires a numeric percent value.");
        return percent;
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
                case "-camera":
                    options.Camera = ReadValue(args, ref i);
                    break;
                case "--name":
                case "-name":
                    options.Name = ReadValue(args, ref i);
                    break;
                case "--x":
                case "-x":
                    options.X = int.Parse(ReadValue(args, ref i));
                    break;
                case "--y":
                case "-y":
                    options.Y = int.Parse(ReadValue(args, ref i));
                    break;
                case "--pan":
                    options.Pan = int.Parse(ReadValue(args, ref i));
                    break;
                case "--tilt":
                    options.Tilt = int.Parse(ReadValue(args, ref i));
                    break;
                case "--zoom":
                    options.Zoom = int.Parse(ReadValue(args, ref i));
                    break;
                case "--preset":
                    options.Preset = true;
                    break;
                case "--save":
                    options.Save = int.Parse(ReadValue(args, ref i));
                    break;
                case "--recall":
                    options.Recall = int.Parse(ReadValue(args, ref i));
                    break;
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

    sealed class Options
    {
        public string? Camera { get; set; }
        public string? Name { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Pan { get; set; }
        public int? Tilt { get; set; }
        public int? Zoom { get; set; }
        public bool Preset { get; set; }
        public int? Save { get; set; }
        public int? Recall { get; set; }
    }
}
