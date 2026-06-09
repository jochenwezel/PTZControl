using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Win32;
using PTZControl.Core;
using PTZControl.Uvc;

namespace PTZControl2;

public sealed partial class MainWindow : Window
{
    private const string SettingsRegistryPath = @"SOFTWARE\MRi-Software\PTZControl\Window";
    private const string DeviceRegistryPath = @"SOFTWARE\MRi-Software\PTZControl\Device";
    private const string OptionsRegistryPath = @"SOFTWARE\MRi-Software\PTZControl\Options";
    private const string InvertPanValueNameFormat = "PTZControl2InvertPan{0}";
    private const string InvertTiltValueNameFormat = "PTZControl2InvertTilt{0}";
    private const string EffectiveZoomRawMaxValueNameFormat = "PTZControl2EffectiveZoomRawMax{0}";
    private const string ThemeModeValueName = "PTZControl2ThemeMode";
    private const string ShowOnlyLogitechCamerasValueName = "PTZControl2ShowOnlyLogitechCameras";
    private const string PreviewTopMostValueName = "PTZControl2PreviewTopMost";
    private const string LogitechControlValueName = "LogitechMotionControl";
    private const string MotorIntervalTimerValueName = "MotorIntervalTimer";
    private const string NoResetValueName = "NoReset";

    private readonly ICameraBackend _backend = CameraBackendFactory.Create();
    private readonly Dictionary<string, CameraInfo> _cameraByLabel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _startupHomeResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CameraListItem> _displayedCameras = new();
    private readonly List<Button> _presetButtons = new();
    private readonly List<TextBlock> _presetLabels = new();
    private readonly HashSet<Key> _pressedActionKeys = new();
    private readonly Dictionary<VideoProcessingProperty, PictureControls> _pictureControls = new();
    private readonly HashSet<string> _presetUnsupportedCameraKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly StartupOptions _startupOptions;
    private readonly DispatcherTimer _successStatusClearTimer;
    private Border _statusBorder = null!;
    private ComboBox _cameraSelector = null!;
    private TextBlock _statusText = null!;
    private Button _moveZoomTabButton = null!;
    private Button _pictureTabButton = null!;
    private StackPanel _moveZoomTabPanel = null!;
    private StackPanel _pictureTabPanel = null!;
    private Slider _stepSlider = null!;
    private TextBlock _stepValueText = null!;
    private Button _memoryButton = null!;
    private Button _homeButton = null!;
    private Button _defaultAllButton = null!;
    private Button _panLeftButton = null!;
    private Button _panRightButton = null!;
    private Button _tiltUpButton = null!;
    private Button _tiltDownButton = null!;
    private Button _zoomInButton = null!;
    private Button _zoomOutButton = null!;
    private Button _previewButton = null!;
    private TextBlock _pictureStatusText = null!;
    private DirectShowPreviewSession? _previewSession;
    private CameraInfoDialog? _cameraInfoDialog;
    private HelpDialog? _helpDialog;
    private AboutDialog? _aboutDialog;
    private int _ownedWindowOffsetIndex;
    private bool _invertPan;
    private bool _invertTilt;
    private int? _effectiveZoomRawMax;
    private bool _logitechControl;
    private bool _showOnlyLogitechCameras;
    private bool _previewTopMost;
    private bool _noReset;
    private bool _startupActionsApplied;
    private int _motorTime = 70;
    private string _themeMode = "System";
    private bool _memoryMode;
    private bool _loadingPictureControls;
    private string? _startupSelectionWarning;

    private sealed record CameraListItem(int Slot, CameraInfo Camera);

    private sealed record PictureControls(
        Slider Slider,
        TextBlock ValueText,
        int DefaultValue,
        VideoProcessingProperty Property);

    private sealed record CameraCapabilities(
        bool Zoom,
        bool Pan,
        bool Tilt,
        bool Presets,
        bool? HomeResult,
        string? ZoomError,
        string? PanError,
        string? TiltError)
    {
        public bool Move => Pan || Tilt;
        public bool Home => HomeResult ?? (Pan && Tilt);
        public bool DriverDefault => Zoom && Pan && Tilt;
    }

    public MainWindow()
        : this(new StartupOptions())
    {
    }

