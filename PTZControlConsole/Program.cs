using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
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

        return Parser.Default.ParseArguments(args,
            typeof(ListDevicesOptions),
            typeof(CamDeviceInfoOptions),
            typeof(ListPresetsOptions),
            typeof(GetPresetNameOptions),
            typeof(SetPresetNameOptions),
            typeof(ClearPresetNameOptions),
            typeof(GetCameraNameOptions),
            typeof(SetCameraNameOptions),
            typeof(ClearCameraNameOptions),
            typeof(SwapPresetNamesOptions),
            typeof(ConfigOptions),
            typeof(RestoreHomeOptions),
            typeof(RestoreDefaultOptions),
            typeof(RestorePresetOptions),
            typeof(SavePresetOptions),
            typeof(ZoomAbsoluteOptions),
            typeof(ZoomRelativeOptions),
            typeof(MoveAbsoluteOptions),
            typeof(MoveRelativeOptions))
            .MapResult(RunParsed, _ => 1);
    }

    static int RunParsed(object parsed) =>
        parsed switch
        {
            ListDevicesOptions => ListDevices(),
            CamDeviceInfoOptions options => PrintCameraDeviceInfo(ResolveCamera(ToOptions(options))),
            ListPresetsOptions options => ListPresets(ResolveCamera(ToOptions(options))),
            GetPresetNameOptions options => GetPresetName(ToOptions(options), ParsePreset(options.Preset)),
            SetPresetNameOptions options => SetPresetName(ToOptions(options), ParsePreset(options.Preset)),
            ClearPresetNameOptions options => ClearPresetName(ToOptions(options), ParsePreset(options.Preset)),
            GetCameraNameOptions options => GetCameraName(ToOptions(options)),
            SetCameraNameOptions options => SetCameraName(ToOptions(options)),
            ClearCameraNameOptions options => ClearCameraName(ToOptions(options)),
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
            MoveRelativeOptions options => MoveRelative(ResolveCamera(ToOptions(options)), ToOptions(options), ParseMode(options.ModeName)),
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

    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  PTZControlConsole list-devices");
        Console.WriteLine("  PTZControlConsole cam-device-info [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole list-presets [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole get-preset-name 1..8 [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole set-preset-name 1..8 --friendlyname \"Title\" [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole clear-preset-name 1..8 [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole get-camera-name --slot 1..3");
        Console.WriteLine("  PTZControlConsole set-camera-name --friendlyname \"Title\" --slot 1..3");
        Console.WriteLine("  PTZControlConsole clear-camera-name --slot 1..3");
        Console.WriteLine("  PTZControlConsole swap-preset-names --slot-a 1..3 --slot-b 1..3");
        Console.WriteLine("  PTZControlConsole config --export [json-path]");
        Console.WriteLine("  PTZControlConsole config --import json-path");
        Console.WriteLine("  PTZControlConsole restore-home --target zoom|move|all [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole restore-default --target zoom|move|move-x|move-y|all [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole restore-preset 1..8 [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole save-preset 1..8 [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3] [--friendlyname \"Title\"]");
        Console.WriteLine("  PTZControlConsole zoom-absolute VALUE --mode percent|raw [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole zoom-relative VALUE_DELTA --mode percent|raw [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole move-absolute --mode percent|raw [--x VALUE] [--y VALUE] [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
        Console.WriteLine("  PTZControlConsole move-relative --mode percent|raw [--x VALUE_DELTA] [--y VALUE_DELTA] [--camera \"NamePart\" | --device-path \"DevicePath\" | --slot 1..3]");
    }

    static int ListDevices()
    {
        foreach (var cam in CameraBackend.Enumerate())
            Console.WriteLine(string.IsNullOrWhiteSpace(cam.MonikerString) ? cam.Name : $"{cam.Name}\t{cam.MonikerString}");
        return 0;
    }

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

    static int ListPresets(string camera)
    {
        var (cameraName, slotIndex) = ResolveCameraSlot(camera);
        Console.WriteLine($"Camera Device Name: {cameraName}");
        if (slotIndex is not null)
        {
            Console.WriteLine($"PTZControl app camera slot: {slotIndex.Value + 1}");
            var alias = TryReadCameraSlotAlias(slotIndex.Value);
            if (!string.IsNullOrWhiteSpace(alias))
                Console.WriteLine($"Camera Slot Alias: {alias}");
        }
        else
        {
            Console.WriteLine("PTZControl app camera slot: not available");
        }
        Console.WriteLine("Preset storage: camera Logitech extension unit");

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
        return 0;
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
        [Option("camera", HelpText = "Camera device name fragment.")]
        public string? Camera { get; set; }

        [Option("device-path", HelpText = "Concrete camera device path.")]
        public string? DevicePath { get; set; }

        [Option("slot", HelpText = "PTZControl camera slot 1..3.")]
        public int? Slot { get; set; }
    }

    [Verb("list-devices")]
    sealed class ListDevicesOptions
    {
    }

    [Verb("cam-device-info")]
    sealed class CamDeviceInfoOptions : CameraSelectionOptions
    {
    }

    [Verb("list-presets")]
    sealed class ListPresetsOptions : CameraSelectionOptions
    {
    }

    abstract class PresetOptions : CameraSelectionOptions
    {
        [Value(0, Required = true, MetaName = "preset")]
        public int Preset { get; set; }
    }

    [Verb("get-preset-name")]
    sealed class GetPresetNameOptions : PresetOptions
    {
    }

    [Verb("set-preset-name")]
    sealed class SetPresetNameOptions : PresetOptions, IFriendlyNameOptions
    {
        [Option("friendlyname", Required = true)]
        public string? FriendlyName { get; set; }
    }

    [Verb("clear-preset-name")]
    sealed class ClearPresetNameOptions : PresetOptions
    {
    }

    [Verb("get-camera-name")]
    sealed class GetCameraNameOptions
    {
        [Option("slot", Required = true)]
        public int? Slot { get; set; }
    }

    [Verb("set-camera-name")]
    sealed class SetCameraNameOptions : IFriendlyNameOptions
    {
        [Option("slot", Required = true)]
        public int? Slot { get; set; }

        [Option("friendlyname", Required = true)]
        public string? FriendlyName { get; set; }
    }

    [Verb("clear-camera-name")]
    sealed class ClearCameraNameOptions
    {
        [Option("slot", Required = true)]
        public int? Slot { get; set; }
    }

    [Verb("swap-preset-names")]
    sealed class SwapPresetNamesOptions
    {
        [Option("slot-a", Required = true)]
        public int? SlotA { get; set; }

        [Option("slot-b", Required = true)]
        public int? SlotB { get; set; }
    }

    [Verb("config")]
    sealed class ConfigOptions : IConfigTransportOptions
    {
        [Option("export", SetName = "export")]
        public bool ExportConfig { get; set; }

        [Value(0, Required = false, MetaName = "json-path")]
        public string? ExportConfigPath { get; set; }

        [Option("import", SetName = "import")]
        public string? ImportConfigPath { get; set; }
    }

    abstract class TargetCameraOptions : CameraSelectionOptions
    {
        [Option("target", Required = true)]
        public string TargetName { get; set; } = "";
    }

    [Verb("restore-home")]
    sealed class RestoreHomeOptions : TargetCameraOptions
    {
    }

    [Verb("restore-default")]
    sealed class RestoreDefaultOptions : TargetCameraOptions
    {
    }

    [Verb("restore-preset")]
    sealed class RestorePresetOptions : PresetOptions
    {
    }

    [Verb("save-preset")]
    sealed class SavePresetOptions : PresetOptions, IFriendlyNameOptions
    {
        [Option("friendlyname")]
        public string? FriendlyName { get; set; }
    }

    abstract class ModeCameraOptions : CameraSelectionOptions
    {
        [Option("mode", Required = true)]
        public string ModeName { get; set; } = "";
    }

    [Verb("zoom-absolute")]
    sealed class ZoomAbsoluteOptions : ModeCameraOptions
    {
        [Value(0, Required = true, MetaName = "value")]
        public int Value { get; set; }
    }

    [Verb("zoom-relative")]
    sealed class ZoomRelativeOptions : ModeCameraOptions
    {
        [Value(0, Required = true, MetaName = "value-delta")]
        public int ValueDelta { get; set; }
    }

    abstract class MoveOptions : ModeCameraOptions, IMoveOptions
    {
        [Option("x")]
        public int? X { get; set; }

        [Option("y")]
        public int? Y { get; set; }
    }

    [Verb("move-absolute")]
    sealed class MoveAbsoluteOptions : MoveOptions
    {
    }

    [Verb("move-relative")]
    sealed class MoveRelativeOptions : MoveOptions
    {
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
}
