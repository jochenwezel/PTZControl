using Avalonia;
using PTZControl.Core;

namespace PTZControl2;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (TryRunRenameDeviceHelper(args, out var exitCode))
        {
            Environment.Exit(exitCode);
            return;
        }

        if (!SingleInstance.TryAcquire(out var singleInstance))
        {
            SingleInstance.ActivateExistingWindow();
            return;
        }

        using (singleInstance)
        {
            App.StartupOptions = StartupOptions.Parse(args);
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static bool TryRunRenameDeviceHelper(string[] args, out int exitCode)
    {
        const string command = "--set-directshow-camera-name";
        exitCode = 0;
        if (args.Length == 0 || !args[0].Equals(command, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var devicePath = ReadOptionValue(args, "--device-path");
            var friendlyName = ReadOptionValue(args, "--friendlyname");
            if (string.IsNullOrWhiteSpace(devicePath) || string.IsNullOrWhiteSpace(friendlyName))
            {
                exitCode = 2;
                return true;
            }

            CameraBackendFactory.Create().SetDirectShowCameraName(devicePath, friendlyName);
            exitCode = 0;
        }
        catch
        {
            exitCode = 1;
        }

        return true;
    }

    private static string? ReadOptionValue(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }
}
