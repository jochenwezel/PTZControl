using System;

namespace PTZControl2;

public sealed class StartupOptions
{
    public string? DeviceNamePart { get; init; }
    public int? Slot { get; init; }
    public bool NoReset { get; init; }
    public bool NoGuard { get; init; }

    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptionsBuilder();
        foreach (var arg in args)
        {
            if (TryReadValue(arg, "-device:", out var deviceNamePart))
            {
                options.DeviceNamePart = deviceNamePart;
            }
            else if (TryReadValue(arg, "-slot:", out var slotText) && int.TryParse(slotText, out var slot))
            {
                options.Slot = slot;
            }
            else if (arg.Equals("-noreset", StringComparison.OrdinalIgnoreCase))
            {
                options.NoReset = true;
            }
            else if (arg.Equals("-noguard", StringComparison.OrdinalIgnoreCase))
            {
                options.NoGuard = true;
            }
        }

        return options.Build();
    }

    private static bool TryReadValue(string arg, string prefix, out string value)
    {
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..].Trim().Trim('"');
            return true;
        }

        value = string.Empty;
        return false;
    }

    private sealed class StartupOptionsBuilder
    {
        public string? DeviceNamePart { get; set; }
        public int? Slot { get; set; }
        public bool NoReset { get; set; }
        public bool NoGuard { get; set; }

        public StartupOptions Build() => new()
        {
            DeviceNamePart = string.IsNullOrWhiteSpace(DeviceNamePart) ? null : DeviceNamePart,
            Slot = Slot,
            NoReset = NoReset,
            NoGuard = NoGuard
        };
    }
}
