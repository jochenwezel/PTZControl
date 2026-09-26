using Microsoft.Win32;
using Microsoft.Extensions.Logging.Abstractions;
using PTZControl.Core;
using PTZControl.Uvc;
using System.Text.Json;

namespace PTZControlServer;

public sealed class CameraApiService(ICameraBackend backend, ILogger<CameraApiService>? logger = null)
{
    private const int MaximumMetadataSlot = 3;
    private readonly SemaphoreSlim _cameraLock = new(1, 1);
    private readonly ILogger<CameraApiService> _logger = logger ?? NullLogger<CameraApiService>.Instance;

    public IReadOnlyList<CameraDeviceDto> GetDevices()
        => ReadLocked(() =>
        {
            var cameras = backend.Enumerate();
            return cameras.Select((camera, index) => new CameraDeviceDto(
                index + 1,
                camera.Name,
                camera.MonikerString,
                index < MaximumMetadataSlot ? ReadCameraAlias(index) : null)).ToArray();
        });

    public CameraDeviceInfoDto GetCameraInfo(int slot)
        => ReadLocked(() =>
        {
            var camera = ResolveCamera(slot);
            return new CameraDeviceInfoDto(
                slot,
                camera.Name,
                camera.MonikerString,
                slot <= MaximumMetadataSlot ? ReadCameraAlias(slot - 1) : null,
                TryReadRange(camera.MonikerString, CameraProperty.Zoom),
                TryReadRange(camera.MonikerString, CameraProperty.Pan),
                TryReadRange(camera.MonikerString, CameraProperty.Tilt),
                Enumerable.Range(1, 8).Select(number => new PresetDto(number, slot <= MaximumMetadataSlot ? ReadPresetName(slot - 1, number) : null)).ToArray());
        });

    public Task ExecuteAsync(int slot, Action<ICameraBackend, string> action) =>
        ExecuteAsync(slot, (cameraBackend, camera) =>
        {
            action(cameraBackend, camera);
            return Task.CompletedTask;
        });

    public async Task ExecuteAsync(int slot, Func<ICameraBackend, string, Task> action)
    {
        await _cameraLock.WaitAsync();
        try
        {
            var camera = ResolveCamera(slot);
            await action(backend, camera.MonikerString);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Camera operation failed for slot {Slot}", slot);
            throw;
        }
        finally
        {
            _cameraLock.Release();
        }
    }

    public Task SetZoomAsync(int slot, int value, ValueMode mode, bool relative) =>
        ExecuteAsync(slot, (cameraBackend, camera) =>
        {
            _logger.LogDebug("Zoom request: slot={Slot}, mode={Mode}, relative={Relative}, value={Value}", slot, mode, relative, value);
            if (relative)
            {
                var rawDelta = mode == ValueMode.Percent
                    ? PercentDelta(cameraBackend, camera, CameraProperty.Zoom, value)
                    : value;
                SetRelative(cameraBackend, camera, slot, CameraProperty.Zoom, rawDelta);
            }
            else
            {
                var target = ToAbsolute(cameraBackend, camera, CameraProperty.Zoom, value, mode);
                SetAbsolute(cameraBackend, camera, slot, CameraProperty.Zoom, target);
            }
        });

    public Task MoveAsync(int slot, int? pan, int? tilt, ValueMode mode, bool relative) =>
        ExecuteAsync(slot, (cameraBackend, camera) =>
        {
            _logger.LogDebug("Move request: slot={Slot}, mode={Mode}, relative={Relative}, pan={Pan}, tilt={Tilt}", slot, mode, relative, pan, tilt);
            if (pan is null && tilt is null)
                throw new ApiInputException("Specify pan, tilt, or both.");

            if (relative)
            {
                int? rawPan = pan is null ? null : mode == ValueMode.Percent ? PercentDelta(cameraBackend, camera, CameraProperty.Pan, pan.Value) : pan;
                int? rawTilt = tilt is null ? null : mode == ValueMode.Percent ? PercentDelta(cameraBackend, camera, CameraProperty.Tilt, tilt.Value) : tilt;
                if (rawPan.HasValue)
                    SetRelative(cameraBackend, camera, slot, CameraProperty.Pan, rawPan.Value);
                if (rawTilt.HasValue)
                    SetRelative(cameraBackend, camera, slot, CameraProperty.Tilt, rawTilt.Value);
            }
            else
            {
                int? rawPan = pan is null ? null : ToAbsolute(cameraBackend, camera, CameraProperty.Pan, pan.Value, mode);
                int? rawTilt = tilt is null ? null : ToAbsolute(cameraBackend, camera, CameraProperty.Tilt, tilt.Value, mode);
                _logger.LogDebug("Absolute move write: slot={Slot}, panTarget={PanTarget}, tiltTarget={TiltTarget}", slot, rawPan, rawTilt);
                cameraBackend.SetPanTiltZoom(camera, rawPan, rawTilt);
                if (rawPan.HasValue)
                    LogReadback(cameraBackend, camera, slot, CameraProperty.Pan, rawPan.Value);
                if (rawTilt.HasValue)
                    LogReadback(cameraBackend, camera, slot, CameraProperty.Tilt, rawTilt.Value);
            }
        });

