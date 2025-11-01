#include "LogitechPtz.h"

#using "..\\PTZControl.Uvc\\bin\\Debug\\net8.0-windows10.0.19041.0\\PTZControl.Uvc.dll"

using namespace PTZControlBridge;

public ref class _UvcShim
{
public:
    static array<String^>^ Enumerate()
    {
        auto list = gcnew System::Collections::Generic::List<String^>();
        for each (auto cam in PTZControl::Uvc::UvcCamera::Enumerate())
            list->Add(cam->Name);
        return list->ToArray();
    }
    static void SetPTZ(String^ name, System::Nullable<int> pan, System::Nullable<int> tilt, System::Nullable<int> zoom)
    {
        PTZControl::Uvc::UvcCamera::SetPanTiltZoom(name, pan.HasValue ? pan.Value : (int)0,
                                                          tilt.HasValue ? tilt.Value : (int)0,
                                                          zoom.HasValue ? zoom.Value : (int)0);
    }
};

array<String^>^ LogitechPtz::EnumerateCameras()
{
    return _UvcShim::Enumerate();
}

void LogitechPtz::SetPanTiltZoom(String^ cameraNamePart, Nullable<int> pan, Nullable<int> tilt, Nullable<int> zoom)
{
    _UvcShim::SetPTZ(cameraNamePart, pan, tilt, zoom);
}