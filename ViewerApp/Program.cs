using System;
using System.Windows.Forms;
using RemoteCore;

namespace ViewerApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Simple service registration for viewer (no DI container used)
            var logger = new ConnectionLogger("viewer.log");
            Application.Run(new ViewerForm());
        }
    }
}
