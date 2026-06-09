using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Collections.Generic;
using System.Linq;

namespace PTZControl2;

public sealed partial class SettingsDialog : Window
{
    private CheckBox _invertPanCheckBox = null!;
    private CheckBox _invertTiltCheckBox = null!;
    private TextBox _effectiveZoomRawMaxTextBox = null!;
    private CheckBox _logitechControlCheckBox = null!;
    private CheckBox _showOnlyLogitechCamerasCheckBox = null!;
    private CheckBox _previewTopMostCheckBox = null!;
    private TabItem _cameraFilterTab = null!;
    private TextBlock _cameraLabelTextBlock = null!;
    private TextBox _motorTimeTextBox = null!;
    private readonly List<TextBox> _presetNameTextBoxes = new();
    private ComboBox _themeModeComboBox = null!;

    public string CameraLabel
    {
        get => _cameraLabelTextBlock.Text ?? string.Empty;
        set => _cameraLabelTextBlock.Text = value;
    }

    public bool InvertPan
    {
        get => _invertPanCheckBox.IsChecked == true;
        set => _invertPanCheckBox.IsChecked = value;
    }

    public bool InvertTilt
    {
        get => _invertTiltCheckBox.IsChecked == true;
        set => _invertTiltCheckBox.IsChecked = value;
    }

    public int? EffectiveZoomRawMax
    {
        get => int.TryParse(_effectiveZoomRawMaxTextBox.Text, out var value) ? value : null;
        set => _effectiveZoomRawMaxTextBox.Text = value?.ToString() ?? string.Empty;
    }

    public bool LogitechControl
    {
        get => _logitechControlCheckBox.IsChecked == true;
        set => _logitechControlCheckBox.IsChecked = value;
    }

    public bool ShowCameraFilterOptions
    {
        get => _cameraFilterTab.IsVisible;
        set => _cameraFilterTab.IsVisible = value;
    }

    public bool ShowOnlyLogitechCameras
    {
        get => _showOnlyLogitechCamerasCheckBox.IsChecked == true;
        set => _showOnlyLogitechCamerasCheckBox.IsChecked = value;
    }

    public bool PreviewTopMost
    {
        get => _previewTopMostCheckBox.IsChecked == true;
        set => _previewTopMostCheckBox.IsChecked = value;
    }

    public int MotorTime
    {
        get => int.TryParse(_motorTimeTextBox.Text, out var value) ? value : 70;
        set => _motorTimeTextBox.Text = value.ToString();
    }

    public string ThemeMode
    {
        get => _themeModeComboBox.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System"
        };
        set => _themeModeComboBox.SelectedIndex = value switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
    }

    public IReadOnlyList<string> PresetNames
    {
        get => _presetNameTextBoxes.Select(textBox => textBox.Text ?? string.Empty).ToList();
        set
        {
            for (var index = 0; index < _presetNameTextBoxes.Count; index++)
                _presetNameTextBoxes[index].Text = index < value.Count ? value[index] : string.Empty;
        }
    }

    public SettingsDialog()
    {
        AvaloniaXamlLoader.Load(this);
        _cameraLabelTextBlock = this.FindControl<TextBlock>("CameraLabelTextBlock")
            ?? throw new InvalidOperationException("CameraLabelTextBlock control not found.");
        _invertPanCheckBox = this.FindControl<CheckBox>("InvertPanCheckBox")
            ?? throw new InvalidOperationException("InvertPanCheckBox control not found.");
        _invertTiltCheckBox = this.FindControl<CheckBox>("InvertTiltCheckBox")
            ?? throw new InvalidOperationException("InvertTiltCheckBox control not found.");
        _effectiveZoomRawMaxTextBox = this.FindControl<TextBox>("EffectiveZoomRawMaxTextBox")
            ?? throw new InvalidOperationException("EffectiveZoomRawMaxTextBox control not found.");
        _logitechControlCheckBox = this.FindControl<CheckBox>("LogitechControlCheckBox")
            ?? throw new InvalidOperationException("LogitechControlCheckBox control not found.");
        _showOnlyLogitechCamerasCheckBox = this.FindControl<CheckBox>("ShowOnlyLogitechCamerasCheckBox")
            ?? throw new InvalidOperationException("ShowOnlyLogitechCamerasCheckBox control not found.");
        _previewTopMostCheckBox = this.FindControl<CheckBox>("PreviewTopMostCheckBox")
            ?? throw new InvalidOperationException("PreviewTopMostCheckBox control not found.");
        _cameraFilterTab = this.FindControl<TabItem>("CameraFilterTab")
            ?? throw new InvalidOperationException("CameraFilterTab control not found.");
        _motorTimeTextBox = this.FindControl<TextBox>("MotorTimeTextBox")
            ?? throw new InvalidOperationException("MotorTimeTextBox control not found.");
        _themeModeComboBox = this.FindControl<ComboBox>("ThemeModeComboBox")
            ?? throw new InvalidOperationException("ThemeModeComboBox control not found.");
        for (var preset = 1; preset <= 8; preset++)
        {
            _presetNameTextBoxes.Add(this.FindControl<TextBox>($"PresetName{preset}TextBox")
                ?? throw new InvalidOperationException($"PresetName{preset}TextBox control not found."));
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void SaveButton_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
        }
    }
}
