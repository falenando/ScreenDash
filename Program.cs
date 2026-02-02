using System.Net;
using Velopack;

namespace ScreenDash
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            VelopackApp.Build()
                .Run();

            // Carrega as configurações de idioma antes de iniciar a UI
            // Assumindo que este é o HostApp, usamos "hostconfig.json"
            LocalizationManager.LoadLanguage("hostconfig.json");

            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}