using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PTZControl2;

internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = "PTZControl2.SingleInstance";
    private const int SwRestore = 9;

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static bool TryAcquire(out SingleInstance? instance)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            instance = new SingleInstance(mutex);
            return true;
        }

        mutex.Dispose();
        instance = null;
        return false;
    }

    public static void ActivateExistingWindow()
    {
        if (!OperatingSystem.IsWindows())
            return;

        ActivateExistingWindowWindows();
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static void ActivateExistingWindowWindows()
    {
        var window = FindWindow(null, "PTZControl2");
        if (window == IntPtr.Zero)
            return;

        ShowWindow(window, SwRestore);
        SetForegroundWindow(window);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