    public static ValueMode ParseMode(string mode) => mode.ToLowerInvariant() switch
    {
        "percent" => ValueMode.Percent,
        "raw" => ValueMode.Raw,
        _ => throw new ApiInputException("Mode must be 'percent' or 'raw'.")
    };

    public static (bool zoom, bool move) ParseHomeTarget(string target) => target.ToLowerInvariant() switch
    {
        "zoom" => (true, false),
        "move" => (false, true),
        "all" => (true, true),
        _ => throw new ApiInputException("Home target must be 'zoom', 'move', or 'all'.")
    };

    public static (bool zoom, bool pan, bool tilt) ParseDefaultTarget(string target) => target.ToLowerInvariant() switch
    {
        "zoom" => (true, false, false),
        "move" => (false, true, true),
        "move-x" => (false, true, false),
        "move-y" => (false, false, true),
        "all" => (true, true, true),
        _ => throw new ApiInputException("Default target must be 'zoom', 'move', 'move-x', 'move-y', or 'all'.")
    };

    public static int ValidatePreset(int preset)
    {
        if (preset is < 1 or > 8)
            throw new ApiInputException("Preset must be between 1 and 8.");
        return preset;
    }

    private CameraInfo ResolveCamera(int slot)
    {
        if (slot < 1)
            throw new ApiInputException("Camera slot must be 1 or greater.");
        var cameras = backend.Enumerate();
        if (slot > cameras.Count)
            throw new CameraNotFoundException($"Camera slot {slot} is not available. Found {cameras.Count} camera(s).");
        var camera = cameras[slot - 1];
        if (string.IsNullOrWhiteSpace(camera.MonikerString))
            throw new CameraNotFoundException($"Camera slot {slot} does not provide a device path.");
        _logger.LogDebug("Resolved camera: slot={Slot}, name={Name}, devicePath={DevicePath}", slot, camera.Name, camera.MonikerString);
        return camera;
    }

    private T ReadLocked<T>(Func<T> read)
    {
        _cameraLock.Wait();
        try
        {
            return read();
        }
        finally
        {
            _cameraLock.Release();
        }
    }

    private static int ToAbsolute(ICameraBackend cameraBackend, string camera, CameraProperty property, int value, ValueMode mode)
    {
        var range = cameraBackend.GetRange(camera, property);
        if (mode == ValueMode.Percent)
        {
            if (value is < 0 or > 100)
                throw new ApiInputException("Absolute percent values must be between 0 and 100.");
            return range.min + (int)Math.Round((range.max - range.min) * (value / 100d));
        }
        if (value < range.min || value > range.max)
            throw new ApiInputException($"Raw {property.ToString().ToLowerInvariant()} value must be between {range.min} and {range.max}.");
        return value;
    }

    private static int PercentDelta(ICameraBackend cameraBackend, string camera, CameraProperty property, int percent)
    {
        if (percent is < -100 or > 100)
            throw new ApiInputException("Relative percent values must be between -100 and 100.");
        var range = cameraBackend.GetRange(camera, property);
        var delta = (int)Math.Round((range.max - range.min) * (percent / 100d));
        if (delta == 0 && percent != 0)
            delta = Math.Sign(percent) * Math.Max(1, range.step);
        return delta;
    }

    private void SetRelative(ICameraBackend cameraBackend, string camera, int slot, CameraProperty property, int delta)
    {
        var range = cameraBackend.GetRange(camera, property);
        var current = cameraBackend.GetValue(camera, property);
        var target = Math.Clamp(current + delta, range.min, range.max);
        _logger.LogDebug("Relative camera write: slot={Slot}, property={Property}, range={Min}..{Max}, step={Step}, current={Current}, delta={Delta}, target={Target}",
            slot, property, range.min, range.max, range.step, current, delta, target);
        SetProperty(cameraBackend, camera, property, target);
        LogReadback(cameraBackend, camera, slot, property, target);
    }

