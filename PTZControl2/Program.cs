using Avalonia;

namespace PTZControl2;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.StartupOptions = StartupOptions.Parse(args);
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
