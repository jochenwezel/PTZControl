using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PTZControl2;

public sealed partial class HelpDialog : Window
{
    private const string ProjectUrl = "https://github.com/jochenwezel/PTZControl";
    private const string ReleasesUrl = "https://github.com/jochenwezel/PTZControl/releases";

    public HelpDialog()
    {
        AvaloniaXamlLoader.Load(this);
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

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