    private void SetAbsolute(ICameraBackend cameraBackend, string camera, int slot, CameraProperty property, int target)
    {
        var range = cameraBackend.GetRange(camera, property);
        var current = cameraBackend.GetValue(camera, property);
        _logger.LogDebug("Absolute camera write: slot={Slot}, property={Property}, range={Min}..{Max}, step={Step}, current={Current}, target={Target}",
            slot, property, range.min, range.max, range.step, current, target);
        SetProperty(cameraBackend, camera, property, target);
        LogReadback(cameraBackend, camera, slot, property, target);
    }

    private static void SetProperty(ICameraBackend cameraBackend, string camera, CameraProperty property, int target)
    {
        if (property == CameraProperty.Zoom)
            cameraBackend.SetPanTiltZoom(camera, zoom: target);
        else if (property == CameraProperty.Pan)
            cameraBackend.SetPanTiltZoom(camera, pan: target);
        else
            cameraBackend.SetPanTiltZoom(camera, tilt: target);
    }

    private void LogReadback(ICameraBackend cameraBackend, string camera, int slot, CameraProperty property, int requested)
    {
        try
        {
            var actual = cameraBackend.GetValue(camera, property);
            _logger.LogDebug("Camera readback: slot={Slot}, property={Property}, requested={Requested}, actual={Actual}", slot, property, requested, actual);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Camera readback failed: slot={Slot}, property={Property}, requested={Requested}", slot, property, requested);
        }
    }

    private CameraRangeDto TryReadRange(string camera, CameraProperty property)
    {
        try
        {
            var range = backend.GetRange(camera, property);
            var current = backend.GetValue(camera, property);
            return new CameraRangeDto(true, range.min, range.max, range.step, range.def, current, null);
        }
        catch (Exception exception)
        {
            return new CameraRangeDto(false, null, null, null, null, null, exception.Message);
        }
    }

    private static string? ReadCameraAlias(int slotIndex)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\MRi-Software\PTZControl\Window");
            return NullIfEmpty(key?.GetValue($"CameraAlias{slotIndex + 1}") as string);
        }
        return ReadJsonSlot(slotIndex + 1)?.FriendlyName;
    }

    private static string? ReadPresetName(int slotIndex, int preset)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\MRi-Software\PTZControl\Window");
            return NullIfEmpty(key?.GetValue($"Tooltip{preset + slotIndex * 100}") as string);
        }
        var slot = ReadJsonSlot(slotIndex + 1);
        return slot?.PresetNames.TryGetValue(preset.ToString(), out var name) == true ? NullIfEmpty(name) : null;
    }

    private static JsonCameraSlot? ReadJsonSlot(int slot)
    {
        try
        {
            var path = GetJsonMetadataPath();
            if (!File.Exists(path))
                return null;
            var config = JsonSerializer.Deserialize<JsonMetadataConfig>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return config?.Cameras.FirstOrDefault(camera => camera.Slot == slot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetJsonMetadataPath()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PTZControl", "ptzcontrol.json");
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "PTZControl", "ptzcontrol.json");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class JsonMetadataConfig
    {
        public List<JsonCameraSlot> Cameras { get; set; } = [];
    }

    private sealed class JsonCameraSlot
    {
        public int Slot { get; set; }
        public string? FriendlyName { get; set; }
        public Dictionary<string, string> PresetNames { get; set; } = [];
    }
}

public enum ValueMode { Percent, Raw }
public sealed record CameraDeviceDto(int Slot, string DeviceName, string DevicePath, string? FriendlyName);
public sealed record CameraDeviceInfoDto(int Slot, string DeviceName, string DevicePath, string? FriendlyName, CameraRangeDto Zoom, CameraRangeDto Pan, CameraRangeDto Tilt, IReadOnlyList<PresetDto> Presets);
public sealed record CameraRangeDto(bool Available, int? Min, int? Max, int? Step, int? Default, int? Current, string? Error);
public sealed record PresetDto(int Number, string? FriendlyName);
public sealed record ActionResultDto(bool Success, string Action, int Slot, string Message);
public sealed class ApiInputException(string message) : Exception(message);
public sealed class CameraNotFoundException(string message) : Exception(message);
