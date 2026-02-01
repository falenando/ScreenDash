using System;
using System.Windows.Forms;
using RemoteCore;

namespace HostApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
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
