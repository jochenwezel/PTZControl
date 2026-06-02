using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using PTZControl.Uvc;

namespace PTZControlConsole;

internal interface ICameraBackend
{
    IReadOnlyList<CameraInfo> Enumerate();
    (int min, int max, int step, int def) GetRange(string camera, CameraProperty property);
    int GetValue(string camera, CameraProperty property);
    void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null);
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

internal sealed class WindowsUvcCameraBackend : ICameraBackend
{
    public IReadOnlyList<CameraInfo> Enumerate() => UvcCamera.Enumerate();

    public (int min, int max, int step, int def) GetRange(string camera, CameraProperty property) =>
        UvcCamera.GetRange(camera, property);

    public int GetValue(string camera, CameraProperty property) =>
        UvcCamera.GetValue(camera, property);

    public void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null) =>
        UvcCamera.SetPanTiltZoom(camera, pan, tilt, zoom);

    public void SavePreset(string camera, int presetNumber) =>
        UvcCamera.SavePreset(camera, presetNumber);

    public void RestorePreset(string camera, int presetNumber) =>
        UvcCamera.RestorePreset(camera, presetNumber);
}

internal sealed class LinuxPreviewCameraBackend : ICameraBackend
{
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

    public (int min, int max, int step, int def) GetRange(string camera, CameraProperty property) =>
        throw LinuxControlNotSupported();

    public int GetValue(string camera, CameraProperty property) =>
        throw LinuxControlNotSupported();

    public void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null) =>
        throw LinuxControlNotSupported();

    public void SavePreset(string camera, int presetNumber) =>
        throw LinuxControlNotSupported();

    public void RestorePreset(string camera, int presetNumber) =>
        throw LinuxControlNotSupported();

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

    private static NotSupportedException LinuxControlNotSupported() =>
        new("Linux camera control is not implemented yet. This preview build can list /dev/video* devices only.");
}

internal sealed class UnsupportedCameraBackend(string message) : ICameraBackend
{
    public IReadOnlyList<CameraInfo> Enumerate() => throw new NotSupportedException(message);

    public (int min, int max, int step, int def) GetRange(string camera, CameraProperty property) =>
        throw new NotSupportedException(message);

    public int GetValue(string camera, CameraProperty property) =>
        throw new NotSupportedException(message);

    public void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null) =>
        throw new NotSupportedException(message);

    public void SavePreset(string camera, int presetNumber) =>
        throw new NotSupportedException(message);

    public void RestorePreset(string camera, int presetNumber) =>
        throw new NotSupportedException(message);
}
