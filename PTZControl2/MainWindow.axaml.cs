using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Win32;
using PTZControl.Core;
using PTZControl.Uvc;

namespace PTZControl2;

public sealed partial class MainWindow : Window
{
    private readonly ICameraBackend _backend = CameraBackendFactory.Create();
    private readonly Dictionary<string, CameraInfo> _cameraByLabel = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TextBlock> _presetLabels = new();
    private ComboBox _cameraSelector = null!;
    private TextBlock _statusText = null!;
    private Slider _zoomSlider = null!;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => RefreshCameras();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _cameraSelector = this.FindControl<ComboBox>("CameraSelector")
            ?? throw new InvalidOperationException("CameraSelector control not found.");
        _statusText = this.FindControl<TextBlock>("StatusText")
            ?? throw new InvalidOperationException("StatusText control not found.");
        _zoomSlider = this.FindControl<Slider>("ZoomSlider")
            ?? throw new InvalidOperationException("ZoomSlider control not found.");
        for (var preset = 1; preset <= 8; preset++)
        {
            _presetLabels.Add(this.FindControl<TextBlock>($"Preset{preset}Text")
                ?? throw new InvalidOperationException($"Preset{preset}Text control not found."));
        }

        _zoomSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                _statusText.Text = $"Zoom target: {(int)_zoomSlider.Value}%";
        };
    }

    private void RefreshButton_Click(object? sender, RoutedEventArgs e) => RefreshCameras();

    private void CameraSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdatePresetLabels();
        _statusText.Text = TryGetSelectedCamera(out var camera)
            ? $"Selected camera: {camera.Name}"
            : "Select a camera.";
    }

    private async void ShowCameraInfoButton_Click(object? sender, RoutedEventArgs e)
    {
        var info = BuildCameraInfoText();
        var dialog = new CameraInfoDialog { Info = info };
        await dialog.ShowDialog(this);
    }

    private void PresetButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: not null } button || !int.TryParse(button.Tag.ToString(), out var preset))
            return;

        RunCameraAction($"Restore preset {preset}", camera => _backend.RestorePreset(camera, preset));
    }

    private void TiltUpButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Tilt up", camera => _backend.MoveRelativePanTilt(camera, y: 10));

    private void TiltDownButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Tilt down", camera => _backend.MoveRelativePanTilt(camera, y: -10));

    private void PanLeftButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Pan left", camera => _backend.MoveRelativePanTilt(camera, x: -10));

    private void PanRightButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Pan right", camera => _backend.MoveRelativePanTilt(camera, x: 10));

    private void HomeButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Home", camera => _backend.RestoreHome(camera, zoom: false, move: true));

    private void ZoomOutButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Zoom out", camera => ChangeZoom(camera, -10));

    private void ZoomInButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Zoom in", camera => ChangeZoom(camera, 10));

    private void SetZoomButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction($"Set zoom {(int)_zoomSlider.Value}%", camera =>
        {
            var raw = ScalePercentToValue(camera, CameraProperty.Zoom, (int)_zoomSlider.Value);
            _backend.SetPanTiltZoom(camera, zoom: raw);
        });

    private void DefaultMoveButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Default move", camera => _backend.RestoreDefault(camera, zoom: false, pan: true, tilt: true));

    private void DefaultAllButton_Click(object? sender, RoutedEventArgs e) =>
        RunCameraAction("Default all", camera => _backend.RestoreDefault(camera, zoom: true, pan: true, tilt: true));

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
            _cameraSelector.SelectedIndex = labels.Count > 0 ? 0 : -1;
            _statusText.Text = labels.Count == 0 ? "No cameras found." : $"Found {labels.Count} camera(s).";
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

    private int ScalePercentToValue(string camera, CameraProperty property, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var range = _backend.GetRange(camera, property);
        return range.min + (int)Math.Round((range.max - range.min) * (percent / 100.0));
    }

    private void RunCameraAction(string actionName, Action<string> action)
    {
        if (!TryGetSelectedCamera(out var camera))
            return;

        try
        {
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
        _statusText.Text = "Select a camera first.";
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
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\MRi-Software\PTZControl\Window");
        return key?.GetValue(valueName) as string;
    }

    private static string GetCameraKey(CameraInfo camera) =>
        string.IsNullOrWhiteSpace(camera.MonikerString) ? camera.Name : camera.MonikerString;

    private void SetError(string context, Exception ex)
    {
        _statusText.Text = $"{context}: {ex.Message}";
    }
}
