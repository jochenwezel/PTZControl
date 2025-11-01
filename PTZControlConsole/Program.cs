using System;
using PTZControlBridge;

class Program
{
    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  PTZControlConsole --list");
        Console.WriteLine("  PTZControlConsole --camera \"NamePart\" [--pan N] [--tilt N] [--zoom N]");
        Console.WriteLine("  PTZControlConsole --preset --camera \"NamePart\" --save N|--recall N");
    }

    static int Main(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 1; }

        if (Array.Exists(args, a => a.Equals("--list", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var name in LogitechPtz.EnumerateCameras())
                Console.WriteLine(name);
            return 0;
        }

        string? camera = null;
        int? pan = null; int? tilt = null; int? zoom = null;
        bool doPreset = false; int? save = null; int? recall = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--camera": camera = args[++i]; break;
                case "--pan": pan = int.Parse(args[++i]); break;
                case "--tilt": tilt = int.Parse(args[++i]); break;
                case "--zoom": zoom = int.Parse(args[++i]); break;
                case "--preset": doPreset = true; break;
                case "--save": save = int.Parse(args[++i]); break;
                case "--recall": recall = int.Parse(args[++i]); break;
            }
        }

        if (string.IsNullOrWhiteSpace(camera)) { PrintUsage(); return 1; }

        try
        {
            if (doPreset)
            {
                if (save.HasValue) LogitechPtz.SavePreset(camera, save.Value);
                else if (recall.HasValue) LogitechPtz.RecallPreset(camera, recall.Value);
                else { PrintUsage(); return 1; }
            }
            else
            {
                LogitechPtz.SetPanTiltZoom(camera, pan, tilt, zoom);
            }

            Console.WriteLine("OK");
            return 0;
        }
        catch (NotSupportedException nse)
        {
            Console.Error.WriteLine(nse.Message);
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }
    }
}