    public MainWindow(StartupOptions startupOptions)
    {
        _startupOptions = startupOptions;
        _successStatusClearTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _successStatusClearTimer.Tick += SuccessStatusClearTimer_Tick;
        InitializeComponent();
        Opened += (_, _) => RefreshCameras();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _cameraSelector = this.FindControl<ComboBox>("CameraSelector")
            ?? throw new InvalidOperationException("CameraSelector control not found.");
        _statusBorder = this.FindControl<Border>("StatusBorder")
            ?? throw new InvalidOperationException("StatusBorder control not found.");
        _statusText = this.FindControl<TextBlock>("StatusText")
            ?? throw new InvalidOperationException("StatusText control not found.");
        _moveZoomTabButton = this.FindControl<Button>("MoveZoomTabButton")
            ?? throw new InvalidOperationException("MoveZoomTabButton control not found.");
        _pictureTabButton = this.FindControl<Button>("PictureTabButton")
            ?? throw new InvalidOperationException("PictureTabButton control not found.");
        _moveZoomTabPanel = this.FindControl<StackPanel>("MoveZoomTabPanel")
            ?? throw new InvalidOperationException("MoveZoomTabPanel control not found.");
        _pictureTabPanel = this.FindControl<StackPanel>("PictureTabPanel")
            ?? throw new InvalidOperationException("PictureTabPanel control not found.");
        _stepSlider = this.FindControl<Slider>("StepSlider")
            ?? throw new InvalidOperationException("StepSlider control not found.");
        _stepValueText = this.FindControl<TextBlock>("StepValueText")
            ?? throw new InvalidOperationException("StepValueText control not found.");
        _memoryButton = this.FindControl<Button>("MemoryButton")
            ?? throw new InvalidOperationException("MemoryButton control not found.");
        _homeButton = this.FindControl<Button>("HomeButton")
            ?? throw new InvalidOperationException("HomeButton control not found.");
        _defaultAllButton = this.FindControl<Button>("DefaultAllButton")
            ?? throw new InvalidOperationException("DefaultAllButton control not found.");
        _panLeftButton = this.FindControl<Button>("PanLeftButton")
            ?? throw new InvalidOperationException("PanLeftButton control not found.");
        _panRightButton = this.FindControl<Button>("PanRightButton")
            ?? throw new InvalidOperationException("PanRightButton control not found.");
        _tiltUpButton = this.FindControl<Button>("TiltUpButton")
            ?? throw new InvalidOperationException("TiltUpButton control not found.");
        _tiltDownButton = this.FindControl<Button>("TiltDownButton")
            ?? throw new InvalidOperationException("TiltDownButton control not found.");
        _zoomInButton = this.FindControl<Button>("ZoomInButton")
            ?? throw new InvalidOperationException("ZoomInButton control not found.");
        _zoomOutButton = this.FindControl<Button>("ZoomOutButton")
            ?? throw new InvalidOperationException("ZoomOutButton control not found.");
        _previewButton = this.FindControl<Button>("PreviewButton")
            ?? throw new InvalidOperationException("PreviewButton control not found.");
        _pictureStatusText = this.FindControl<TextBlock>("PictureStatusText")
            ?? throw new InvalidOperationException("PictureStatusText control not found.");
        AddPictureControls(VideoProcessingProperty.Brightness, "BrightnessSlider", "BrightnessValueText");
        AddPictureControls(VideoProcessingProperty.Contrast, "ContrastSlider", "ContrastValueText");
        AddPictureControls(VideoProcessingProperty.Sharpness, "SharpnessSlider", "SharpnessValueText");
        AddPictureControls(VideoProcessingProperty.Saturation, "SaturationSlider", "SaturationValueText");
        _previewButton.IsVisible = OperatingSystem.IsWindows();
        for (var preset = 1; preset <= 8; preset++)
        {
            _presetButtons.Add(this.FindControl<Button>($"Preset{preset}Button")
                ?? throw new InvalidOperationException($"Preset{preset}Button control not found."));
            _presetLabels.Add(this.FindControl<TextBlock>($"Preset{preset}Text")
                ?? throw new InvalidOperationException($"Preset{preset}Text control not found."));
        }

        _stepSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                _stepValueText.Text = $"{StepPercent}";
                _statusText.Text = $"Step size: {StepPercent}";
            }
        };

        LoadSettings();
        Closed += (_, _) =>
        {
            ClosePreviewSession();
            _cameraInfoDialog?.Close();
            _helpDialog?.Close();
            _aboutDialog?.Close();
        };
    }

    private void AddPictureControls(VideoProcessingProperty property, string sliderName, string valueTextName)
    {
        var slider = this.FindControl<Slider>(sliderName)
            ?? throw new InvalidOperationException($"{sliderName} control not found.");
        var valueText = this.FindControl<TextBlock>(valueTextName)
            ?? throw new InvalidOperationException($"{valueTextName} control not found.");
        _pictureControls[property] = new PictureControls(slider, valueText, 0, property);
    }

    private void MoveZoomTabButton_Click(object? sender, RoutedEventArgs e) => SelectLiveControlTab(picture: false);

    private void PictureTabButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectLiveControlTab(picture: true);
        RefreshPictureControls();
    }

    private void SelectLiveControlTab(bool picture)
    {
        _moveZoomTabPanel.IsVisible = !picture;
        _pictureTabPanel.IsVisible = picture;
        _moveZoomTabButton.Classes.Set("selected", !picture);
        _pictureTabButton.Classes.Set("selected", picture);
    }

    private void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        RefreshCameras();
    }

    private void CameraSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        LoadSelectedCameraSettings();
        UpdatePresetLabels();
        var capabilityStatus = UpdateSelectedCameraCapabilities();
        RefreshPictureControls();
        SetStatus(TryGetSelectedCamera(out var camera)
            ? string.IsNullOrWhiteSpace(capabilityStatus)
                ? $"Selected camera: {camera.Name}"
                : $"Selected camera: {camera.Name}. {capabilityStatus}"
            : "Select a camera.");
    }

    private void ShowCameraInfoButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        if (_cameraInfoDialog is not null)
        {
            _cameraInfoDialog.Activate();
            return;
        }

        _cameraInfoDialog = new CameraInfoDialog
        {
            InfoProvider = BuildCameraInfoText
        };
        _cameraInfoDialog.Closed += (_, _) => _cameraInfoDialog = null;
        PositionOwnedWindow(_cameraInfoDialog);
        _cameraInfoDialog.Show(this);
    }

    private void PreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        if (!OperatingSystem.IsWindows())
        {
            SetWarning("Live preview is only available on Windows.");
            return;
        }

        if (!TryGetSelectedCamera(out var camera))
            return;

        try
        {
            if (_previewSession is not null && !_previewSession.IsAlive)
                ClosePreviewSession();

            if (_previewSession is not null)
            {
                ClosePreviewSession();
                SetStatus("Live preview closed.");
                return;
            }

            _previewSession = DirectShowPreviewSession.Start(
                GetCameraKey(camera),
                $"PTZControl2 Preview - {camera.Name}",
                _previewTopMost,
                () => Dispatcher.UIThread.Post(() =>
                {
                    ClosePreviewSession();
                    SetStatus("Live preview closed.");
                }));
            _previewButton.Content = "Close Preview";
            SetStatus($"Live preview opened: {camera.Name}");
        }
        catch (Exception ex)
        {
            ClosePreviewSession();
            SetError("Open live preview failed", ex);
        }
    }

    private void HelpButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        if (_helpDialog is not null)
        {
            _helpDialog.Activate();
            return;
        }

        _helpDialog = new HelpDialog();
        _helpDialog.Closed += (_, _) => _helpDialog = null;
        PositionOwnedWindow(_helpDialog);
        _helpDialog.Show(this);
    }

    private void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        if (_aboutDialog is not null)
        {
            _aboutDialog.Activate();
            return;
        }

        _aboutDialog = new AboutDialog();
        _aboutDialog.Closed += (_, _) => _aboutDialog = null;
        PositionOwnedWindow(_aboutDialog);
        _aboutDialog.Show(this);
    }

    private void PresetButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: not null } button || !int.TryParse(button.Tag.ToString(), out var preset))
            return;

        RunPresetAction(preset);
    }

    private void RunPresetAction(int preset)
    {
        if (!IsActionAvailable(_presetButtons[preset - 1], "Preset commands are not available for the selected camera."))
            return;

        if (_memoryMode)
        {
            SetMemoryMode(false);
            RunPresetCameraAction($"Save preset {preset}", camera => _backend.SavePreset(camera, preset));
            return;
        }

        RunPresetCameraAction($"Restore preset {preset}", camera => _backend.RestorePreset(camera, preset));
    }

    private void TiltUpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_tiltUpButton, "Tilt is not available for the selected camera."))
            RunCameraAction($"Tilt up {StepPercent}", camera => _backend.MoveRelativePanTilt(camera, y: TiltDelta(StepPercent)));
    }

    private void TiltDownButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_tiltDownButton, "Tilt is not available for the selected camera."))
            RunCameraAction($"Tilt down {StepPercent}", camera => _backend.MoveRelativePanTilt(camera, y: TiltDelta(-StepPercent)));
    }

    private void PanLeftButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_panLeftButton, "Pan is not available for the selected camera."))
            RunCameraAction($"Pan left {StepPercent}", camera => _backend.MoveRelativePanTilt(camera, x: PanDelta(-StepPercent)));
    }

    private void PanRightButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_panRightButton, "Pan is not available for the selected camera."))
            RunCameraAction($"Pan right {StepPercent}", camera => _backend.MoveRelativePanTilt(camera, x: PanDelta(StepPercent)));
    }

    private void HomeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_homeButton, "Home is not available for the selected camera."))
            RunCameraAction("Home", camera => _backend.RestoreHome(camera, zoom: false, move: true));
    }

    private void ZoomOutButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_zoomOutButton, "Zoom is not available for the selected camera."))
            RunZoomAction("Zoom out", -StepPercent);
    }

    private void ZoomInButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_zoomInButton, "Zoom is not available for the selected camera."))
            RunZoomAction("Zoom in", StepPercent);
    }

    private void DefaultAllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_defaultAllButton, "Driver default is not available for the selected camera."))
            RunCameraAction("Default all", camera => _backend.RestoreDefault(camera, zoom: true, pan: true, tilt: true));
    }

    private void MemoryButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsActionAvailable(_memoryButton, "Preset commands are not available for the selected camera."))
            SetMemoryMode(!_memoryMode);
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        var slotIndex = GetSelectedCameraSlotIndex();
        var hasSelectedCamera = _cameraSelector.SelectedIndex >= 0 &&
            _cameraSelector.SelectedIndex < _displayedCameras.Count;
        var camera = hasSelectedCamera ? _displayedCameras[_cameraSelector.SelectedIndex].Camera : null;
        var presetNames = slotIndex >= 0
            ? Enumerable.Range(1, 8)
                .Select(preset => ReadPresetName(slotIndex, preset) ?? string.Empty)
                .ToList()
            : Enumerable.Repeat(string.Empty, 8).ToList();
        var dialog = new SettingsDialog
        {
            CameraLabel = camera is null ? "No camera selected." : $"Camera slot {slotIndex + 1}: {camera.Name}",
            InvertPan = slotIndex >= 0 && _invertPan,
            InvertTilt = slotIndex >= 0 && _invertTilt,
            EffectiveZoomRawMax = slotIndex >= 0 ? _effectiveZoomRawMax : null,
            LogitechControl = _logitechControl,
            MotorTime = _motorTime,
            ThemeMode = _themeMode,
            ShowCameraFilterOptions = OperatingSystem.IsWindows(),
            ShowOnlyLogitechCameras = _showOnlyLogitechCameras,
            PreviewTopMost = _previewTopMost,
            PresetNames = presetNames
        };

        if (await dialog.ShowDialog<bool>(this))
        {
            if (slotIndex >= 0)
            {
                _invertPan = dialog.InvertPan;
                _invertTilt = dialog.InvertTilt;
                _effectiveZoomRawMax = dialog.EffectiveZoomRawMax;
            }

            _logitechControl = dialog.LogitechControl;
            _motorTime = dialog.MotorTime;
            _themeMode = dialog.ThemeMode;
            _showOnlyLogitechCameras = OperatingSystem.IsWindows() && dialog.ShowOnlyLogitechCameras;
            _previewTopMost = OperatingSystem.IsWindows() && dialog.PreviewTopMost;
            _previewSession?.SetTopMost(_previewTopMost);
            SaveSettings(slotIndex, dialog.PresetNames);
            ApplyTheme();
            RefreshCameras();
            SetStatus("Settings saved.");
        }
    }

    private void RefreshCameras()
    {
        try
        {
            _cameraByLabel.Clear();
            _displayedCameras.Clear();
            var allCameras = _backend.Enumerate();
            _displayedCameras.AddRange(allCameras
                .Select((camera, index) => new CameraListItem(index + 1, camera))
                .Where(item => ShouldShowCamera(item.Camera)));

            var labels = _displayedCameras.Select(item =>
            {
                var label = $"{item.Slot}: {item.Camera.Name}";
                if (!string.IsNullOrWhiteSpace(item.Camera.MonikerString))
                    label += $" ({item.Camera.MonikerString})";
                _cameraByLabel[label] = item.Camera;
                return label;
            }).ToList();

            _cameraSelector.ItemsSource = labels;
            _cameraSelector.SelectedIndex = SelectStartupCamera(_displayedCameras);
            var startupStatus = BuildStartupStatus(allCameras.Count, labels.Count);
            var selectedCamera = _cameraSelector.SelectedIndex >= 0 && _cameraSelector.SelectedIndex < _displayedCameras.Count
                ? _displayedCameras[_cameraSelector.SelectedIndex].Camera
                : null;
            var resetStatus = ApplyStartupCameraReset(allCameras, selectedCamera);
            UpdatePresetLabels();
            var capabilityStatus = UpdateSelectedCameraCapabilities();
            if (IsRedundantHomeStartupWarning(resetStatus, capabilityStatus))
                resetStatus = string.Empty;

            var refreshStatus = string.IsNullOrWhiteSpace(resetStatus)
                ? startupStatus
                : $"{startupStatus} {resetStatus}";
            if (!string.IsNullOrWhiteSpace(capabilityStatus))
                refreshStatus = $"{refreshStatus} {capabilityStatus}";
            if (!string.IsNullOrWhiteSpace(resetStatus) || capabilityStatus.Contains("except", StringComparison.Ordinal) || capabilityStatus.Contains("none", StringComparison.Ordinal))
                SetWarning(refreshStatus);
            else
                SetStatus(refreshStatus);
        }
        catch (Exception ex)
        {
            SetError("Refresh cameras failed", ex);
            ApplyCameraCapabilities(null);
        }
    }

    private string BuildCameraInfoText()
    {
        if (!TryGetSelectedCamera(out var camera))
            return "No camera selected.";

        var cameraKey = GetCameraKey(camera);
        var lines = new List<string>
        {
            $"Device Name: {camera.Name}",
            $"Device Path: {camera.MonikerString}",
            $"Effective Zoom Raw Max: {(_effectiveZoomRawMax?.ToString() ?? "not set")}",
            ""
        };

        AppendRange(lines, cameraKey, "Zoom", CameraProperty.Zoom);
        AppendRange(lines, cameraKey, "Pan", CameraProperty.Pan);
        AppendRange(lines, cameraKey, "Tilt", CameraProperty.Tilt);
        lines.Add("");
        lines.Add("Video processing:");
        AppendVideoProcessingRange(lines, cameraKey, "Brightness", VideoProcessingProperty.Brightness);
        AppendVideoProcessingRange(lines, cameraKey, "Contrast", VideoProcessingProperty.Contrast);
        AppendVideoProcessingRange(lines, cameraKey, "Sharpness", VideoProcessingProperty.Sharpness);
        AppendVideoProcessingRange(lines, cameraKey, "Saturation", VideoProcessingProperty.Saturation);

        return string.Join(Environment.NewLine, lines);
    }

    private void AppendRange(List<string> lines, string camera, string label, CameraProperty property)
    {
        try
        {
            var range = _backend.GetRange(camera, property);
            var current = _backend.GetValue(camera, property);
            lines.Add($"{label}: {range.min}..{range.max}, step {range.step}, default {range.def}, current {current}");
        }
        catch (Exception ex)
        {
            lines.Add($"{label}: not available ({ex.Message})");
        }
    }

    private void AppendVideoProcessingRange(List<string> lines, string camera, string label, VideoProcessingProperty property)
    {
        try
        {
            var range = _backend.GetVideoProcessingRange(camera, property);
            var current = _backend.GetVideoProcessingValue(camera, property);
            lines.Add($"{label}: {range.min}..{range.max}, step {range.step}, default {range.def}, current {current}");
        }
        catch (Exception ex)
        {
            lines.Add($"{label}: not available ({ex.Message})");
        }
    }

    private void RefreshPictureControls()
    {
        if (!TryGetSelectedCameraQuiet(out var camera))
        {
            foreach (var controls in _pictureControls.Values)
            {
                controls.Slider.IsEnabled = false;
                controls.ValueText.Text = "n/a";
            }

            _pictureStatusText.Text = "No camera selected.";
            return;
        }

        var cameraKey = GetCameraKey(camera);
        _loadingPictureControls = true;
        try
        {
            var unavailable = new List<string>();
            foreach (var property in _pictureControls.Keys.ToList())
            {
                var controls = _pictureControls[property];
                try
                {
                    var range = _backend.GetVideoProcessingRange(cameraKey, property);
                    var current = _backend.GetVideoProcessingValue(cameraKey, property);
                    controls = controls with { DefaultValue = range.def };
                    _pictureControls[property] = controls;
                    controls.Slider.Minimum = range.min;
                    controls.Slider.Maximum = range.max;
                    controls.Slider.TickFrequency = Math.Max(1, range.step);
                    controls.Slider.IsSnapToTickEnabled = range.step > 1;
                    controls.Slider.Value = Math.Clamp(current, range.min, range.max);
                    controls.Slider.IsEnabled = true;
                    controls.ValueText.Text = current.ToString();
                }
                catch
                {
                    controls.Slider.IsEnabled = false;
                    controls.ValueText.Text = "n/a";
                    unavailable.Add(property.ToString().ToLowerInvariant());
                }
            }

            _pictureStatusText.Text = unavailable.Count == 0
                ? "Picture controls loaded."
                : $"Unavailable: {string.Join(", ", unavailable)}.";
        }
        finally
        {
            _loadingPictureControls = false;
        }
    }

    private void PictureSlider_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_loadingPictureControls ||
            e.Property != Slider.ValueProperty ||
            sender is not Slider slider ||
            !TryGetSelectedCameraQuiet(out var camera))
        {
            return;
        }

        var match = _pictureControls.Values.FirstOrDefault(controls => ReferenceEquals(controls.Slider, slider));
        if (match is null)
            return;

        var value = (int)Math.Round(slider.Value);
        match.ValueText.Text = value.ToString();
        try
        {
            _backend.SetVideoProcessingValue(GetCameraKey(camera), match.Property, value);
            _pictureStatusText.Text = $"{match.Property}: {value}";
        }
        catch (Exception ex)
        {
            _pictureStatusText.Text = $"{match.Property}: {ex.Message}";
        }
    }

    private void ResetPictureDefaultsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedCamera(out var camera))
            return;

        var cameraKey = GetCameraKey(camera);
        foreach (var controls in _pictureControls.Values.Where(controls => controls.Slider.IsEnabled))
            _backend.SetVideoProcessingValue(cameraKey, controls.Property, controls.DefaultValue);

        RefreshPictureControls();
        _pictureStatusText.Text = "Picture controls reset to defaults.";
    }

    private string UpdateSelectedCameraCapabilities()
    {
        if (!TryGetSelectedCamera(out var camera))
        {
            ApplyCameraCapabilities(null);
            return string.Empty;
        }

        var capabilities = ProbeCameraCapabilities(GetCameraKey(camera));
        ApplyCameraCapabilities(capabilities);
        return BuildCapabilityWarning(capabilities);
    }

    private CameraCapabilities ProbeCameraCapabilities(string camera)
    {
        var zoom = ProbeCameraProperty(camera, CameraProperty.Zoom);
        var pan = ProbeCameraProperty(camera, CameraProperty.Pan);
        var tilt = ProbeCameraProperty(camera, CameraProperty.Tilt);
        return new CameraCapabilities(
            zoom.Supported,
            pan.Supported,
            tilt.Supported,
            !_presetUnsupportedCameraKeys.Contains(camera),
            _startupHomeResults.TryGetValue(camera, out var startupHomeResult) ? startupHomeResult : null,
            zoom.Error,
            pan.Error,
            tilt.Error);
    }

    private (bool Supported, string? Error) ProbeCameraProperty(string camera, CameraProperty property)
    {
        try
        {
            _backend.GetRange(camera, property);
            _backend.GetValue(camera, property);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private void ApplyCameraCapabilities(CameraCapabilities? capabilities)
    {
        var hasCamera = capabilities is not null;
        var zoom = capabilities?.Zoom == true;
        var pan = capabilities?.Pan == true;
        var tilt = capabilities?.Tilt == true;
        var presets = capabilities?.Presets == true;
        var home = capabilities?.Home == true;
        var driverDefault = capabilities?.DriverDefault == true;

        foreach (var button in _presetButtons)
            button.IsEnabled = presets;

        _memoryButton.IsEnabled = presets;
        _homeButton.IsEnabled = home;
        _defaultAllButton.IsEnabled = driverDefault;
        _panLeftButton.IsEnabled = pan;
        _panRightButton.IsEnabled = pan;
        _tiltUpButton.IsEnabled = tilt;
        _tiltDownButton.IsEnabled = tilt;
        _zoomInButton.IsEnabled = zoom;
        _zoomOutButton.IsEnabled = zoom;
        _stepSlider.IsEnabled = hasCamera && (zoom || pan || tilt);
    }

    private static string BuildCapabilityWarning(CameraCapabilities capabilities)
    {
        var allCapabilities = new[]
        {
            ("presets", capabilities.Presets),
            ("pan", capabilities.Pan),
            ("tilt", capabilities.Tilt),
            ("zoom", capabilities.Zoom),
            ("home", capabilities.Home),
            ("driver default", capabilities.DriverDefault)
        };

        var available = allCapabilities
            .Where(capability => capability.Item2)
            .Select(capability => capability.Item1)
            .ToList();
        var unavailable = allCapabilities
            .Where(capability => !capability.Item2)
            .Select(capability => capability.Item1)
            .ToList();

        if (available.Count == 0)
            return "Capabilities: none.";

        if (unavailable.Count == 0)
            return "Capabilities: all.";

        if (unavailable.Count <= 2)
            return $"Capabilities: all except {string.Join(", ", unavailable)}.";

        return $"Capabilities: {string.Join(", ", available)}.";
    }

    private static bool IsRedundantHomeStartupWarning(string resetStatus, string capabilityStatus) =>
        resetStatus.StartsWith("Restore home position of selected camera failed", StringComparison.Ordinal) &&
        CapabilityStatusReportsMissingHome(capabilityStatus);

    private static bool CapabilityStatusReportsMissingHome(string capabilityStatus)
    {
        if (!capabilityStatus.StartsWith("Capabilities:", StringComparison.Ordinal))
            return false;

        if (capabilityStatus.Equals("Capabilities: none.", StringComparison.Ordinal))
            return true;

        if (capabilityStatus.StartsWith("Capabilities: all except ", StringComparison.Ordinal))
            return capabilityStatus.Contains("home", StringComparison.Ordinal);

        return !capabilityStatus.Contains("home", StringComparison.Ordinal);
    }

    private (int before, int after) ChangeZoom(string camera, int deltaPercent)
    {
        var range = _backend.GetRange(camera, CameraProperty.Zoom);
        var current = _backend.GetValue(camera, CameraProperty.Zoom);
        var effectiveMax = _effectiveZoomRawMax is { } configuredMax
            ? Math.Clamp(configuredMax, range.min, range.max)
            : range.max;
        var effectiveCurrent = Math.Clamp(current, range.min, effectiveMax);
        var delta = (int)Math.Round((effectiveMax - range.min) * (deltaPercent / 100.0));
        if (delta == 0 && deltaPercent != 0 && effectiveMax > range.min)
            delta = Math.Sign(deltaPercent) * Math.Max(1, range.step);

        var zoom = Math.Clamp(effectiveCurrent + delta, range.min, effectiveMax);
        _backend.SetPanTiltZoom(camera, zoom: zoom);
        var after = _backend.GetValue(camera, CameraProperty.Zoom);
        return (current, after);
    }

    private void RunCameraAction(string actionName, Action<string> action)
    {
        if (!TryGetSelectedCamera(out var camera))
            return;

        try
        {
            CancelMemoryModeForOtherAction();
            action(GetCameraKey(camera));
            SetStatus($"{actionName}: OK");
        }
        catch (Exception ex)
        {
            SetError(actionName, ex);
        }
    }

    private void RunZoomAction(string actionName, int deltaPercent)
    {
        if (!TryGetSelectedCamera(out var camera))
            return;

        try
        {
            CancelMemoryModeForOtherAction();
            var (before, after) = ChangeZoom(GetCameraKey(camera), deltaPercent);
            SetStatus($"{actionName}: OK (raw {before} -> {after})");
        }
        catch (Exception ex)
        {
            SetError(actionName, ex);
        }
    }

    private void RunPresetCameraAction(string actionName, Action<string> action)
    {
        if (!TryGetSelectedCamera(out var camera))
            return;

        var cameraKey = GetCameraKey(camera);
        try
        {
            CancelMemoryModeForOtherAction();
            action(cameraKey);
            SetStatus($"{actionName}: OK");
        }
        catch (Exception ex) when (IsPresetCapabilityFailure(ex))
        {
            _presetUnsupportedCameraKeys.Add(cameraKey);
            ApplyCameraCapabilities(ProbeCameraCapabilities(cameraKey));
            SetError($"{actionName} failed; preset commands disabled for this camera", ex);
        }
        catch (Exception ex)
        {
            SetError(actionName, ex);
        }
    }

    private static bool IsPresetCapabilityFailure(Exception ex) =>
        ex is NotSupportedException ||
        ex.Message.Contains("extension unit", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("peripheral control", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("nicht gesetzt", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("nicht unterstützt", StringComparison.OrdinalIgnoreCase);

    private bool IsActionAvailable(Button button, string unavailableMessage)
    {
        if (button.IsEnabled)
            return true;

        CancelMemoryModeForOtherAction();
        SetWarning(unavailableMessage);
        return false;
    }

    private bool TryGetSelectedCamera(out CameraInfo camera)
    {
        if (TryGetSelectedCameraQuiet(out camera))
            return true;

        camera = null!;
        SetWarning(_cameraByLabel.Count == 0
            ? "No camera available. Connect a camera and click Refresh."
            : "Select a camera first.");
        return false;
    }

    private bool TryGetSelectedCameraQuiet(out CameraInfo camera)
    {
        if (_cameraSelector.SelectedItem is string label && _cameraByLabel.TryGetValue(label, out camera!))
            return true;

        camera = null!;
        return false;
    }

    private void UpdatePresetLabels()
    {
        var slotIndex = GetSelectedCameraSlotIndex();
        for (var preset = 1; preset <= _presetLabels.Count; preset++)
        {
            var name = slotIndex >= 0 ? ReadPresetName(slotIndex, preset) : null;
            _presetLabels[preset - 1].Text = string.IsNullOrWhiteSpace(name)
                ? $"Preset {preset}"
                : name;
        }
    }

    private static string? ReadPresetName(int cameraSlotIndex, int preset)
    {
        if (OperatingSystem.IsWindows())
            return ReadPresetNameFromRegistry(cameraSlotIndex, preset);

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadPresetNameFromRegistry(int cameraSlotIndex, int preset)
    {
        var valueName = $"Tooltip{preset + cameraSlotIndex * 100}";
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
        return key?.GetValue(valueName) as string;
    }

    private static int? TryReadNullableInt(object? value)
    {
        if (value is null)
            return null;

        return int.TryParse(Convert.ToString(value), out var result) ? result : null;
    }

    private static string GetCameraKey(CameraInfo camera) =>
        string.IsNullOrWhiteSpace(camera.MonikerString) ? camera.Name : camera.MonikerString;

    private int StepPercent => Math.Max(1, (int)Math.Round(_stepSlider.Value));

    private int PanDelta(int delta) => _invertPan ? -delta : delta;

    private int TiltDelta(int delta) => _invertTilt ? -delta : delta;

    private void SetMemoryMode(bool enabled)
    {
        _successStatusClearTimer.Stop();
        _memoryMode = enabled;
        _memoryButton.Background = enabled
            ? Brush.Parse("#B9782E")
            : Brush.Parse("#735C2C");
        _statusBorder.Background = enabled
            ? GetBrushResource("AppMemoryStatusBrush")
            : GetBrushResource("AppStatusBrush");
        _statusBorder.BorderBrush = enabled
            ? GetBrushResource("AppMemoryStatusBorderBrush")
            : GetBrushResource("AppStatusBorderBrush");
        _statusText.Text = enabled
            ? "Memory mode: the next preset click saves the current position. Click Memory again or press Esc to cancel."
            : "Memory mode off.";
    }

    private void LoadSettings()
    {
        if (!OperatingSystem.IsWindows())
        {
            _noReset = _startupOptions.NoReset ?? false;
            return;
        }

        LoadSettingsFromRegistry();
    }

    [SupportedOSPlatform("windows")]
    private void LoadSettingsFromRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
        _themeMode = Convert.ToString(key?.GetValue(ThemeModeValueName, "System")) ?? "System";

        using var deviceKey = Registry.CurrentUser.OpenSubKey(DeviceRegistryPath);
        _logitechControl = Convert.ToInt32(deviceKey?.GetValue(LogitechControlValueName, 0)) != 0;
        _motorTime = Convert.ToInt32(deviceKey?.GetValue(MotorIntervalTimerValueName, 70));

        using var optionsKey = Registry.CurrentUser.OpenSubKey(OptionsRegistryPath);
        _noReset = _startupOptions.NoReset ?? Convert.ToInt32(optionsKey?.GetValue(NoResetValueName, 0)) != 0;
        _showOnlyLogitechCameras = Convert.ToInt32(optionsKey?.GetValue(ShowOnlyLogitechCamerasValueName, 0)) != 0;
        _previewTopMost = Convert.ToInt32(optionsKey?.GetValue(PreviewTopMostValueName, 0)) != 0;
        ApplyTheme();
    }

    private void LoadSelectedCameraSettings()
    {
        var slotIndex = GetSelectedCameraSlotIndex();
        if (!OperatingSystem.IsWindows() || slotIndex < 0)
            return;

        LoadSelectedCameraSettingsFromRegistry(slotIndex);
    }

    [SupportedOSPlatform("windows")]
    private void LoadSelectedCameraSettingsFromRegistry(int cameraSlotIndex)
    {
        var slot = cameraSlotIndex + 1;
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
        _invertPan = Convert.ToInt32(key?.GetValue(string.Format(InvertPanValueNameFormat, slot), 0)) != 0;
        _invertTilt = Convert.ToInt32(key?.GetValue(string.Format(InvertTiltValueNameFormat, slot), 0)) != 0;
        _effectiveZoomRawMax = TryReadNullableInt(key?.GetValue(string.Format(EffectiveZoomRawMaxValueNameFormat, slot)));
    }

    private void SaveSettings(int cameraSlotIndex, IReadOnlyList<string> presetNames)
    {
        if (!OperatingSystem.IsWindows())
            return;

        SaveSettingsToRegistry(cameraSlotIndex, presetNames);
    }

    [SupportedOSPlatform("windows")]
    private void SaveSettingsToRegistry(int cameraSlotIndex, IReadOnlyList<string> presetNames)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath);
        key?.SetValue(ThemeModeValueName, _themeMode, RegistryValueKind.String);

        if (cameraSlotIndex >= 0)
        {
            var slot = cameraSlotIndex + 1;
            key?.SetValue(string.Format(InvertPanValueNameFormat, slot), _invertPan ? 1 : 0, RegistryValueKind.DWord);
            key?.SetValue(string.Format(InvertTiltValueNameFormat, slot), _invertTilt ? 1 : 0, RegistryValueKind.DWord);
            var effectiveZoomRawMaxValueName = string.Format(EffectiveZoomRawMaxValueNameFormat, slot);
            if (_effectiveZoomRawMax is { } effectiveZoomRawMax)
                key?.SetValue(effectiveZoomRawMaxValueName, effectiveZoomRawMax, RegistryValueKind.DWord);
            else
                key?.DeleteValue(effectiveZoomRawMaxValueName, throwOnMissingValue: false);
            for (var preset = 1; preset <= 8; preset++)
            {
                var valueName = $"Tooltip{preset + cameraSlotIndex * 100}";
                var value = preset <= presetNames.Count ? presetNames[preset - 1] : string.Empty;
                key?.SetValue(valueName, value, RegistryValueKind.String);
            }
        }

        using var deviceKey = Registry.CurrentUser.CreateSubKey(DeviceRegistryPath);
        deviceKey?.SetValue(LogitechControlValueName, _logitechControl ? 1 : 0, RegistryValueKind.DWord);
        deviceKey?.SetValue(MotorIntervalTimerValueName, _motorTime, RegistryValueKind.DWord);

        using var optionsKey = Registry.CurrentUser.CreateSubKey(OptionsRegistryPath);
        optionsKey?.SetValue(ShowOnlyLogitechCamerasValueName, _showOnlyLogitechCameras ? 1 : 0, RegistryValueKind.DWord);
        optionsKey?.SetValue(PreviewTopMostValueName, _previewTopMost ? 1 : 0, RegistryValueKind.DWord);
    }

    private void ApplyTheme()
    {
        if (Application.Current is null)
            return;

        Application.Current.RequestedThemeVariant = _themeMode switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _memoryMode)
        {
            e.Handled = true;
            SetMemoryMode(false);
            return;
        }

        if (e.Source is TextBox)
            return;

        if (!TryBeginHotkey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleCameraHotkey(e) || TryHandleActionHotkey(e))
        {
            e.Handled = true;
            return;
        }

        EndHotkey(e.Key);
    }

    private void Window_KeyUp(object? sender, KeyEventArgs e)
    {
        EndHotkey(e.Key);
    }

    private void CancelMemoryModeForOtherAction()
    {
        if (_memoryMode)
            SetMemoryMode(false);
    }

    private void SetError(string context, Exception ex)
    {
        SetWarning($"{context}: {ex.Message}");
    }

    private void SetStatus(string message)
    {
        _statusBorder.Background = GetBrushResource("AppStatusBrush");
        _statusBorder.BorderBrush = GetBrushResource("AppStatusBorderBrush");
        _statusText.Text = message;
        _successStatusClearTimer.Stop();
        if (!string.IsNullOrWhiteSpace(message))
            _successStatusClearTimer.Start();
    }

    private void SetWarning(string message)
    {
        _successStatusClearTimer.Stop();
        _statusBorder.Background = GetBrushResource("AppWarningStatusBrush");
        _statusBorder.BorderBrush = GetBrushResource("AppWarningStatusBorderBrush");
        _statusText.Text = message;
    }

    private void SuccessStatusClearTimer_Tick(object? sender, EventArgs e)
    {
        _successStatusClearTimer.Stop();
        _statusText.Text = string.Empty;
    }

    private void ClosePreviewSession()
    {
        _previewSession?.Dispose();
        _previewSession = null;
        if (_previewButton is not null)
            _previewButton.Content = "Preview";
    }

    private void PositionOwnedWindow(Window window)
    {
        const int baseOffset = 36;
        const int cascadeStep = 26;
        var cascadeOffset = baseOffset + (_ownedWindowOffsetIndex++ % 5) * cascadeStep;
        window.Position = new PixelPoint(Position.X + cascadeOffset, Position.Y + cascadeOffset);
    }

    private IBrush GetBrushResource(string key)
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var resource) == true &&
            resource is IBrush brush)
        {
            return brush;
        }

        return Brush.Parse("#E8EDF2");
    }

    private int SelectStartupCamera(IReadOnlyList<CameraListItem> cameras)
    {
        if (cameras.Count == 0)
        {
            _startupSelectionWarning = null;
            return -1;
        }

        if (_startupOptions.Slot is not null)
        {
            var index = Enumerable.Range(0, cameras.Count)
                .FirstOrDefault(index => cameras[index].Slot == _startupOptions.Slot.Value, -1);
            if (index >= 0)
            {
                _startupSelectionWarning = null;
                return index;
            }

            _startupSelectionWarning = $"Startup slot {_startupOptions.Slot.Value} is not available. Using first camera.";
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(_startupOptions.DeviceNamePart))
        {
            for (var index = 0; index < cameras.Count; index++)
            {
                if (cameras[index].Camera.Name.Contains(_startupOptions.DeviceNamePart, StringComparison.OrdinalIgnoreCase))
                {
                    _startupSelectionWarning = null;
                    return index;
                }
            }

            _startupSelectionWarning = $"Startup device filter '{_startupOptions.DeviceNamePart}' did not match a camera. Using first camera.";
            return 0;
        }

        _startupSelectionWarning = null;
        return 0;
    }

    private string BuildStartupStatus(int detectedCameraCount, int displayedCameraCount)
    {
        if (detectedCameraCount == 0)
            return "No cameras found. Connect a camera and click Refresh.";

        if (displayedCameraCount == 0)
            return "No cameras match the current camera filter. Change Settings or click Refresh.";

        var status = _showOnlyLogitechCameras
            ? $"Found {displayedCameraCount} Logitech camera(s) out of {detectedCameraCount} detected camera(s)."
            : $"Found {displayedCameraCount} camera(s).";
        if (!string.IsNullOrWhiteSpace(_startupSelectionWarning))
            status += $" {_startupSelectionWarning}";
        if (_noReset)
            status += " NoReset is active.";
        return status;
    }

    private string ApplyStartupCameraReset(IReadOnlyList<CameraInfo> cameras, CameraInfo? selectedCamera)
    {
        if (_startupActionsApplied || cameras.Count == 0)
            return string.Empty;

        _startupActionsApplied = true;
        if (_noReset)
            return "Startup home reset skipped.";

        var selectedCameraKey = selectedCamera is null ? null : GetCameraKey(selectedCamera);
        string? selectedCameraError = null;
        for (var index = cameras.Count - 1; index >= 0; index--)
        {
            try
            {
                _backend.RestoreHome(GetCameraKey(cameras[index]), zoom: false, move: true);
                _startupHomeResults[GetCameraKey(cameras[index])] = true;
            }
            catch (Exception ex) when (selectedCameraKey == GetCameraKey(cameras[index]))
            {
                _startupHomeResults[GetCameraKey(cameras[index])] = false;
                selectedCameraError = ex.Message;
            }
            catch
            {
                _startupHomeResults[GetCameraKey(cameras[index])] = false;
                // Ignore non-selected camera startup reset failures to avoid noisy warnings.
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedCameraError))
        {
            return $"Restore home position of selected camera failed ({selectedCameraError}).";
        }

        return string.Empty;
    }

    private bool TryHandleCameraHotkey(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return false;

        var slot = e.Key switch
        {
            Key.D1 or Key.NumPad1 or Key.PageUp => 1,
            Key.D2 or Key.NumPad2 or Key.PageDown => 2,
            Key.D3 or Key.NumPad3 => 3,
            _ => 0
        };

        if (slot == 0)
            return false;

        SelectCameraSlot(slot);
        return true;
    }

    private bool TryHandleActionHotkey(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
            return false;

        switch (e.Key)
        {
            case Key.Home:
            case Key.NumPad0:
                HomeButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.D:
                DefaultAllButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.Left:
                PanLeftButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.Right:
                PanRightButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.Up:
                TiltUpButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.Down:
                TiltDownButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.M:
                MemoryButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.V:
                SelectLiveControlTab(picture: false);
                return true;
            case Key.I:
                SelectLiveControlTab(picture: true);
                RefreshPictureControls();
                return true;
            case Key.P:
                PreviewButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.Add:
            case Key.OemPlus:
            case Key.PageUp:
                ZoomInButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.Subtract:
            case Key.OemMinus:
            case Key.PageDown:
                ZoomOutButton_Click(this, new RoutedEventArgs());
                return true;
            case Key.Divide:
            case Key.Multiply:
                SettingsButton_Click(this, new RoutedEventArgs());
                return true;
        }

        var preset = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            _ => 0
        };

        if (preset == 0)
            return false;

        RunPresetAction(preset);
        return true;
    }

    private void SelectCameraSlot(int slot)
    {
        for (var index = 0; index < _displayedCameras.Count; index++)
        {
            if (_displayedCameras[index].Slot == slot)
            {
                _cameraSelector.SelectedIndex = index;
                SetStatus($"Selected camera slot {slot}.");
                return;
            }
        }

        SetWarning($"Camera slot {slot} is not available.");
    }

    private int GetSelectedCameraSlotIndex()
    {
        var selectedIndex = _cameraSelector.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _displayedCameras.Count)
            return -1;

        return _displayedCameras[selectedIndex].Slot - 1;
    }

    private bool ShouldShowCamera(CameraInfo camera) =>
        !_showOnlyLogitechCameras || IsLogitechCamera(camera);

    private static bool IsLogitechCamera(CameraInfo camera) =>
        OperatingSystem.IsWindows() &&
        camera.MonikerString?.Contains("vid_046d", StringComparison.OrdinalIgnoreCase) == true;

    private bool TryBeginHotkey(Key key)
    {
        if (!IsHandledHotkey(key))
            return true;

        return _pressedActionKeys.Add(key);
    }

    private void EndHotkey(Key key)
    {
        _pressedActionKeys.Remove(key);
    }

    private static bool IsHandledHotkey(Key key) => key is
        Key.Home or Key.NumPad0 or
        Key.Left or Key.Right or Key.Up or Key.Down or
        Key.D or Key.M or Key.V or Key.I or Key.P or
        Key.Add or Key.OemPlus or Key.PageUp or
        Key.Subtract or Key.OemMinus or Key.PageDown or
        Key.Divide or Key.Multiply or
        Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6 or Key.D7 or Key.D8 or
        Key.NumPad1 or Key.NumPad2 or Key.NumPad3 or Key.NumPad4 or Key.NumPad5 or Key.NumPad6 or Key.NumPad7 or Key.NumPad8;
}
