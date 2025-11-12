using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DropZoneApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using var mutex = new Mutex(true, "DropZoneApp_Mutex", out bool isNew);
            if (!isNew) return;

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var config = AppConfig.Load();
            try { AutostartService.Apply(config.AutoStart); } catch { }

            bool startMinimized = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            using var main = new MainForm(config);
            if (startMinimized)
            {
                main.Load += (_, __) => { main.WindowState = FormWindowState.Minimized; };
            }
            Application.Run(main);
        }
    }
}
