using System;
using System.Collections.Generic;
using System.Linq;
using DirectShowLib;
using System.Runtime.InteropServices;

namespace PTZControl.Uvc
{
    public enum CameraProperty
    {
        /// <summary>
        /// Horizontal pan position. On real PTZ cameras this may drive the pan motor; on webcams it may only move a digital crop.
        /// Support, range and units are camera/driver dependent.
        /// </summary>
        Pan = 0,

        /// <summary>
        /// Vertical tilt position. On real PTZ cameras this may drive the tilt motor; on webcams it may only move a digital crop.
        /// Support, range and units are camera/driver dependent.
        /// </summary>
        Tilt = 1,

        /// <summary>
        /// Rotation around the optical axis. This is rarely implemented by typical webcams or PTZ cameras and may be unsupported.
        /// </summary>
        Roll = 2,

        /// <summary>
        /// Zoom position. Depending on the camera this can be optical zoom, digital zoom, or unsupported.
        /// Support, range and units are camera/driver dependent.
        /// </summary>
        Zoom = 3,

        /// <summary>
        /// Exposure setting. Cameras commonly expose auto/manual modes separately via IAMCameraControl flags; manual values are
        /// driver-specific and often not plain milliseconds.
        /// </summary>
        Exposure = 4,

        /// <summary>
        /// Iris/aperture setting. Only cameras with a controllable iris usually support this property.
        /// </summary>
        Iris = 5,

        /// <summary>
        /// Focus setting. Setting a manual focus value does not guarantee an autofocus run; a practical refocus attempt usually
        /// means switching the camera to autofocus and, if desired, back to manual afterwards. Behavior is firmware/driver dependent.
        /// </summary>
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

    public sealed class DirectShowPreviewSession : IDisposable
    {
        private IFilterGraph2? _graph;
        private ICaptureGraphBuilder2? _captureGraphBuilder;
        private IBaseFilter? _source;
        private IMediaControl? _mediaControl;
        private IMediaEvent? _mediaEvent;
        private IVideoWindow? _videoWindow;
        private string _title = string.Empty;
        private bool _topMost;
        private bool _disposed;

        private DirectShowPreviewSession()
        {
        }

        public static DirectShowPreviewSession Start(string cameraNamePart, string title, bool topMost)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("DirectShow preview is only available on Windows.");

            var session = new DirectShowPreviewSession
            {
                _title = title,
                _topMost = topMost
            };
            try
            {
                session.StartCore(cameraNamePart);
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        private void StartCore(string cameraNamePart)
        {
            var cam = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)
                .FirstOrDefault(d =>
                    d.Name.Contains(cameraNamePart, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(d.DevicePath) && d.DevicePath.Contains(cameraNamePart, StringComparison.OrdinalIgnoreCase)))
                ?? throw new InvalidOperationException($"Kamera '{cameraNamePart}' nicht gefunden.");

            _graph = (IFilterGraph2)new FilterGraph();
            _captureGraphBuilder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

            var hr = _captureGraphBuilder.SetFiltergraph(_graph);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            hr = _graph.AddSourceFilterForMoniker(cam.Mon, null, cam.Name, out _source);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            hr = _captureGraphBuilder.RenderStream(PinCategory.Capture, MediaType.Video, _source, null, null);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            _videoWindow = _graph as IVideoWindow;
            _videoWindow?.put_Caption(_title);

            _mediaControl = _graph as IMediaControl;
            _mediaEvent = _graph as IMediaEvent;
            hr = _mediaControl?.Run() ?? -1;
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            ApplyTopMost();
            BringToFront();
        }

        public void SetTopMost(bool topMost)
        {
            _topMost = topMost;
            ApplyTopMost();
        }

        public void BringToFront()
        {
            try
            {
                var handle = FindPreviewWindow();
                if (handle != IntPtr.Zero)
                    SetWindowPos(handle, HwndTop, 0, 0, 0, 0, SetWindowPosFlags);
            }
            catch
            {
                // Preview window focus is best-effort; camera preview should keep running.
            }
        }

        public bool IsAlive => !_disposed && !HasTerminalEvent() && FindPreviewWindow() != IntPtr.Zero;

        private bool HasTerminalEvent()
        {
            if (_mediaEvent is null)
                return false;

            while (_mediaEvent.GetEvent(out var eventCode, out var param1, out var param2, 0) == 0)
            {
                try
                {
                    if (eventCode is EventCode.UserAbort or EventCode.WindowDestroyed or EventCode.ErrorAbort or EventCode.ErrorAbortEx)
                        return true;
                }
                finally
                {
                    _mediaEvent.FreeEventParams(eventCode, param1, param2);
                }
            }

            return false;
        }

        private void ApplyTopMost()
        {
            try
            {
                var handle = FindPreviewWindow();
                if (handle != IntPtr.Zero)
                    SetWindowPos(handle, _topMost ? HwndTopMost : HwndNoTopMost, 0, 0, 0, 0, SetWindowPosFlags);
            }
            catch
            {
                // Topmost handling is best-effort for the native DirectShow renderer window.
            }
        }

        private IntPtr FindPreviewWindow() =>
            string.IsNullOrWhiteSpace(_title) ? IntPtr.Zero : FindWindow(null, _title);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                _mediaControl?.Stop();
            }
            catch
            {
                // Best-effort cleanup for native DirectShow preview resources.
            }
            finally
            {
                ReleaseComObject(_videoWindow);
                ReleaseComObject(_mediaEvent);
                ReleaseComObject(_mediaControl);
                ReleaseComObject(_source);
                ReleaseComObject(_captureGraphBuilder);
                ReleaseComObject(_graph);
                _videoWindow = null;
                _mediaEvent = null;
                _mediaControl = null;
                _source = null;
                _captureGraphBuilder = null;
                _graph = null;
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }

        private static readonly IntPtr HwndTop = new(0);
        private static readonly IntPtr HwndTopMost = new(-1);
        private static readonly IntPtr HwndNoTopMost = new(-2);
        private const uint SetWindowPosNoSize = 0x0001;
        private const uint SetWindowPosNoMove = 0x0002;
        private const uint SetWindowPosNoActivate = 0x0010;
        private const uint SetWindowPosShowWindow = 0x0040;
        private const uint SetWindowPosFlags =
            SetWindowPosNoSize |
            SetWindowPosNoMove |
            SetWindowPosNoActivate |
            SetWindowPosShowWindow;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }
}
