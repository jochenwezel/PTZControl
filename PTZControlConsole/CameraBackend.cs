using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PTZControl.Uvc;

namespace PTZControlConsole;

internal interface ICameraBackend
{
    IReadOnlyList<CameraInfo> Enumerate();
    string GetDirectShowCameraName(string devicePath);
    void SetDirectShowCameraName(string devicePath, string friendlyName);
    (int min, int max, int step, int def) GetRange(string camera, CameraProperty property);
    int GetValue(string camera, CameraProperty property);
    void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null);
    void MoveRelativePanTilt(string camera, int? x = null, int? y = null);
    void RestoreHome(string camera, bool zoom, bool move);
    void RestoreDefault(string camera, bool zoom, bool pan, bool tilt);
    void SavePreset(string camera, int presetNumber);
    void RestorePreset(string camera, int presetNumber);
}

internal static class CameraBackendFactory
{
    public static ICameraBackend Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsUvcCameraBackend();

        if (OperatingSystem.IsLinux())
            return new LinuxPreviewCameraBackend();

        if (OperatingSystem.IsMacOS())
            return new UnsupportedCameraBackend("macOS camera control is not implemented yet.");

        return new UnsupportedCameraBackend($"Camera control is not implemented on {RuntimeInformation.OSDescription}.");
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsUvcCameraBackend : ICameraBackend
{
    public IReadOnlyList<CameraInfo> Enumerate() => UvcCamera.Enumerate();

    public string GetDirectShowCameraName(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            throw new ArgumentException("Device path is required.", nameof(devicePath));

        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(BuildDirectShowCameraRegistryPath(devicePath));
        return key?.GetValue("FriendlyName") as string
            ?? throw new InvalidOperationException("DirectShow FriendlyName was not found for the selected camera.");
    }

    public void SetDirectShowCameraName(string devicePath, string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            throw new ArgumentException("Device path is required.", nameof(devicePath));
        if (string.IsNullOrWhiteSpace(friendlyName))
            throw new ArgumentException("Friendly name is required.", nameof(friendlyName));

        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(BuildDirectShowCameraRegistryPath(devicePath), writable: true)
            ?? throw new InvalidOperationException("DirectShow camera registry key was not found for the selected camera.");
        key.SetValue("FriendlyName", friendlyName, Microsoft.Win32.RegistryValueKind.String);
    }

    private static string BuildDirectShowCameraRegistryPath(string devicePath)
    {
        var path = devicePath.Trim();
        const string monikerPrefix = "@device:pnp:";
        if (path.StartsWith(monikerPrefix, StringComparison.OrdinalIgnoreCase))
            path = path[monikerPrefix.Length..];

        path = path.ToLowerInvariant()
            .Replace(@"\\?\usb", @"\##?#USB", StringComparison.OrdinalIgnoreCase)
            .Replace(@"\global", @"\#GLOBAL\Device Parameters", StringComparison.OrdinalIgnoreCase)
            .Replace(@"\{", @"\#{", StringComparison.OrdinalIgnoreCase);

        if (!path.EndsWith(@"\device parameters", StringComparison.OrdinalIgnoreCase))
            path += @"\Device Parameters";

        return @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{65E8773D-8F56-11D0-A3B9-00A0C9223196}" + path;
    }

    public (int min, int max, int step, int def) GetRange(string camera, CameraProperty property) =>
        UvcCamera.GetRange(camera, property);

    public int GetValue(string camera, CameraProperty property) =>
        UvcCamera.GetValue(camera, property);

    public void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null) =>
        UvcCamera.SetPanTiltZoom(camera, pan, tilt, zoom);

    public void MoveRelativePanTilt(string camera, int? x = null, int? y = null) =>
        UvcCamera.MoveRelativePanTilt(camera, x, y);

    public void RestoreHome(string camera, bool zoom, bool move) =>
        UvcCamera.RestoreHome(camera, zoom, move);

    public void RestoreDefault(string camera, bool zoom, bool pan, bool tilt) =>
        UvcCamera.RestoreDefault(camera, zoom, pan, tilt);

    public void SavePreset(string camera, int presetNumber) =>
        UvcCamera.SavePreset(camera, presetNumber);

    public void RestorePreset(string camera, int presetNumber) =>
        UvcCamera.RestorePreset(camera, presetNumber);
}

