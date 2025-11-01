using System;
using PTZControl.Uvc;

class Program
{
    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  PTZControlConsole --list");
        Console.WriteLine("  PTZControlConsole --camera \"NamePart\" [--pan N] [--tilt N] [--zoom N]");
    }

    static int Main(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 1; }

        if (Array.Exists(args, a => a.Equals("--list", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var cam in UvcCamera.Enumerate())
                Console.WriteLine(cam.Name);
            return 0;
        }

        string? camera = null;
        int? pan = null; int? tilt = null; int? zoom = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--camera": camera = args[++i]; break;
                case "--pan": pan = int.Parse(args[++i]); break;
                case "--tilt": tilt = int.Parse(args[++i]); break;
                case "--zoom": zoom = int.Parse(args[++i]); break;
            }
        }

        if (string.IsNullOrWhiteSpace(camera)) { PrintUsage(); return 1; }

        try
        {
            UvcCamera.SetPanTiltZoom(camera, pan, tilt, zoom);
            Console.WriteLine("OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }
    }
}