using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PTZControl2;

public sealed partial class CameraInfoDialog : Window
{
    private TextBox _infoText = null!;

    public CameraInfoDialog()
    {
        InitializeComponent();
    }

    public string Info
    {
        get => _infoText.Text ?? "";
        set => _infoText.Text = value;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _infoText = this.FindControl<TextBox>("InfoText")
            ?? throw new System.InvalidOperationException("InfoText control not found.");
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
