using System;
using System.Windows.Forms;
using RemoteCore;
using Velopack;

namespace HostApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            var startupLogger = new ConnectionLogger("host-startup.log");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try { startupLogger.Log("UnhandledException: " + e.ExceptionObject); } catch { }
            };
            Application.ThreadException += (_, e) =>
            {
                try { startupLogger.Log("ThreadException: " + e.Exception); } catch { }
            };

            VelopackApp.Build().Run();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Simple service registration / factory
            var capturer = new RemoteCore.Implementations.ScreenCapturerDesktopDuplication();
            var encoder = new RemoteCore.Implementations.JpegFrameEncoder(50, 1024);
            var logger = new ConnectionLogger("host.log");

            Application.Run(new HostForm(capturer, encoder, logger));
        }
    }
}
