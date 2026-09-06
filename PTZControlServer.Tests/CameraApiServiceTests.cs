using PTZControl.Core;
using PTZControl.Uvc;
using Xunit;

namespace PTZControlServer.Tests;

public sealed class CameraApiServiceTests
{
    [Fact]
    public async Task MoveAbsolutePercent_MapsToRawRange()
    {
        var backend = new FakeCameraBackend();
        var service = new CameraApiService(backend);
        await service.MoveAsync(1, 25, 75, ValueMode.Percent, relative: false);
        Assert.Equal(-5, backend.LastPan);
        Assert.Equal(5, backend.LastTilt);
    }

    [Fact]
    public async Task ZoomRelativeRaw_ClampsAtDeviceMaximum()
    {
        var backend = new FakeCameraBackend { Zoom = 4 };
        var service = new CameraApiService(backend);
        await service.SetZoomAsync(1, 10, ValueMode.Raw, relative: true);
        Assert.Equal(5, backend.LastZoom);
    }

    [Theory]
    [InlineData("percent", ValueMode.Percent)]
    [InlineData("raw", ValueMode.Raw)]
    public void ParseMode_AcceptsDocumentedValues(string value, ValueMode expected) =>
        Assert.Equal(expected, CameraApiService.ParseMode(value));

    private sealed class FakeCameraBackend : ICameraBackend
    {
        public int Zoom { get; set; } = 1;
        public int? LastPan { get; private set; }
        public int? LastTilt { get; private set; }
        public int? LastZoom { get; private set; }

        public IReadOnlyList<CameraInfo> Enumerate() => [new() { Name = "Test camera", MonikerString = "test-device" }];
        public (int min, int max, int step, int def) GetRange(string camera, CameraProperty property) => property == CameraProperty.Zoom ? (1, 5, 1, 1) : (-10, 10, 1, 0);
        public int GetValue(string camera, CameraProperty property) => property == CameraProperty.Zoom ? Zoom : 0;
        public void SetPanTiltZoom(string camera, int? pan = null, int? tilt = null, int? zoom = null) { LastPan = pan; LastTilt = tilt; LastZoom = zoom; }
        public string GetDirectShowCameraName(string devicePath) => throw new NotSupportedException();
        public void SetDirectShowCameraName(string devicePath, string friendlyName) => throw new NotSupportedException();
        public (int min, int max, int step, int def) GetVideoProcessingRange(string camera, VideoProcessingProperty property) => throw new NotSupportedException();
        public int GetVideoProcessingValue(string camera, VideoProcessingProperty property) => throw new NotSupportedException();
        public void SetVideoProcessingValue(string camera, VideoProcessingProperty property, int value) => throw new NotSupportedException();
        public void MoveRelativeZoom(string camera, int deltaPercent) => throw new NotSupportedException();
        public void MoveRelativePanTilt(string camera, int? x = null, int? y = null) => throw new NotSupportedException();
        public void RestoreHome(string camera, bool zoom, bool move) => throw new NotSupportedException();
        public void RestoreDefault(string camera, bool zoom, bool pan, bool tilt) => throw new NotSupportedException();
        public void SavePreset(string camera, int presetNumber) => throw new NotSupportedException();
        public void RestorePreset(string camera, int presetNumber) => throw new NotSupportedException();
    }
}
