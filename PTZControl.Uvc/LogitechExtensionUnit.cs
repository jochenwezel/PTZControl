using System;
using System.Linq;
using System.Runtime.InteropServices;
using DirectShowLib;

namespace PTZControl.Uvc
{
    internal static class LogitechExtensionUnit
    {
        private static readonly Guid KsNodeTypeDevSpecific = new("941C7AC0-C559-11D0-8A2B-00A0C9255AC1");
        private static readonly Guid LogitechXuVideoPipeControl = new("49E40215-F434-47FE-B158-0E885023E51B");
        private static readonly Guid LogitechXuPeripheralControl = new("FFE52D21-8030-4E2C-82D9-F587D00540BD");

        private const int XuVideoFwZoomControl = 0x06;
        private const int XuPeripheralControlPanTiltRelativeControl = 0x01;
        private const int XuPeripheralControlPanTiltModeControl = 0x02;
        private const int KsPropertyTypeSet = 0x00000002;
        private const int KsPropertyTypeSetSupport = 0x00000100;
        private const int KsPropertyTypeTopology = 0x10000000;
        private const int NumPresets = 8;

        public static void SavePreset(string cameraNamePart, int presetNumber) =>
            SetPresetMode(cameraNamePart, PresetNumberToModeValue(presetNumber, 4));

        public static void MoveRelativePanTilt(string cameraNamePart, int? x, int? y)
        {
            if (x is null && y is null)
                return;

            var panDirection = Math.Sign(x ?? 0);
            var tiltDirection = Math.Sign(y ?? 0);
            var value = CreatePanTiltRelativeValue(panDirection, tiltDirection);
            SetPeripheralControl(cameraNamePart, XuPeripheralControlPanTiltRelativeControl, value);
        }

        public static void RestorePreset(string cameraNamePart, int presetNumber)
        {
            if (presetNumber < 0 || presetNumber > NumPresets)
                throw new ArgumentOutOfRangeException(nameof(presetNumber), $"Preset number must be between 0 and {NumPresets}.");

            if (presetNumber == 0)
            {
                GotoHome(cameraNamePart);
                return;
            }

            SetPresetMode(cameraNamePart, PresetNumberToModeValue(presetNumber, 12));
        }

        private static int PresetNumberToModeValue(int presetNumber, int baseValue)
        {
            if (presetNumber < 1 || presetNumber > NumPresets)
                throw new ArgumentOutOfRangeException(nameof(presetNumber), $"Preset number must be between 1 and {NumPresets}.");
            return baseValue + presetNumber - 1;
        }

        private static void SetPresetMode(string cameraNamePart, int value)
            => SetPeripheralControl(cameraNamePart, XuPeripheralControlPanTiltModeControl, value);

        private static void SetPeripheralControl(string cameraNamePart, int propertyId, int value)
        {
            var ksControl = GetKsControl(cameraNamePart);
            var peripheralNodeId = FindExtensionUnitNodeId(ksControl, LogitechXuPeripheralControl, "Logitech peripheral control");
            SetExtensionUnitProperty(ksControl, LogitechXuPeripheralControl, peripheralNodeId, propertyId, value);
        }

        private static int CreatePanTiltRelativeValue(int xDirection, int yDirection)
        {
            var pan = DirectionToSignedByte(xDirection);
            var tilt = DirectionToSignedByte(yDirection == 0 ? 0 : -yDirection);
            return (tilt << 24) | (pan << 8);
        }

        private static int DirectionToSignedByte(int direction) =>
            direction switch
            {
                < 0 => unchecked((byte)-1),
                > 0 => 1,
                _ => 0
            };

        private static void GotoHome(string cameraNamePart)
        {
            var ksControl = GetKsControl(cameraNamePart);
            var videoPipeNodeId = FindExtensionUnitNodeId(ksControl, LogitechXuVideoPipeControl, "Logitech video pipe control");
            var peripheralNodeId = FindExtensionUnitNodeId(ksControl, LogitechXuPeripheralControl, "Logitech peripheral control");
            SetExtensionUnitProperty(ksControl, LogitechXuVideoPipeControl, videoPipeNodeId, XuVideoFwZoomControl, 0);
            SetExtensionUnitProperty(ksControl, LogitechXuPeripheralControl, peripheralNodeId, XuPeripheralControlPanTiltModeControl, 3);
        }

