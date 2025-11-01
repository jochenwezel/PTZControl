using System;
using System.Windows.Forms;

namespace PTZControlGUI
{
    internal static class ApplicationConfiguration_OBSOLETE
    {
        public static void Initialize()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
    }
}