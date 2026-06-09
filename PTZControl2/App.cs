using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PTZControl2;

public sealed class App : Application
{
    public static StartupOptions StartupOptions { get; set; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(StartupOptions);

        base.OnFrameworkInitializationCompleted();
    }
}
