using System;
using System.Collections.Generic;
using System.Linq;
using DirectShowLib;
using System.Runtime.InteropServices;

namespace PTZControl.Uvc
{
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
            {
                list.Add(new CameraInfo { Name = dev.Name, MonikerString = dev.DevicePath ?? "" });
            }
            return list;
        }

        public static void SetPanTiltZoom(string cameraNamePart, int? pan = null, int? tilt = null, int? zoom = null)
        {
            var cams = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            var cam = cams.FirstOrDefault(d => d.Name.Contains(cameraNamePart, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Kamera mit Namensbestandteil '{cameraNamePart}' nicht gefunden.");

            var graph = (IFilterGraph2)new FilterGraph();
            graph.AddSourceFilterForMoniker(cam.Mon, null, cam.Name, out IBaseFilter src);

            if (src is not IAMCameraControl camCtl)
                throw new NotSupportedException("IAMCameraControl (UVC) wird nicht unterstützt.");

            void SetProp(CameraControlProperty prop, int value)
            {
                camCtl.GetRange(prop, out int min, out int max, out int step, out int def, out CameraControlFlags flags);
                value = Math.Clamp(value, min, max);
                int hr = camCtl.Set(prop, value, CameraControlFlags.Manual);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            }

            if (pan.HasValue)  SetProp(CameraControlProperty.Pan,  pan.Value);
            if (tilt.HasValue) SetProp(CameraControlProperty.Tilt, tilt.Value);
            if (zoom.HasValue) SetProp(CameraControlProperty.Zoom, zoom.Value);
        }

        public static (int min, int max, int step, int def) GetRange(string cameraNamePart, CameraControlProperty prop)
        {
            var cams = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            var cam = cams.FirstOrDefault(d => d.Name.Contains(cameraNamePart, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Kamera '{cameraNamePart}' nicht gefunden.");

            var graph = (IFilterGraph2)new FilterGraph();
            graph.AddSourceFilterForMoniker(cam.Mon, null, cam.Name, out IBaseFilter src);

            if (src is not IAMCameraControl camCtl)
                throw new NotSupportedException("IAMCameraControl (UVC) wird nicht unterstützt.");

            camCtl.GetRange(prop, out int min, out int max, out int step, out int def, out CameraControlFlags flags);
            return (min, max, step, def);
        }
    }
}