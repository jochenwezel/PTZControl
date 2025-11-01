#pragma once
using namespace System;
using namespace System::Collections::Generic;

namespace PTZControlBridge
{
    public enum class CameraProperty
    {
        Pan = 0,
        Tilt = 1,
        Zoom = 3
    };

    public value struct Range
    {
        int Min;
        int Max;
        int Step;
        int Default;
    };

    public ref class LogitechPtz sealed
    {
    public:
        // Geräte-Auflistung
        static array<String^>^ EnumerateCameras();

        // Standard UVC-PTZ
        static Range GetRange(String^ cameraNamePart, CameraProperty prop);
        static void SetPanTiltZoom(String^ cameraNamePart, Nullable<int> pan, Nullable<int> tilt, Nullable<int> zoom);

        // Logitech-spezifisch (XU) - Platzhalter/Stub (IKsControl)
        static void UseLogitechMotionControl(String^ cameraNamePart, bool enable);
        static void SavePreset(String^ cameraNamePart, int presetIndex);
        static void RecallPreset(String^ cameraNamePart, int presetIndex);
    };
}