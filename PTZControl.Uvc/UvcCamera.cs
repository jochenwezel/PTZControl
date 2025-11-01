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
            var cam = cams.FirstOrDefault(d => d.Name.Contains(cameraNamePart, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Kamera '{cameraNamePart}' nicht gefunden.");

            graph = (IFilterGraph2)new FilterGraph();
            graph.AddSourceFilterForMoniker(cam.Mon, null, cam.Name, out src);
            if (src is not IAMCameraControl ctl)
                throw new NotSupportedException("IAMCameraControl (UVC) wird nicht unterstützt.");
            return ctl;
        }

        public static void SetPanTiltZoom(string cam, int? pan = null, int? tilt = null, int? zoom = null)
        {
            var ctl = GetControl(cam, out var src, out var graph);
            void Set(CameraControlProperty prop, int value)
            {
                ctl.GetRange(prop, out int min, out int max, out int step, out int def, out CameraControlFlags flags);
                value = Math.Clamp(value, min, max);
                int hr = ctl.Set(prop, value, CameraControlFlags.Manual);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            }
            if (pan.HasValue)  Set(CameraControlProperty.Pan,  pan.Value);
            if (tilt.HasValue) Set(CameraControlProperty.Tilt, tilt.Value);
            if (zoom.HasValue) Set(CameraControlProperty.Zoom, zoom.Value);
        }

        public static (int min, int max, int step, int def) GetRange(string cam, CameraProperty prop)
        {
            var ctl = GetControl(cam, out var src, out var graph);
            var p = prop switch
            {
                CameraProperty.Pan => CameraControlProperty.Pan,
                CameraProperty.Tilt => CameraControlProperty.Tilt,
                CameraProperty.Zoom => CameraControlProperty.Zoom,
                _ => throw new NotSupportedException(prop.ToString())
            };
            ctl.GetRange(p, out int min, out int max, out int step, out int def, out _);
            return (min, max, step, def);
        }
    }
}