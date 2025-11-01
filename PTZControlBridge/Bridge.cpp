#include "Bridge.h"
#include "LogitechXuGuids.h"

#using "..\\PTZControl.Uvc\\bin\\Debug\\net8.0-windows10.0.19041.0\\PTZControl.Uvc.dll"

using namespace PTZControlBridge;

array<String^>^ LogitechPtz::EnumerateCameras()
{
    auto list = gcnew System::Collections::Generic::List<String^>();
    for each (auto cam in PTZControl::Uvc::UvcCamera::Enumerate())
        list->Add(cam->Name);
    return list->ToArray();
}

Range LogitechPtz::GetRange(String^ cameraNamePart, CameraProperty prop)
{
    auto p = (PTZControl::Uvc::CameraProperty)(int)prop;
    auto r = PTZControl::Uvc::UvcCamera::GetRange(cameraNamePart, p);
    Range rr; rr.Min = r.Item1; rr.Max = r.Item2; rr.Step = r.Item3; rr.Default = r.Item4;
    return rr;
}

void LogitechPtz::SetPanTiltZoom(String^ cameraNamePart, Nullable<int> pan, Nullable<int> tilt, Nullable<int> zoom)
{
    PTZControl::Uvc::UvcCamera::SetPanTiltZoom(cameraNamePart,
        pan.HasValue ? pan.Value : (int)0,
        tilt.HasValue ? tilt.Value : (int)0,
        zoom.HasValue ? zoom.Value : (int)0);
}

// ---- Logitech-spezifische Stubs (IKsControl/XU) ----
// TODO: Ersetzen durch echte Implementierung gegen IKsControl und XU-GUIDs.

void LogitechPtz::UseLogitechMotionControl(String^ cameraNamePart, bool enable)
{
    throw gcnew System::NotSupportedException("Logitech Motion Control (XU) noch nicht implementiert.");
}

void LogitechPtz::SavePreset(String^ cameraNamePart, int presetIndex)
{
    throw gcnew System::NotSupportedException("Logitech Preset Save (XU) noch nicht implementiert.");
}

void LogitechPtz::RecallPreset(String^ cameraNamePart, int presetIndex)
{
    throw gcnew System::NotSupportedException("Logitech Preset Recall (XU) noch nicht implementiert.");
}