using System;
using System.Collections.Generic;
using System.Linq;
using DirectShowLib;
using System.Runtime.InteropServices;

namespace PTZControl.Uvc
{
    public enum CameraProperty
    {
        Pan = 0,      // CameraControlProperty.Pan
        Tilt = 1,     // CameraControlProperty.Tilt
        Roll = 2,
        Zoom = 3,
        Exposure = 4,
        Iris = 5,
        Focus = 6
    }

    public sealed class CameraInfo
    {
        public string Name { get; init; } = "";
        public string MonikerString { get; init; } = "";
        public override string ToString() => Name;
    }

    public static class UvcCamera
    {
        public static IReadOnlyList<CameraInfo> Enumerate()
        {
            var list = new List<CameraInfo>();
            foreach (var dev in DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice))
                list.Add(new CameraInfo { Name = dev.Name, MonikerString = dev.DevicePath ?? "" });
            return list;
        }

        private static IAMCameraControl GetControl(string cameraNamePart, out IBaseFilter src, out IFilterGraph2 graph)
        {
            var cams = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            var cam = cams.FirstOrDefault(d =>
                    d.Name.Contains(cameraNamePart, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(d.DevicePath) && d.DevicePath.Contains(cameraNamePart, StringComparison.OrdinalIgnoreCase)))
                   ?? throw new InvalidOperationException($"Kamera '{cameraNamePart}' nicht gefunden.");

            graph = (IFilterGraph2)new FilterGraph();
            graph.AddSourceFilterForMoniker(cam.Mon, null, cam.Name, out src);
            if (src is not IAMCameraControl ctl)
                throw new NotSupportedException("IAMCameraControl (UVC) wird nicht unterstützt.");
            return ctl;
        }

        public static void SetPanTiltZoom(string cam, int? pan = null, int? tilt = null, int? zoom = null)
        {
            WithControl(cam, ctl =>
            {
                void Set(CameraControlProperty prop, int value)
                {
                    var hr = ctl.GetRange(prop, out int min, out int max, out _, out _, out _);
                    if (hr != 0) Marshal.ThrowExceptionForHR(hr);

                    value = Math.Clamp(value, min, max);
                    hr = ctl.Set(prop, value, CameraControlFlags.Manual);
                    if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                }

                if (pan.HasValue)  Set(CameraControlProperty.Pan,  pan.Value);
                if (tilt.HasValue) Set(CameraControlProperty.Tilt, tilt.Value);
                if (zoom.HasValue) Set(CameraControlProperty.Zoom, zoom.Value);
            });
        }

        public static void MoveRelativePanTilt(string cam, int? x = null, int? y = null) =>
            LogitechExtensionUnit.MoveRelativePanTilt(cam, x, y);

        public static void RestoreHome(string cam, bool zoom, bool move) =>
            LogitechExtensionUnit.RestoreHome(cam, zoom, move);

        public static void RestoreDefault(string cam, bool zoom, bool pan, bool tilt)
        {
            var zoomValue = zoom ? GetRange(cam, CameraProperty.Zoom).def : (int?)null;
            var panValue = pan ? GetRange(cam, CameraProperty.Pan).def : (int?)null;
            var tiltValue = tilt ? GetRange(cam, CameraProperty.Tilt).def : (int?)null;
            SetPanTiltZoom(cam, panValue, tiltValue, zoomValue);
        }

        public static (int min, int max, int step, int def) GetRange(string cam, CameraProperty prop)
        {
            var p = prop switch
            {
                CameraProperty.Pan => CameraControlProperty.Pan,
                CameraProperty.Tilt => CameraControlProperty.Tilt,
                CameraProperty.Zoom => CameraControlProperty.Zoom,
                _ => throw new NotSupportedException(prop.ToString())
            };

            return WithControl(cam, ctl =>
            {
                var hr = ctl.GetRange(p, out int min, out int max, out int step, out int def, out _);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                return (min, max, step, def);
            });
        }

        public static int GetValue(string cam, CameraProperty prop)
        {
            var p = prop switch
            {
                CameraProperty.Pan => CameraControlProperty.Pan,
                CameraProperty.Tilt => CameraControlProperty.Tilt,
                CameraProperty.Zoom => CameraControlProperty.Zoom,
                _ => throw new NotSupportedException(prop.ToString())
            };

            return WithControl(cam, ctl =>
            {
                int hr = ctl.Get(p, out int value, out _);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                return value;
            });
        }

        public static void SavePreset(string cam, int presetNumber) =>
            LogitechExtensionUnit.SavePreset(cam, presetNumber);

        public static void RestorePreset(string cam, int presetNumber) =>
            LogitechExtensionUnit.RestorePreset(cam, presetNumber);

        private static void WithControl(string cameraNamePart, Action<IAMCameraControl> action) =>
            WithControl<object?>(cameraNamePart, ctl =>
            {
                action(ctl);
                return null;
            });

        private static T WithControl<T>(string cameraNamePart, Func<IAMCameraControl, T> action)
        {
            var ctl = GetControl(cameraNamePart, out var src, out var graph);
            try
            {
                return action(ctl);
            }
            finally
            {
                ReleaseComObject(src);
                ReleaseComObject(graph);
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
    }
}
