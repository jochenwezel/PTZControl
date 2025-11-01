#pragma once
using namespace System;
using namespace System::Collections::Generic;

namespace PTZControlBridge
{
    public ref class LogitechPtz sealed
    {
    public:
        static array<String^>^ EnumerateCameras();
        static void SetPanTiltZoom(String^ cameraNamePart, Nullable<int> pan, Nullable<int> tilt, Nullable<int> zoom);
    };
}