        private static void SetExtensionUnitProperty(IKsControl ksControl, Guid propertySet, int nodeId, int propertyId, int value)
        {
            var property = new KspNode
            {
                Property = new KsProperty
                {
                    Set = propertySet,
                    Id = propertyId,
                    Flags = KsPropertyTypeSet | KsPropertyTypeTopology
                },
                NodeId = nodeId
            };

            var hr = ksControl.KsProperty(ref property, Marshal.SizeOf<KspNode>(), ref value, sizeof(int), out _);
            if (hr != 0)
                throw new NotSupportedException("Logitech extension unit property konnte auf dieser Kamera nicht gesetzt werden.", Marshal.GetExceptionForHR(hr));
        }

        private static IKsControl GetKsControl(string cameraNamePart)
        {
            var camera = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)
                .FirstOrDefault(d => d.Name.Contains(cameraNamePart, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Kamera '{cameraNamePart}' nicht gefunden.");

            var iid = typeof(IKsControl).GUID;
            object control;
            try
            {
                camera.Mon.BindToObject(null!, null!, ref iid, out control);
            }
            catch (COMException ex)
            {
                throw new NotSupportedException("IKsControl wird von dieser Kamera nicht unterstützt.", ex);
            }
            return control as IKsControl
                ?? throw new NotSupportedException("IKsControl wird von dieser Kamera nicht unterstützt.");
        }

        private static int FindExtensionUnitNodeId(IKsControl ksControl, Guid propertySet, string displayName)
        {
            if (ksControl is not IKsTopologyInfo topologyInfo)
                throw new NotSupportedException("IKsTopologyInfo wird von dieser Kamera nicht unterstützt.");

            var hr = topologyInfo.get_NumNodes(out var nodeCount);
            if (hr != 0)
                throw new NotSupportedException("IKsTopologyInfo konnte die Kamera-Nodes nicht lesen.", Marshal.GetExceptionForHR(hr));

            for (var nodeId = 0; nodeId < nodeCount; nodeId++)
            {
                hr = topologyInfo.get_NodeType(nodeId, out var nodeType);
                if (hr != 0 || nodeType != KsNodeTypeDevSpecific)
                    continue;

                if (IsExtensionUnitSupported(ksControl, propertySet, nodeId))
                    return nodeId;
            }

            throw new NotSupportedException($"{displayName} extension unit wird von dieser Kamera nicht unterstützt.");
        }

        private static bool IsExtensionUnitSupported(IKsControl ksControl, Guid propertySet, int nodeId)
        {
            var property = new KspNode
            {
                Property = new KsProperty
                {
                    Set = propertySet,
                    Id = 0,
                    Flags = KsPropertyTypeSetSupport | KsPropertyTypeTopology
                },
                NodeId = nodeId
            };

            var unused = 0;
            var hr = ksControl.KsProperty(ref property, Marshal.SizeOf<KspNode>(), ref unused, 0, out _);
            return hr >= 0;
        }

        [ComImport]
        [Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IKsControl
        {
            [PreserveSig]
            int KsProperty(
                ref KspNode property,
                int propertyLength,
                ref int propertyData,
                int dataLength,
                out int bytesReturned);

            [PreserveSig]
            int KsMethod(IntPtr method, int methodLength, IntPtr methodData, int dataLength, out int bytesReturned);

            [PreserveSig]
            int KsEvent(IntPtr @event, int eventLength, IntPtr eventData, int dataLength, out int bytesReturned);
        }

        [ComImport]
        [Guid("720D4AC0-7533-11D0-A5D6-28DB04C10000")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IKsTopologyInfo
        {
            [PreserveSig]
            int get_NumCategories(out int categories);

            [PreserveSig]
            int get_Category(int index, out Guid category);

            [PreserveSig]
            int get_NumConnections(out int connections);

            [PreserveSig]
            int get_ConnectionInfo(int index, IntPtr connectionInfo);

            [PreserveSig]
            int get_NodeName(int nodeId, IntPtr nodeName, int bufferSize, out int nameLength);

            [PreserveSig]
            int get_NumNodes(out int nodes);

            [PreserveSig]
            int get_NodeType(int nodeId, out Guid nodeType);

            [PreserveSig]
            int CreateNodeInstance(int nodeId, ref Guid iid, out object instance);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KsProperty
        {
            public Guid Set;
            public int Id;
            public int Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KspNode
        {
            public KsProperty Property;
            public int NodeId;
            public int Reserved;
        }
    }
}
