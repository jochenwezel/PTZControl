using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
using CommandLine.Text;
using Microsoft.Win32;
using PTZControl.Uvc;
using PTZControlConsole;
using UvcCameraProperty = PTZControl.Uvc.CameraProperty;

class Program
{
    private static readonly ICameraBackend CameraBackend = CameraBackendFactory.Create();
    private static readonly Type[] PublicVerbTypes =
    {
        typeof(ListDevicesOptions),
        typeof(CamDeviceInfoOptions),
        typeof(GetPresetNameOptions),
        typeof(SetPresetNameOptions),
        typeof(ClearPresetNameOptions),
        typeof(GetCameraNameOptions),
        typeof(SetCameraNameOptions),
        typeof(ClearCameraNameOptions),
        typeof(GetDirectShowCameraNameOptions),
        typeof(SetDirectShowCameraNameOptions),
        typeof(SwapPresetNamesOptions),
        typeof(ConfigOptions),
        typeof(RestoreHomeOptions),
        typeof(RestoreDefaultOptions),
        typeof(RestorePresetOptions),
        typeof(SavePresetOptions),
        typeof(ZoomAbsoluteOptions),
        typeof(ZoomRelativeOptions),
        typeof(MoveAbsoluteOptions),
        typeof(MoveSeekOptions),
        typeof(MoveRelativeOptions)
    };
    private static readonly Type[] AllVerbTypes = PublicVerbTypes.Concat(new[] { typeof(DocsOptions) }).ToArray();
    private static readonly Parser RuntimeParser = new(settings =>
    {
        settings.HelpWriter = null;
        settings.AutoHelp = true;
        settings.AutoVersion = true;
    });
    private static readonly Parser DocumentationParser = new(settings => settings.HelpWriter = null);

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
            Console.Error.Write(RenderMainHelp());
            return 0;
        }

        return RuntimeParser.ParseArguments(args, AllVerbTypes)
            .MapResult(RunParsed, errors => PrintParserHelp(args));
    }

    static int PrintParserHelp(string[] args)
    {
        if (args.Length == 1 && IsMainHelpRequest(args[0]))
        {
            Console.Error.Write(RenderMainHelp());
            return 0;
        }

        if (args.Length == 2 && args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.Write(CaptureParserHelp(new[] { args[1], "--help" }));
            return 0;
        }

        if (args.Length == 1 && args[0].Equals("version", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(GetVersionLine());
            return 0;
        }

        Console.Error.Write(CaptureParserHelp(args));
        return 1;
    }

    static bool IsMainHelpRequest(string arg) =>
        arg.Equals("help", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--h", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-?", StringComparison.OrdinalIgnoreCase);

    static int RunParsed(object parsed) =>
        parsed switch
        {
            ListDevicesOptions => ListDevices(),
            CamDeviceInfoOptions options => PrintCameraDeviceInfo(ResolveCamera(ToOptions(options))),
            GetPresetNameOptions options => GetPresetName(ToOptions(options), ParsePreset(options.Preset)),
            SetPresetNameOptions options => SetPresetName(ToOptions(options), ParsePreset(options.Preset)),
            ClearPresetNameOptions options => ClearPresetName(ToOptions(options), ParsePreset(options.Preset)),
            GetCameraNameOptions options => GetCameraName(ToOptions(options)),
            SetCameraNameOptions options => SetCameraName(ToOptions(options)),
            ClearCameraNameOptions options => ClearCameraName(ToOptions(options)),
            GetDirectShowCameraNameOptions options => GetDirectShowCameraName(ToOptions(options)),
            SetDirectShowCameraNameOptions options => SetDirectShowCameraName(ToOptions(options), options.AcknowledgeWarning),
            SwapPresetNamesOptions options => SwapPresetNames(RequireSlot(options.SlotA, "--slot-a"), RequireSlot(options.SlotB, "--slot-b")),
            ConfigOptions options => RunConfig(ToOptions(options)),
            RestoreHomeOptions options => RestoreHome(ResolveCamera(ToOptions(options)), ParseTarget(options.TargetName)),
            RestoreDefaultOptions options => RestoreDefault(ResolveCamera(ToOptions(options)), ParseTarget(options.TargetName)),
            RestorePresetOptions options =>
                RunAndOk(() => CameraBackend.RestorePreset(ResolveCamera(ToOptions(options)), ParsePreset(options.Preset))),
            SavePresetOptions options =>
                RunAndOk(() =>
                {
                    var internalOptions = ToOptions(options);
                    WarnUnsupportedPresetName(internalOptions);
                    CameraBackend.SavePreset(ResolveCamera(internalOptions), ParsePreset(options.Preset));
                }),
            ZoomAbsoluteOptions options => SetAbsoluteZoom(ResolveCamera(ToOptions(options)), options.Value, ParseMode(options.ModeName)),
            ZoomRelativeOptions options => SetRelativeZoom(ResolveCamera(ToOptions(options)), options.ValueDelta, ParseMode(options.ModeName)),
            MoveAbsoluteOptions options => MoveAbsolute(ResolveCamera(ToOptions(options)), ToOptions(options), ParseMode(options.ModeName)),
            MoveSeekOptions options => MoveSeek(ResolveCamera(ToOptions(options)), ToOptions(options), ParseMode(options.ModeName), options),
            MoveRelativeOptions options => MoveRelative(ResolveCamera(ToOptions(options)), ToOptions(options), ParseMode(options.ModeName)),
            DocsOptions options => GenerateDocs(options),
            _ => throw new ArgumentException("Unknown command.")
        };

    static int RunAndOk(Action action)
    {
        action();
        return Ok();
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

    static int ListDevices()
    {
        foreach (var cam in CameraBackend.Enumerate())
            Console.WriteLine(string.IsNullOrWhiteSpace(cam.MonikerString) ? cam.Name : $"{cam.Name}\t{cam.MonikerString}");
        return 0;
    }

    static int GenerateDocs(DocsOptions options)
    {
        var output = string.IsNullOrWhiteSpace(options.OutputPath)
            ? Path.Combine("docs", "generated")
            : options.OutputPath;

        var files = new Dictionary<string, string>
        {
            ["cli-help.md"] = GenerateCliHelpMarkdown(),
            ["example-output.md"] = GenerateExampleOutputMarkdown()
        };

        if (options.Check)
        {
            var mismatches = files
                .Where(file => !File.Exists(Path.Combine(output, file.Key)) || File.ReadAllText(Path.Combine(output, file.Key)) != file.Value)
                .Select(file => file.Key)
                .ToList();

            if (mismatches.Count == 0)
                return 0;

            Console.Error.WriteLine("Generated documentation is out of date:");
            foreach (var mismatch in mismatches)
                Console.Error.WriteLine($"  {Path.Combine(output, mismatch)}");
            return 1;
        }

        Directory.CreateDirectory(output);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(output, file.Key), file.Value);

        return 0;
    }

    static string GenerateCliHelpMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# PTZControlConsole CLI Help");
        builder.AppendLine();
        builder.AppendLine("This file is generated by `PTZControlConsole docs --output docs/generated`.");
        builder.AppendLine();
        AppendMainHelpBlock(builder);
        foreach (var verb in PublicVerbTypes.Select(GetVerbName))
            AppendHelpBlock(builder, verb, new[] { verb, "--help" });
        return builder.ToString();
    }

    static string GetVerbName(Type verbType) =>
        verbType.GetCustomAttribute<VerbAttribute>()?.Name
        ?? throw new InvalidOperationException($"Missing VerbAttribute on {verbType.FullName}.");

    static void AppendMainHelpBlock(StringBuilder builder)
    {
        var help = new HelpText
        {
            Heading = GetVersionLine(),
            Copyright = ""
        };
        help.AddVerbs(PublicVerbTypes);

        builder.AppendLine("## Main help");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.Append(RenderMainHelp(forDocumentation: true));
        builder.AppendLine("```");
        builder.AppendLine();
    }

    static string RenderMainHelp(bool forDocumentation = false)
    {
        var help = new HelpText
        {
            Heading = forDocumentation ? GetDocumentationVersionLine() : GetVersionLine(),
            Copyright = ""
        };
        help.AddVerbs(PublicVerbTypes);
        return help.ToString();
    }

    static string GetVersionLine()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return $"PTZControlConsole {informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown"}";
    }

    static string GetDocumentationVersionLine()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return $"PTZControlConsole {assembly.GetName().Version?.ToString() ?? "VERSION"}";
    }

    static void AppendHelpBlock(StringBuilder builder, string title, string[] args)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.Append(CaptureParserHelp(args, forDocumentation: true));
        builder.AppendLine("```");
        builder.AppendLine();
    }

    static string CaptureParserHelp(string[] args, bool forDocumentation = false)
    {
        var result = DocumentationParser.ParseArguments(args, AllVerbTypes);
        var text = HelpText.AutoBuild(result, help =>
        {
            help.Heading = forDocumentation ? GetDocumentationVersionLine() : GetVersionLine();
            help.Copyright = "";
            return HelpText.DefaultParsingErrorsHandler(result, help);
        }, example => example).ToString();
        return text;
    }

    static string GenerateExampleOutputMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# PTZControlConsole Example Output");
        builder.AppendLine();
        builder.AppendLine("This file is generated by `PTZControlConsole docs --output docs/generated`.");
        builder.AppendLine("The examples use stable sample data and do not query local camera hardware.");
        builder.AppendLine();
        AppendExampleBlock(builder, "list-devices", GenerateSampleListDevices());
        AppendExampleBlock(builder, "cam-device-info", GenerateSampleCamDeviceInfo());
        AppendExampleBlock(builder, "config --export", GenerateSampleConfigExport());
        return builder.ToString();
    }

    static void AppendExampleBlock(StringBuilder builder, string title, string content)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.Append(content);
        builder.AppendLine("```");
        builder.AppendLine();
    }

    static string GenerateSampleListDevices() =>
        "PTZ Pro 2\t@device:pnp:\\\\?\\usb#vid_046d&pid_085f&mi_00#sample\\global" + Environment.NewLine +
        "Logitech HD Webcam C525\t@device:pnp:\\\\?\\usb#vid_046d&pid_0826&mi_02#sample\\global" + Environment.NewLine;

    static string GenerateSampleCamDeviceInfo() =>
        """
        Camera:
          Device Name: PTZ Pro 2
          Device Path: @device:pnp:\\?\usb#vid_046d&pid_085f&mi_00#sample\global
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
        
        """;

    static string GenerateSampleConfigExport() =>
        """
        {
          "cameras": [
            {
              "slot": 1,
              "friendlyName": "Main camera",
              "lastDeviceName": "PTZ Pro 2",
              "lastDevicePath": "@device:pnp:\\\\?\\usb#vid_046d&pid_085f&mi_00#sample\\global",
              "presetNames": {
                "1": "Speaker",
                "2": "Stage"
              }
            }
          ]
        }
        
        """;

    static int GetPresetName(Options options, int preset)
    {
        var slotIndex = ResolvePresetNameSlot(options);
        Console.WriteLine(ReadPresetName(slotIndex, preset) ?? "");
        return 0;
    }

    static int SetPresetName(Options options, int preset)
    {
        if (string.IsNullOrWhiteSpace(options.FriendlyName))
            throw new ArgumentException("set-preset-name requires --friendlyname.");

        var slotIndex = ResolvePresetNameSlot(options);
        WritePresetName(slotIndex, preset, options.FriendlyName);
        return Ok();
    }

    static int ClearPresetName(Options options, int preset)
    {
        var slotIndex = ResolvePresetNameSlot(options);
        WritePresetName(slotIndex, preset, "");
        return Ok();
    }

    static int GetCameraName(Options options)
    {
        var slot = RequireSlot(options.Slot, "--slot");
        Console.WriteLine(ReadCameraSlotAlias(slot - 1) ?? "");
        return 0;
    }

    static int SetCameraName(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.FriendlyName))
            throw new ArgumentException("set-camera-name requires --friendlyname.");

        var slot = RequireSlot(options.Slot, "--slot");
        WriteCameraSlotAlias(slot - 1, options.FriendlyName);
        return Ok();
    }

    static int ClearCameraName(Options options)
    {
        var slot = RequireSlot(options.Slot, "--slot");
        WriteCameraSlotAlias(slot - 1, "");
        return Ok();
    }

    static int GetDirectShowCameraName(Options options)
    {
        var camera = ResolveSingleCameraInfo(options, requireExplicitSelector: true);
        Console.WriteLine(CameraBackend.GetDirectShowCameraName(camera.MonikerString));
        return 0;
    }

    static int SetDirectShowCameraName(Options options, bool acknowledgeWarning)
    {
        if (string.IsNullOrWhiteSpace(options.FriendlyName))
            throw new ArgumentException("set-directshow-camera-name requires --friendlyname.");

        var camera = ResolveSingleCameraInfo(options, requireExplicitSelector: true);
        if (!acknowledgeWarning)
            RequireDirectShowRenameConfirmation();

        try
        {
            CameraBackend.SetDirectShowCameraName(camera.MonikerString, options.FriendlyName);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Administrator rights are required to set the DirectShow camera name. Start the console as administrator or use an elevated shell.", ex);
        }
        catch (SecurityException ex)
        {
            throw new InvalidOperationException("Administrator rights are required to set the DirectShow camera name. Start the console as administrator or use an elevated shell.", ex);
        }

        return Ok();
    }

    static void RequireDirectShowRenameConfirmation()
    {
        Console.Error.WriteLine("Warning:");
        Console.Error.WriteLine(DirectShowRenameWarningText);
        Console.Error.Write("Type YES to continue: ");
        var answer = Console.ReadLine();
        if (!string.Equals(answer, "YES", StringComparison.Ordinal))
            throw new OperationCanceledException("DirectShow camera rename was not confirmed.");
    }

    static int RunConfig(Options options)
    {
        if (options.ExportConfig && !string.IsNullOrWhiteSpace(options.ImportConfigPath))
            throw new ArgumentException("Use either --export or --import, not both.");
        if (!options.ExportConfig && string.IsNullOrWhiteSpace(options.ImportConfigPath))
            throw new ArgumentException("config requires --export [json-path] or --import json-path.");

        if (options.ExportConfig)
        {
            var json = ExportMetadataConfig();
            if (string.IsNullOrWhiteSpace(options.ExportConfigPath))
                Console.WriteLine(json);
            else
                File.WriteAllText(options.ExportConfigPath, json);
            return 0;
        }

        ImportMetadataConfig(options.ImportConfigPath!);
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

    static int PrintCameraDeviceInfo(string camera)
    {
        var (match, slotIndex) = ResolveCameraInfo(camera);

        Console.WriteLine("Camera:");
        Console.WriteLine($"  Device Name: {match?.Name ?? camera}");
        if (!string.IsNullOrWhiteSpace(match?.MonikerString))
            Console.WriteLine($"  Device Path: {match.MonikerString}");
        if (slotIndex is not null)
        {
            Console.WriteLine($"  PTZControl Slot: {slotIndex.Value + 1}");
            var alias = TryReadCameraSlotAlias(slotIndex.Value);
            if (!string.IsNullOrWhiteSpace(alias))
                Console.WriteLine($"  Camera Slot Alias: {alias}");
        }
        Console.WriteLine();
        PrintPropertyInfo("Zoom", camera, UvcCameraProperty.Zoom);
        Console.WriteLine();
        PrintPropertyInfo("Move X axis", camera, UvcCameraProperty.Pan);
        Console.WriteLine();
        PrintPropertyInfo("Move Y axis", camera, UvcCameraProperty.Tilt);
        Console.WriteLine();
        Console.WriteLine("Available restore targets:");
        Console.WriteLine("  Home: zoom, move, all");
        Console.WriteLine("  Default: zoom, move, move-x, move-y, all");
        Console.WriteLine();
        Console.WriteLine("Presets:");
        Console.WriteLine("  Restore range: 1..8");
        Console.WriteLine("  Save range: 1..8");
        Console.WriteLine("  Storage: camera Logitech extension unit");
        PrintPresetNames(slotIndex, "  ");
        return 0;
    }

    static void PrintPresetNames(int? slotIndex, string indent)
    {
        for (var preset = 1; preset <= 8; preset++)
        {
            var name = slotIndex is null ? null : ReadPresetName(slotIndex.Value, preset);
            var displayName = string.IsNullOrWhiteSpace(name) ? "(none)" : name;
            Console.WriteLine($"{indent}Preset {preset}: name={displayName}; values=not readable");
        }
    }

    static void PrintPropertyInfo(string label, string camera, UvcCameraProperty property)
    {
        Console.WriteLine($"{label}:");
        Console.WriteLine("  Percent range: 0..100");
        try
        {
            var range = CameraBackend.GetRange(camera, property);
            Console.WriteLine($"  Raw min: {range.min}");
            Console.WriteLine($"  Raw max: {range.max}");
            Console.WriteLine($"  Raw default: {range.def}");
            Console.WriteLine($"  Raw step size: {range.step}");
            PrintCurrentPropertyValue(camera, property);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Raw: not available ({GetErrorMessage(ex)})");
            if (ex.InnerException is not null)
                Console.WriteLine($"  Cause: {GetErrorMessage(ex.InnerException)}");
        }
    }

    static void PrintCurrentPropertyValue(string camera, UvcCameraProperty property)
    {
        try
        {
            var current = CameraBackend.GetValue(camera, property);
            Console.WriteLine($"  Raw current: {current}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Raw current: not available ({GetErrorMessage(ex)})");
            if (ex.InnerException is not null)
                Console.WriteLine($"  Cause: {GetErrorMessage(ex.InnerException)}");
        }
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
        if (pan is null && tilt is null) throw new ArgumentException("move-absolute requires -x/--pan and/or -y/--tilt.");
        CameraBackend.SetPanTiltZoom(camera, pan, tilt);
        return Ok();
    }

    static int MoveSeek(string camera, Options options, ValueMode mode, MoveSeekOptions seekOptions)
    {
        if (options.X is null && options.Y is null) throw new ArgumentException("move-seek requires -x/--pan and/or -y/--tilt.");

        var panTarget = options.X is null ? (int?)null : ConvertAbsoluteValue(camera, UvcCameraProperty.Pan, options.X.Value, mode);
        var tiltTarget = options.Y is null ? (int?)null : ConvertAbsoluteValue(camera, UvcCameraProperty.Tilt, options.Y.Value, mode);
        var panTolerance = panTarget is null ? 0 : ConvertSeekTolerance(camera, UvcCameraProperty.Pan, seekOptions.Tolerance, mode);
        var tiltTolerance = tiltTarget is null ? 0 : ConvertSeekTolerance(camera, UvcCameraProperty.Tilt, seekOptions.Tolerance, mode);
        var maxIterations = Math.Max(1, seekOptions.MaxIterations);
        var settleMs = Math.Max(0, seekOptions.SettleMilliseconds);

        var lastDistance = GetSeekDistance(camera, panTarget, tiltTarget);
        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var panCurrent = panTarget is null ? (int?)null : CameraBackend.GetValue(camera, UvcCameraProperty.Pan);
            var tiltCurrent = tiltTarget is null ? (int?)null : CameraBackend.GetValue(camera, UvcCameraProperty.Tilt);
            var panReached = panTarget is null || Math.Abs(panTarget.Value - panCurrent!.Value) <= panTolerance;
            var tiltReached = tiltTarget is null || Math.Abs(tiltTarget.Value - tiltCurrent!.Value) <= tiltTolerance;
            if (panReached && tiltReached)
                return Ok();

            var panDirection = panTarget is null ? (int?)null : Math.Sign(panTarget.Value - panCurrent!.Value);
            var tiltDirection = tiltTarget is null ? (int?)null : Math.Sign(tiltTarget.Value - tiltCurrent!.Value);
            CameraBackend.MoveRelativePanTilt(camera, panDirection, tiltDirection);
            if (settleMs > 0)
                Thread.Sleep(settleMs);

            var distance = GetSeekDistance(camera, panTarget, tiltTarget);
            if (distance >= lastDistance)
                throw new InvalidOperationException($"move-seek did not make progress after {iteration} iteration(s). Current distance: {distance}; previous distance: {lastDistance}.");
            lastDistance = distance;
        }

        throw new TimeoutException($"move-seek did not reach the target within {maxIterations} iteration(s). Final distance: {lastDistance}.");
    }

    static int MoveRelative(string camera, Options options, ValueMode mode)
    {
        if (options.X is null && options.Y is null) throw new ArgumentException("move-relative requires -x/--pan and/or -y/--tilt.");
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

    static int GetSeekDistance(string camera, int? panTarget, int? tiltTarget)
    {
        var distance = 0;
        if (panTarget is not null)
            distance += Math.Abs(panTarget.Value - CameraBackend.GetValue(camera, UvcCameraProperty.Pan));
        if (tiltTarget is not null)
            distance += Math.Abs(tiltTarget.Value - CameraBackend.GetValue(camera, UvcCameraProperty.Tilt));
        return distance;
    }

    static int Ok()
    {
        Console.WriteLine("OK");
        return 0;
    }

    static string ResolveCamera(Options options)
    {
        var selectorCount =
            (!string.IsNullOrWhiteSpace(options.Camera) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(options.DevicePath) ? 1 : 0) +
            (options.Slot.HasValue ? 1 : 0);
        if (selectorCount > 1)
            throw new ArgumentException("Use only one camera selector: --camera, --device-path, or --slot.");

        if (options.Slot.HasValue)
        {
            var slot = RequireSlot(options.Slot, "--slot");
            var slotCameras = CameraBackend.Enumerate();
            if (slotCameras.Count < slot)
                throw new InvalidOperationException($"Camera slot {slot} is not available. Found {slotCameras.Count} camera(s).");
            return slotCameras[slot - 1].Name;
        }

        if (!string.IsNullOrWhiteSpace(options.DevicePath))
            return options.DevicePath;

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
            if (CameraMatches(cameras[i], camera))
                return (cameras[i].Name, i < 3 ? i : null);
        }

        return (camera, null);
    }

    static (CameraInfo? camera, int? slotIndex) ResolveCameraInfo(string camera)
    {
        var cameras = CameraBackend.Enumerate();
        for (var i = 0; i < cameras.Count; i++)
        {
            if (CameraMatches(cameras[i], camera))
                return (cameras[i], i < 3 ? i : null);
        }

        return (null, null);
    }

    static CameraInfo ResolveSingleCameraInfo(Options options, bool requireExplicitSelector)
    {
        var selectorCount =
            (!string.IsNullOrWhiteSpace(options.Camera) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(options.DevicePath) ? 1 : 0) +
            (options.Slot.HasValue ? 1 : 0);
        if (selectorCount > 1)
            throw new ArgumentException("Use only one camera selector: --camera, --device-path, or --slot.");
        if (requireExplicitSelector && selectorCount == 0)
            throw new ArgumentException("Use --camera, --device-path, or --slot to select the camera.");

        var cameras = CameraBackend.Enumerate();
        if (cameras.Count == 0)
            throw new InvalidOperationException("No camera found.");

        if (options.Slot.HasValue)
        {
            var slot = RequireSlot(options.Slot, "--slot");
            if (cameras.Count < slot)
                throw new InvalidOperationException($"Camera slot {slot} is not available. Found {cameras.Count} camera(s).");
            return RequireDevicePath(cameras[slot - 1]);
        }

        if (!string.IsNullOrWhiteSpace(options.DevicePath))
        {
            var match = cameras
                .Where(camera => !string.IsNullOrWhiteSpace(camera.MonikerString) &&
                    string.Equals(camera.MonikerString, options.DevicePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (match.Count == 1)
                return RequireDevicePath(match[0]);

            return new CameraInfo { Name = options.DevicePath, MonikerString = options.DevicePath };
        }

        if (!string.IsNullOrWhiteSpace(options.Camera))
        {
            var matches = cameras.Where(camera => CameraMatches(camera, options.Camera)).ToList();
            if (matches.Count == 0)
                throw new InvalidOperationException($"Camera '{options.Camera}' not found.");
            if (matches.Count > 1)
                throw new InvalidOperationException($"Camera selector '{options.Camera}' is ambiguous. Use --device-path instead.");
            return RequireDevicePath(matches[0]);
        }

        return RequireDevicePath(cameras[0]);
    }

    static CameraInfo RequireDevicePath(CameraInfo camera)
    {
        if (string.IsNullOrWhiteSpace(camera.MonikerString))
            throw new InvalidOperationException($"Camera '{camera.Name}' does not provide a device path.");
        return camera;
    }

    static bool CameraMatches(CameraInfo camera, string query) =>
        camera.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(camera.MonikerString) && camera.MonikerString.Contains(query, StringComparison.OrdinalIgnoreCase));

    static int ResolvePresetNameSlot(Options options)
    {
        if (options.Slot.HasValue && (!string.IsNullOrWhiteSpace(options.Camera) || !string.IsNullOrWhiteSpace(options.DevicePath)))
            throw new ArgumentException("Use either --slot, --camera, or --device-path.");

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

        return LoadJsonMetadataConfig().GetSlot(cameraSlotIndex + 1)?.GetPresetName(preset);
    }

    static void WritePresetName(int cameraSlotIndex, int preset, string name)
    {
        if (OperatingSystem.IsWindows())
        {
            WritePresetNameToRegistry(cameraSlotIndex, preset, name);
            return;
        }

        var config = LoadJsonMetadataConfig();
        config.GetOrCreateSlot(cameraSlotIndex + 1).SetPresetName(preset, name);
        SaveJsonMetadataConfig(config);
    }

    static string? ReadCameraSlotAlias(int cameraSlotIndex)
    {
        if (OperatingSystem.IsWindows())
            return ReadCameraSlotAliasFromRegistry(cameraSlotIndex);

        return LoadJsonMetadataConfig().GetSlot(cameraSlotIndex + 1)?.FriendlyName;
    }

    static string? TryReadCameraSlotAlias(int cameraSlotIndex)
    {
        try
        {
            return ReadCameraSlotAlias(cameraSlotIndex);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    static void WriteCameraSlotAlias(int cameraSlotIndex, string name)
    {
        if (OperatingSystem.IsWindows())
        {
            WriteCameraSlotAliasToRegistry(cameraSlotIndex, name);
            return;
        }

        var config = LoadJsonMetadataConfig();
        config.GetOrCreateSlot(cameraSlotIndex + 1).FriendlyName = string.IsNullOrWhiteSpace(name) ? null : name;
        SaveJsonMetadataConfig(config);
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

    [SupportedOSPlatform("windows")]
    static string? ReadCameraSlotAliasFromRegistry(int cameraSlotIndex)
    {
        var valueName = $"CameraAlias{cameraSlotIndex + 1}";
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\MRi-Software\PTZControl\Window");
        return key?.GetValue(valueName) as string;
    }

    [SupportedOSPlatform("windows")]
    static void WriteCameraSlotAliasToRegistry(int cameraSlotIndex, string name)
    {
        var valueName = $"CameraAlias{cameraSlotIndex + 1}";
        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\MRi-Software\PTZControl\Window");
        key.SetValue(valueName, name, RegistryValueKind.String);
    }

    static void EnsurePresetNamesSupported()
    {
    }

    static string ExportMetadataConfig() =>
        JsonSerializer.Serialize(ReadCurrentMetadataConfig(), JsonOptions);

    static void ImportMetadataConfig(string path)
    {
        var config = JsonSerializer.Deserialize<MetadataConfig>(File.ReadAllText(path), JsonOptions)
            ?? new MetadataConfig();
        WriteCurrentMetadataConfig(config);
    }

    static MetadataConfig ReadCurrentMetadataConfig()
    {
        var config = new MetadataConfig();
        var cameras = CameraBackend.Enumerate();
        for (var slot = 1; slot <= 3; slot++)
        {
            var slotConfig = new CameraSlotMetadata { Slot = slot };
            if (slot <= cameras.Count)
            {
                slotConfig.LastDeviceName = cameras[slot - 1].Name;
                slotConfig.LastDevicePath = cameras[slot - 1].MonikerString;
            }
            slotConfig.FriendlyName = ReadCameraSlotAlias(slot - 1);
            for (var preset = 1; preset <= 8; preset++)
            {
                var presetName = ReadPresetName(slot - 1, preset);
                if (!string.IsNullOrWhiteSpace(presetName))
                    slotConfig.PresetNames[preset.ToString()] = presetName;
            }

            if (!string.IsNullOrWhiteSpace(slotConfig.FriendlyName) || slotConfig.PresetNames.Count > 0)
                config.Cameras.Add(slotConfig);
        }

        return config;
    }

    static void WriteCurrentMetadataConfig(MetadataConfig config)
    {
        for (var slot = 1; slot <= 3; slot++)
        {
            var slotConfig = config.GetSlot(slot);
            WriteCameraSlotAlias(slot - 1, slotConfig?.FriendlyName ?? "");
            for (var preset = 1; preset <= 8; preset++)
            {
                var name = slotConfig?.GetPresetName(preset) ?? "";
                WritePresetName(slot - 1, preset, name);
            }
        }
    }

    static MetadataConfig LoadJsonMetadataConfig()
    {
        var path = GetJsonMetadataConfigPath();
        if (!File.Exists(path))
            return new MetadataConfig();

        return JsonSerializer.Deserialize<MetadataConfig>(File.ReadAllText(path), JsonOptions)
            ?? new MetadataConfig();
    }

    static void SaveJsonMetadataConfig(MetadataConfig config)
    {
        var path = GetJsonMetadataConfigPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }

    static string GetJsonMetadataConfigPath()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PTZControl", "ptzcontrol.json");

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "PTZControl", "ptzcontrol.json");
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

    static int ConvertSeekTolerance(string camera, UvcCameraProperty property, int tolerance, ValueMode mode)
    {
        tolerance = Math.Max(0, tolerance);
        if (mode == ValueMode.Raw)
            return tolerance;

        var range = CameraBackend.GetRange(camera, property);
        return Math.Max(1, (int)Math.Round((range.max - range.min) * (tolerance / 100.0)));
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
        return ParsePreset(preset);
    }

    static int ParsePreset(int preset)
    {
        if (preset < 1 || preset > 8)
            throw new ArgumentOutOfRangeException(nameof(preset), "Preset number must be between 1 and 8. Use restore-home for the Logitech home position.");
        return preset;
    }

    static void WarnUnsupportedPresetName(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.FriendlyName))
            Console.Error.WriteLine("Preset names are not saved by save-preset; use set-preset-name to store a friendly name.");
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

    sealed class MetadataConfig
    {
        public List<CameraSlotMetadata> Cameras { get; set; } = new();

        public CameraSlotMetadata? GetSlot(int slot) =>
            Cameras.FirstOrDefault(camera => camera.Slot == slot);

        public CameraSlotMetadata GetOrCreateSlot(int slot)
        {
            var slotConfig = GetSlot(slot);
            if (slotConfig is not null)
                return slotConfig;

            slotConfig = new CameraSlotMetadata { Slot = slot };
            Cameras.Add(slotConfig);
            Cameras.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return slotConfig;
        }
    }

    sealed class CameraSlotMetadata
    {
        public int Slot { get; set; }
        public string? FriendlyName { get; set; }
        public string? LastDeviceName { get; set; }
        public string? LastDevicePath { get; set; }
        public Dictionary<string, string> PresetNames { get; set; } = new();

        public string? GetPresetName(int preset) =>
            PresetNames.TryGetValue(preset.ToString(), out var name) ? name : null;

        public void SetPresetName(int preset, string name)
        {
            var key = preset.ToString();
            if (string.IsNullOrWhiteSpace(name))
                PresetNames.Remove(key);
            else
                PresetNames[key] = name;
        }
    }

    interface ICameraSelectionOptions
    {
        string? Camera { get; }
        string? DevicePath { get; }
        int? Slot { get; }
    }

    interface IFriendlyNameOptions
    {
        string? FriendlyName { get; }
    }

    interface IConfigTransportOptions
    {
        bool ExportConfig { get; }
        string? ExportConfigPath { get; }
        string? ImportConfigPath { get; }
    }

    interface IMoveOptions
    {
        int? X { get; }
        int? Y { get; }
    }

    abstract class CameraSelectionOptions : ICameraSelectionOptions
    {
        [Option('c', "camera", HelpText = "Camera device name fragment.")]
        public string? Camera { get; set; }

        [Option('d', "device-path", HelpText = "Concrete camera device path.")]
        public string? DevicePath { get; set; }

        [Option('s', "slot", HelpText = "PTZControl camera slot 1..3.")]
        public int? Slot { get; set; }
    }

    [Verb("list-devices", HelpText = "List available camera devices.")]
    sealed class ListDevicesOptions
    {
    }

    [Verb("cam-device-info", HelpText = "Show camera device information and supported raw ranges.")]
    sealed class CamDeviceInfoOptions : CameraSelectionOptions
    {
    }

    abstract class PresetOptions : CameraSelectionOptions
    {
        [Value(0, Required = true, MetaName = "preset", HelpText = "Preset number 1..8.")]
        public int Preset { get; set; }
    }

    [Verb("get-preset-name", HelpText = "Print the friendly name of one preset.")]
    sealed class GetPresetNameOptions : PresetOptions
    {
    }

    [Verb("set-preset-name", HelpText = "Store the friendly name of one preset.")]
    sealed class SetPresetNameOptions : PresetOptions, IFriendlyNameOptions
    {
        [Option('n', "friendlyname", Required = true, HelpText = "Friendly display name to store.")]
        public string? FriendlyName { get; set; }
    }

    [Verb("clear-preset-name", HelpText = "Clear the friendly name of one preset.")]
    sealed class ClearPresetNameOptions : PresetOptions
    {
    }

    [Verb("get-camera-name", HelpText = "Print the friendly name of one camera slot.")]
    sealed class GetCameraNameOptions
    {
        [Option('s', "slot", Required = true, HelpText = "PTZControl camera slot 1..3.")]
        public int? Slot { get; set; }
    }

    [Verb("set-camera-name", HelpText = "Store the friendly name of one camera slot.")]
    sealed class SetCameraNameOptions : IFriendlyNameOptions
    {
        [Option('s', "slot", Required = true, HelpText = "PTZControl camera slot 1..3.")]
        public int? Slot { get; set; }

        [Option('n', "friendlyname", Required = true, HelpText = "Friendly display name to store.")]
        public string? FriendlyName { get; set; }
    }

    [Verb("clear-camera-name", HelpText = "Clear the friendly name of one camera slot.")]
    sealed class ClearCameraNameOptions
    {
        [Option('s', "slot", Required = true, HelpText = "PTZControl camera slot 1..3.")]
        public int? Slot { get; set; }
    }

    [Verb("get-directshow-camera-name", HelpText = "Print the Windows DirectShow camera name.")]
    sealed class GetDirectShowCameraNameOptions : CameraSelectionOptions
    {
    }

    [Verb("set-directshow-camera-name", HelpText = "Set the Windows DirectShow camera name.")]
    sealed class SetDirectShowCameraNameOptions : CameraSelectionOptions, IFriendlyNameOptions
    {
        [Option('n', "friendlyname", Required = true, HelpText = "DirectShow friendly name to write.")]
        public string? FriendlyName { get; set; }

        [Option("acknowledge-warning", HelpText = "Skip the interactive registry risk confirmation.")]
        public bool AcknowledgeWarning { get; set; }
    }

    [Verb("swap-preset-names", HelpText = "Swap preset friendly names between two camera slots.")]
    sealed class SwapPresetNamesOptions
    {
        [Option("slot-a", Required = true, HelpText = "First PTZControl camera slot 1..3.")]
        public int? SlotA { get; set; }

        [Option("slot-b", Required = true, HelpText = "Second PTZControl camera slot 1..3.")]
        public int? SlotB { get; set; }
    }

    [Verb("config", HelpText = "Export or import preset and camera friendly-name metadata.")]
    sealed class ConfigOptions : IConfigTransportOptions
    {
        [Option("export", SetName = "export", HelpText = "Export metadata JSON to stdout or json-path.")]
        public bool ExportConfig { get; set; }

        [Value(0, Required = false, MetaName = "json-path", HelpText = "JSON file path for config export.")]
        public string? ExportConfigPath { get; set; }

        [Option("import", SetName = "import", HelpText = "Import metadata JSON from a file path.")]
        public string? ImportConfigPath { get; set; }
    }

    abstract class TargetCameraOptions : CameraSelectionOptions
    {
        [Option('t', "target", Required = true, HelpText = "Restore target: zoom, move, move-x, move-y, or all. Home supports zoom, move, or all.")]
        public string TargetName { get; set; } = "";
    }

    [Verb("restore-home", HelpText = "Restore the Logitech home position.")]
    sealed class RestoreHomeOptions : TargetCameraOptions
    {
    }

    [Verb("restore-default", HelpText = "Restore driver default values.")]
    sealed class RestoreDefaultOptions : TargetCameraOptions
    {
    }

    [Verb("restore-preset", HelpText = "Restore a camera preset position.")]
    sealed class RestorePresetOptions : PresetOptions
    {
    }

    [Verb("save-preset", HelpText = "Save the current camera position as a preset.")]
    sealed class SavePresetOptions : PresetOptions, IFriendlyNameOptions
    {
        [Option('n', "friendlyname", HelpText = "Friendly display name to store separately with set-preset-name.")]
        public string? FriendlyName { get; set; }
    }

    abstract class ModeCameraOptions : CameraSelectionOptions
    {
        [Option('m', "mode", Required = true, HelpText = "Value mode: percent or raw.")]
        public string ModeName { get; set; } = "";
    }

    [Verb("zoom-absolute", HelpText = "Set an absolute zoom value.")]
    sealed class ZoomAbsoluteOptions : ModeCameraOptions
    {
        [Value(0, Required = true, MetaName = "value", HelpText = "Absolute zoom value.")]
        public int Value { get; set; }
    }

    [Verb("zoom-relative", HelpText = "Change zoom by a relative delta.")]
    sealed class ZoomRelativeOptions : ModeCameraOptions
    {
        [Value(0, Required = true, MetaName = "value-delta", HelpText = "Relative zoom delta.")]
        public int ValueDelta { get; set; }
    }

    abstract class MoveOptions : ModeCameraOptions, IMoveOptions
    {
        [Option('x', "pan", HelpText = "Pan axis value.")]
        public int? X { get; set; }

        [Option('y', "tilt", HelpText = "Tilt axis value.")]
        public int? Y { get; set; }
    }

    [Verb("move-absolute", HelpText = "Set absolute pan and/or tilt values.")]
    sealed class MoveAbsoluteOptions : MoveOptions
    {
    }

    [Verb("move-seek", HelpText = "Experimentally seek absolute pan and/or tilt values using relative movement.")]
    sealed class MoveSeekOptions : MoveOptions
    {
        [Option("tolerance", Default = 2, HelpText = "Allowed target distance in the selected mode.")]
        public int Tolerance { get; set; }

        [Option("max-iterations", Default = 30, HelpText = "Maximum relative correction attempts.")]
        public int MaxIterations { get; set; }

        [Option("settle-ms", Default = 250, HelpText = "Delay after each relative movement attempt.")]
        public int SettleMilliseconds { get; set; }
    }

    [Verb("move-relative", HelpText = "Change pan and/or tilt by relative deltas.")]
    sealed class MoveRelativeOptions : MoveOptions
    {
    }

    [Verb("docs", Hidden = true)]
    sealed class DocsOptions
    {
        [Option("output", HelpText = "Generated documentation output directory.")]
        public string? OutputPath { get; set; }

        [Option("check", HelpText = "Check whether generated documentation is up to date.")]
        public bool Check { get; set; }
    }

    static Options ToOptions(object source)
    {
        var options = new Options();
        if (source is ICameraSelectionOptions camera)
        {
            options.Camera = camera.Camera;
            options.DevicePath = camera.DevicePath;
            options.Slot = camera.Slot;
        }
        if (source is IFriendlyNameOptions friendlyName)
            options.FriendlyName = friendlyName.FriendlyName;
        if (source is IConfigTransportOptions config)
        {
            options.ExportConfig = config.ExportConfig;
            options.ExportConfigPath = config.ExportConfigPath;
            options.ImportConfigPath = config.ImportConfigPath;
        }
        if (source is GetCameraNameOptions getCameraName)
            options.Slot = getCameraName.Slot;
        if (source is SetCameraNameOptions setCameraName)
            options.Slot = setCameraName.Slot;
        if (source is ClearCameraNameOptions clearCameraName)
            options.Slot = clearCameraName.Slot;
        if (source is IMoveOptions move)
        {
            options.X = move.X;
            options.Y = move.Y;
        }
        return options;
    }

    sealed class Options
    {
        public string? Camera { get; set; }
        public string? DevicePath { get; set; }
        public string? FriendlyName { get; set; }
        public bool ExportConfig { get; set; }
        public string? ExportConfigPath { get; set; }
        public string? ImportConfigPath { get; set; }
        public int? Slot { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
    }

    private const string DirectShowRenameWarningText =
        "This feature is provided \"as is\" without any warranty. The authors are not responsible for any damage to the system or the cameras. Use this feature at your own risk. It is recommended to create a backup of the registry path HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\DeviceClasses\\{65e8773d-8f56-11d0-a3b9-00a0c9223196} before using this feature, especially if you are not sure what you are doing.";
}
