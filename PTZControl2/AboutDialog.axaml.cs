using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PTZControl2;

public sealed partial class AboutDialog : Window
{
    private const string ProjectUrl = "https://github.com/jochenwezel/PTZControl";
    private const string ReleasesUrl = "https://github.com/jochenwezel/PTZControl/releases";

    public AboutDialog()
    {
        AvaloniaXamlLoader.Load(this);
        SetRuntimeInformation();
    }

    private void SetRuntimeInformation()
    {
        var assembly = Assembly.GetExecutingAssembly();
        SetText("VersionTextBlock", assembly.GetName().Version?.ToString() ?? "unknown");
        SetText("OsTextBlock", RuntimeInformation.OSDescription);
        SetText("ProcessTextBlock", $"{RuntimeInformation.ProcessArchitecture} process on {RuntimeInformation.OSArchitecture} OS");
        SetText("DotNetTextBlock", Environment.Version.ToString());
        SetText("RuntimeTextBlock", RuntimeInformation.FrameworkDescription);
    }

    private void ProjectButton_Click(object? sender, RoutedEventArgs e) => OpenUrl(ProjectUrl);

    private void ReleasesButton_Click(object? sender, RoutedEventArgs e) => OpenUrl(ReleasesUrl);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void SetText(string controlName, string value)
    {
        var textBlock = this.FindControl<TextBlock>(controlName)
            ?? throw new InvalidOperationException($"{controlName} control not found.");
        textBlock.Text = value;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
