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
    private const string ThemeModeValueName = "PTZControl2ThemeMode";
    private const string LogitechControlValueName = "LogitechMotionControl";
    private const string MotorIntervalTimerValueName = "MotorIntervalTimer";
    private const string NoResetValueName = "NoReset";
    private const string NoGuardValueName = "NoGuard";

    private readonly ICameraBackend _backend = CameraBackendFactory.Create();
    private readonly Dictionary<string, CameraInfo> _cameraByLabel = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TextBlock> _presetLabels = new();
    private readonly StartupOptions _startupOptions;
    private Border _statusBorder = null!;
    private ComboBox _cameraSelector = null!;
    private TextBlock _statusText = null!;
    private Slider _stepSlider = null!;
    private TextBlock _stepValueText = null!;
    private Button _memoryButton = null!;
    private bool _invertPan;
    private bool _invertTilt;
    private bool _logitechControl;
    private bool _noReset;
    private bool _noGuard;
    private bool _startupActionsApplied;
    private int _motorTime = 70;
    private string _themeMode = "System";
    private bool _memoryMode;
    private string? _startupSelectionWarning;

    public MainWindow()
        : this(new StartupOptions())
    {
    }

    public MainWindow(StartupOptions startupOptions)
    {
        _startupOptions = startupOptions;
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
        _stepSlider = this.FindControl<Slider>("StepSlider")
            ?? throw new InvalidOperationException("StepSlider control not found.");
        _stepValueText = this.FindControl<TextBlock>("StepValueText")
            ?? throw new InvalidOperationException("StepValueText control not found.");
        _memoryButton = this.FindControl<Button>("MemoryButton")
            ?? throw new InvalidOperationException("MemoryButton control not found.");
        for (var preset = 1; preset <= 8; preset++)
        {
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
        _statusText.Text = TryGetSelectedCamera(out var camera)
            ? $"Selected camera: {camera.Name}"
            : "Select a camera.";
    }

    private async void ShowCameraInfoButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        var info = BuildCameraInfoText();
        var dialog = new CameraInfoDialog { Info = info };
        await dialog.ShowDialog(this);
    }

    private async void HelpButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        var dialog = new HelpDialog();
        await dialog.ShowDialog(this);
    }

    private void PresetButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: not null } button || !int.TryParse(button.Tag.ToString(), out var preset))
            return;

        RunPresetAction(preset);
    }

    private void RunPresetAction(int preset)
    {
        if (_memoryMode)
        {
            SetMemoryMode(false);
            RunCameraAction($"Save preset {preset}", camera => _backend.SavePreset(camera, preset));
            return;
        }

        RunCameraAction($"Restore preset {preset}", camera => _backend.RestorePreset(camera, preset));
    }

    private void TiltUpButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction($"Tilt up {StepPercent}", camera => _backend.MoveRelativePanTilt(camera, y: TiltDelta(StepPercent)));

    private void TiltDownButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction($"Tilt down {StepPercent}", camera => _backend.MoveRelativePanTilt(camera, y: TiltDelta(-StepPercent)));

    private void PanLeftButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction($"Pan left {StepPercent}", camera => _backend.MoveRelativePanTilt(camera, x: PanDelta(-StepPercent)));

    private void PanRightButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction($"Pan right {StepPercent}", camera => _backend.MoveRelativePanTilt(camera, x: PanDelta(StepPercent)));

    private void HomeButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Home", camera => _backend.RestoreHome(camera, zoom: false, move: true));

    private void ZoomOutButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction($"Zoom out {StepPercent}", camera => ChangeZoom(camera, -StepPercent));

    private void ZoomInButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction($"Zoom in {StepPercent}", camera => ChangeZoom(camera, StepPercent));

    private void DefaultAllButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Default all", camera => _backend.RestoreDefault(camera, zoom: true, pan: true, tilt: true));

    private void MemoryButton_Click(object? sender, RoutedEventArgs e) => SetMemoryMode(!_memoryMode);

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelMemoryModeForOtherAction();
        if (!TryGetSelectedCamera(out var camera))
            return;

        var slotIndex = _cameraSelector.SelectedIndex;
        var presetNames = Enumerable.Range(1, 8)
            .Select(preset => ReadPresetName(slotIndex, preset) ?? string.Empty)
            .ToList();
        var dialog = new SettingsDialog
        {
            CameraLabel = $"Camera slot {slotIndex + 1}: {camera.Name}",
            InvertPan = _invertPan,
            InvertTilt = _invertTilt,
            LogitechControl = _logitechControl,
            MotorTime = _motorTime,
            ThemeMode = _themeMode,
            PresetNames = presetNames
        };

        if (await dialog.ShowDialog<bool>(this))
        {
            _invertPan = dialog.InvertPan;
            _invertTilt = dialog.InvertTilt;
            _logitechControl = dialog.LogitechControl;
            _motorTime = dialog.MotorTime;
            _themeMode = dialog.ThemeMode;
            SaveSettings(slotIndex, dialog.PresetNames);
            ApplyTheme();
            UpdatePresetLabels();
            _statusText.Text = "Settings saved.";
        }
    }

    private void RefreshCameras()
    {
        try
        {
            _cameraByLabel.Clear();
            var cameras = _backend.Enumerate();
            var labels = cameras.Select((camera, index) =>
            {
                var label = $"{index + 1}: {camera.Name}";
                if (!string.IsNullOrWhiteSpace(camera.MonikerString))
                    label += $" ({camera.MonikerString})";
                _cameraByLabel[label] = camera;
                return label;
            }).ToList();

            _cameraSelector.ItemsSource = labels;
            _cameraSelector.SelectedIndex = SelectStartupCamera(cameras);
            var startupStatus = BuildStartupStatus(labels.Count);
            var resetStatus = ApplyStartupCameraReset(cameras);
            _statusText.Text = string.IsNullOrWhiteSpace(resetStatus)
                ? startupStatus
                : $"{startupStatus} {resetStatus}";
            UpdatePresetLabels();
        }
        catch (Exception ex)
        {
            SetError("Refresh cameras failed", ex);
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
            ""
        };

        AppendRange(lines, cameraKey, "Zoom", CameraProperty.Zoom);
        AppendRange(lines, cameraKey, "Pan", CameraProperty.Pan);
        AppendRange(lines, cameraKey, "Tilt", CameraProperty.Tilt);

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

    private void ChangeZoom(string camera, int deltaPercent)
    {
        var raw = AddPercentDelta(camera, CameraProperty.Zoom, deltaPercent);
        _backend.SetPanTiltZoom(camera, zoom: raw);
    }

    private int AddPercentDelta(string camera, CameraProperty property, int deltaPercent)
    {
        var range = _backend.GetRange(camera, property);
        var current = _backend.GetValue(camera, property);
        var delta = (int)Math.Round((range.max - range.min) * (deltaPercent / 100.0));
        return Math.Clamp(current + delta, range.min, range.max);
    }

    private void RunCameraAction(string actionName, Action<string> action)
    {
        if (!TryGetSelectedCamera(out var camera))
            return;

        try
        {
            CancelMemoryModeForOtherAction();
            action(GetCameraKey(camera));
            _statusText.Text = $"{actionName}: OK";
        }
        catch (Exception ex)
        {
            SetError(actionName, ex);
        }
    }

    private bool TryGetSelectedCamera(out CameraInfo camera)
    {
        if (_cameraSelector.SelectedItem is string label && _cameraByLabel.TryGetValue(label, out camera!))
            return true;

        camera = null!;
        _statusText.Text = _cameraByLabel.Count == 0
            ? "No camera available. Connect a camera and click Refresh."
            : "Select a camera first.";
        return false;
    }

    private void UpdatePresetLabels()
    {
        var slotIndex = _cameraSelector.SelectedIndex;
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

    private static string GetCameraKey(CameraInfo camera) =>
        string.IsNullOrWhiteSpace(camera.MonikerString) ? camera.Name : camera.MonikerString;

    private int StepPercent => Math.Max(1, (int)Math.Round(_stepSlider.Value));

    private int PanDelta(int delta) => _invertPan ? -delta : delta;

    private int TiltDelta(int delta) => _invertTilt ? -delta : delta;

    private void SetMemoryMode(bool enabled)
    {
        _memoryMode = enabled;
        _memoryButton.Background = enabled
            ? Avalonia.Media.Brush.Parse("#B9782E")
            : Avalonia.Media.Brush.Parse("#735C2C");
        _statusBorder.Background = enabled
            ? Brush.Parse("#FFF0D9")
            : Brush.Parse("#E8EDF2");
        _statusBorder.BorderBrush = enabled
            ? Brush.Parse("#E0A044")
            : Brush.Parse("#CED6DF");
        _statusText.Text = enabled
            ? "Memory mode: the next preset click saves the current position. Click Memory again or press Esc to cancel."
            : "Memory mode off.";
    }

    private void LoadSettings()
    {
        if (!OperatingSystem.IsWindows())
        {
            _noReset = _startupOptions.NoReset ?? false;
            _noGuard = _startupOptions.NoGuard ?? false;
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
        _noGuard = _startupOptions.NoGuard ?? Convert.ToInt32(optionsKey?.GetValue(NoGuardValueName, 0)) != 0;
        ApplyTheme();
    }

    private void LoadSelectedCameraSettings()
    {
        if (!OperatingSystem.IsWindows() || _cameraSelector.SelectedIndex < 0)
            return;

        LoadSelectedCameraSettingsFromRegistry(_cameraSelector.SelectedIndex);
    }

    [SupportedOSPlatform("windows")]
    private void LoadSelectedCameraSettingsFromRegistry(int cameraSlotIndex)
    {
        var slot = cameraSlotIndex + 1;
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
        _invertPan = Convert.ToInt32(key?.GetValue(string.Format(InvertPanValueNameFormat, slot), 0)) != 0;
        _invertTilt = Convert.ToInt32(key?.GetValue(string.Format(InvertTiltValueNameFormat, slot), 0)) != 0;
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
        var slot = cameraSlotIndex + 1;
        using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath);
        key?.SetValue(string.Format(InvertPanValueNameFormat, slot), _invertPan ? 1 : 0, RegistryValueKind.DWord);
        key?.SetValue(string.Format(InvertTiltValueNameFormat, slot), _invertTilt ? 1 : 0, RegistryValueKind.DWord);
        key?.SetValue(ThemeModeValueName, _themeMode, RegistryValueKind.String);
        for (var preset = 1; preset <= 8; preset++)
        {
            var valueName = $"Tooltip{preset + cameraSlotIndex * 100}";
            var value = preset <= presetNames.Count ? presetNames[preset - 1] : string.Empty;
            key?.SetValue(valueName, value, RegistryValueKind.String);
        }

        using var deviceKey = Registry.CurrentUser.CreateSubKey(DeviceRegistryPath);
        deviceKey?.SetValue(LogitechControlValueName, _logitechControl ? 1 : 0, RegistryValueKind.DWord);
        deviceKey?.SetValue(MotorIntervalTimerValueName, _motorTime, RegistryValueKind.DWord);
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

        if (TryHandleCameraHotkey(e) || TryHandleActionHotkey(e))
            e.Handled = true;
    }

    private void CancelMemoryModeForOtherAction()
    {
        if (_memoryMode)
            SetMemoryMode(false);
    }

    private void SetError(string context, Exception ex)
    {
        _statusText.Text = $"{context}: {ex.Message}";
    }

    private int SelectStartupCamera(IReadOnlyList<CameraInfo> cameras)
    {
        if (cameras.Count == 0)
        {
            _startupSelectionWarning = null;
            return -1;
        }

        if (_startupOptions.Slot is not null)
        {
            if (_startupOptions.Slot.Value >= 1 && _startupOptions.Slot.Value <= cameras.Count)
            {
                _startupSelectionWarning = null;
                return _startupOptions.Slot.Value - 1;
            }

            _startupSelectionWarning = $"Startup slot {_startupOptions.Slot.Value} is not available. Using first camera.";
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(_startupOptions.DeviceNamePart))
        {
            for (var index = 0; index < cameras.Count; index++)
            {
                if (cameras[index].Name.Contains(_startupOptions.DeviceNamePart, StringComparison.OrdinalIgnoreCase))
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

    private string BuildStartupStatus(int cameraCount)
    {
        if (cameraCount == 0)
            return "No cameras found. Connect a camera and click Refresh.";

        var status = $"Found {cameraCount} camera(s).";
        if (!string.IsNullOrWhiteSpace(_startupSelectionWarning))
            status += $" {_startupSelectionWarning}";
        if (_startupOptions.Slot is not null)
            status += $" Startup slot: {_startupOptions.Slot}.";
        if (!string.IsNullOrWhiteSpace(_startupOptions.DeviceNamePart))
            status += $" Startup device filter: {_startupOptions.DeviceNamePart}.";
        if (_noReset)
            status += " NoReset is active.";
        if (_noGuard)
            status += " NoGuard is active.";
        return status;
    }

    private string ApplyStartupCameraReset(IReadOnlyList<CameraInfo> cameras)
    {
        if (_startupActionsApplied || cameras.Count == 0)
            return string.Empty;

        _startupActionsApplied = true;
        if (_noReset)
            return "Startup home reset skipped.";

        var failures = new List<string>();
        for (var index = cameras.Count - 1; index >= 0; index--)
        {
            try
            {
                _backend.RestoreHome(GetCameraKey(cameras[index]), zoom: false, move: true);
            }
            catch (Exception ex)
            {
                failures.Add($"{cameras[index].Name}: {ex.Message}");
            }
        }

        return failures.Count == 0
            ? "Startup home reset completed."
            : $"Startup home reset partially failed: {string.Join("; ", failures)}";
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
            case Key.Enter:
            case Key.NumPad0:
                HomeButton_Click(this, new RoutedEventArgs());
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
        if (slot >= 1 && slot <= _cameraByLabel.Count)
        {
            _cameraSelector.SelectedIndex = slot - 1;
            _statusText.Text = $"Selected camera slot {slot}.";
            return;
        }

        _statusText.Text = $"Camera slot {slot} is not available.";
    }
}