internal sealed class LinuxPreviewCameraBackend : ICameraBackend
{
    private const int OReadWrite = 2;
    private const uint V4L2CtrlFlagDisabled = 0x0001;
    private const uint V4L2CtrlFlagInactive = 0x0010;
    private const uint V4L2CidPanAbsolute = 0x009a0908;
    private const uint V4L2CidTiltAbsolute = 0x009a0909;
    private const uint V4L2CidZoomAbsolute = 0x009a090d;
    private const ulong VidIoctlQueryCtrl = 0xC0445624;
    private const ulong VidIoctlGCtrl = 0xC008561B;
    private const ulong VidIoctlSCtrl = 0xC008561C;

    public IReadOnlyList<CameraInfo> Enumerate()
    {
        const string devicesPath = "/dev";
        if (!Directory.Exists(devicesPath))
            return Array.Empty<CameraInfo>();

        return Directory.EnumerateFiles(devicesPath, "video*")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CameraInfo
            {
                Name = GetVideoDeviceName(path),
                MonikerString = path
            })
            .ToList();
    }

    public string GetDirectShowCameraName(string devicePath) =>
        throw new NotSupportedException("DirectShow camera names are only available on Windows.");

    public void SetDirectShowCameraName(string devicePath, string friendlyName) =>
        throw new NotSupportedException("DirectShow camera rename is only available on Windows.");

    public (int min, int max, int step, int def) GetRange(string camera, CameraProperty property)
    {
        using var device = OpenCamera(camera);
        var query = QueryControl(device.FileDescriptor, property);
        return (query.Minimum, query.Maximum, query.Step, query.DefaultValue);
    }

    public int GetValue(string camera, CameraProperty property)
    {
        using var device = OpenCamera(camera);
        var control = new V4L2Control { Id = ToV4L2ControlId(property) };
        ThrowIfIoctlFailed(ioctl(device.FileDescriptor, VidIoctlGCtrl, ref control), $"read {property}");
        return control.Value;
    }

    public void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null)
    {
        using var device = OpenCamera(camera);
        if (pan.HasValue)
            SetValue(device.FileDescriptor, CameraProperty.Pan, pan.Value);
        if (tilt.HasValue)
            SetValue(device.FileDescriptor, CameraProperty.Tilt, tilt.Value);
        if (zoom.HasValue)
            SetValue(device.FileDescriptor, CameraProperty.Zoom, zoom.Value);
    }

    public void MoveRelativePanTilt(string camera, int? x = null, int? y = null)
    {
        var pan = x is null ? (int?)null : AddDelta(camera, CameraProperty.Pan, x.Value);
        var tilt = y is null ? (int?)null : AddDelta(camera, CameraProperty.Tilt, y.Value);
        SetPanTiltZoom(camera, pan, tilt);
    }

    public void RestoreHome(string camera, bool zoom, bool move) =>
        throw LinuxPresetNotSupported();

    public void RestoreDefault(string camera, bool zoom, bool pan, bool tilt)
    {
        var zoomValue = zoom ? GetRange(camera, CameraProperty.Zoom).def : (int?)null;
        var panValue = pan ? GetRange(camera, CameraProperty.Pan).def : (int?)null;
        var tiltValue = tilt ? GetRange(camera, CameraProperty.Tilt).def : (int?)null;
        SetPanTiltZoom(camera, panValue, tiltValue, zoomValue);
    }

    public void SavePreset(string camera, int presetNumber) =>
        throw LinuxPresetNotSupported();

    public void RestorePreset(string camera, int presetNumber) =>
        throw LinuxPresetNotSupported();

    private static void SetValue(int fileDescriptor, CameraProperty property, int value)
    {
        var range = QueryControl(fileDescriptor, property);
        value = Math.Clamp(value, range.Minimum, range.Maximum);
        var control = new V4L2Control { Id = ToV4L2ControlId(property), Value = value };
        ThrowIfIoctlFailed(ioctl(fileDescriptor, VidIoctlSCtrl, ref control), $"set {property}");
    }

    private int AddDelta(string camera, CameraProperty property, int deltaPercent)
    {
        var range = GetRange(camera, property);
        var current = GetValue(camera, property);
        var delta = (int)Math.Round((range.max - range.min) * (deltaPercent / 100.0));
        return Math.Clamp(current + delta, range.min, range.max);
    }

    private static V4L2QueryControl QueryControl(int fileDescriptor, CameraProperty property)
    {
        var query = new V4L2QueryControl { Id = ToV4L2ControlId(property) };
        ThrowIfIoctlFailed(ioctl(fileDescriptor, VidIoctlQueryCtrl, ref query), $"query {property}");

        if ((query.Flags & (V4L2CtrlFlagDisabled | V4L2CtrlFlagInactive)) != 0)
            throw new NotSupportedException($"Linux V4L2 control {property} is disabled or inactive on this camera.");

        return query;
    }

    private static uint ToV4L2ControlId(CameraProperty property) => property switch
    {
        CameraProperty.Pan => V4L2CidPanAbsolute,
        CameraProperty.Tilt => V4L2CidTiltAbsolute,
        CameraProperty.Zoom => V4L2CidZoomAbsolute,
        _ => throw new NotSupportedException($"Linux V4L2 control {property} is not supported.")
    };

    private static LinuxVideoDevice OpenCamera(string camera)
    {
        var devicePath = ResolveDevicePath(camera);
        var fileDescriptor = open(devicePath, OReadWrite);
        if (fileDescriptor < 0)
            throw new InvalidOperationException($"Could not open camera device '{devicePath}'.", new IOException(Marshal.GetLastPInvokeError().ToString()));
        return new LinuxVideoDevice(devicePath, fileDescriptor);
    }

    private static string ResolveDevicePath(string camera)
    {
        if (camera.StartsWith("/dev/video", StringComparison.OrdinalIgnoreCase) && File.Exists(camera))
            return camera;

        var match = new LinuxPreviewCameraBackend().Enumerate()
            .FirstOrDefault(device =>
                device.Name.Contains(camera, StringComparison.OrdinalIgnoreCase) ||
                device.MonikerString.Contains(camera, StringComparison.OrdinalIgnoreCase));

        return match?.MonikerString ?? throw new InvalidOperationException($"Camera '{camera}' not found.");
    }

    private static string GetVideoDeviceName(string devicePath)
    {
        var deviceName = Path.GetFileName(devicePath);
        var sysfsNamePath = Path.Combine("/sys/class/video4linux", deviceName, "name");
        if (File.Exists(sysfsNamePath))
        {
            var name = File.ReadAllText(sysfsNamePath).Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return deviceName;
    }

    private static NotSupportedException LinuxPresetNotSupported() =>
        new("Linux preset/home control is not implemented yet. Standard V4L2 pan, tilt, and zoom controls are supported in this release candidate.");

    private static void ThrowIfIoctlFailed(int result, string operation)
    {
        if (result >= 0)
            return;

        var errno = Marshal.GetLastPInvokeError();
        throw new NotSupportedException($"Linux V4L2 {operation} failed with errno {errno}.");
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2QueryControl queryControl);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2Control control);

    private sealed class LinuxVideoDevice(string path, int fileDescriptor) : IDisposable
    {
        public int FileDescriptor { get; } = fileDescriptor;

        public void Dispose()
        {
            if (FileDescriptor >= 0)
                close(FileDescriptor);
        }

        public override string ToString() => path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct V4L2Control
    {
        public uint Id;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private unsafe struct V4L2QueryControl
    {
        public uint Id;
        public uint Type;
        public fixed byte Name[32];
        public int Minimum;
        public int Maximum;
        public int Step;
        public int DefaultValue;
        public uint Flags;
        public fixed uint Reserved[2];
    }
}

internal sealed class UnsupportedCameraBackend(string message) : ICameraBackend
{
    public IReadOnlyList<CameraInfo> Enumerate() => throw new NotSupportedException(message);

    public string GetDirectShowCameraName(string devicePath) =>
        throw new NotSupportedException("DirectShow camera names are only available on Windows.");

    public void SetDirectShowCameraName(string devicePath, string friendlyName) =>
        throw new NotSupportedException("DirectShow camera rename is only available on Windows.");

    public (int min, int max, int step, int def) GetRange(string camera, CameraProperty property) =>
        throw new NotSupportedException(message);

    public int GetValue(string camera, CameraProperty property) =>
        throw new NotSupportedException(message);

    public void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null) =>
        throw new NotSupportedException(message);

    public void MoveRelativePanTilt(string camera, int? x = null, int? y = null) =>
        throw new NotSupportedException(message);

    public void RestoreHome(string camera, bool zoom, bool move) =>
        throw new NotSupportedException(message);

    public void RestoreDefault(string camera, bool zoom, bool pan, bool tilt) =>
        throw new NotSupportedException(message);

    public void SavePreset(string camera, int presetNumber) =>
        throw new NotSupportedException(message);

    public void RestorePreset(string camera, int presetNumber) =>
        throw new NotSupportedException(message);
}
