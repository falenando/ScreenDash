using System;
using System.IO;

namespace RemoteCore
{
    public class ConnectionLogger
    {
        private readonly string _primaryLogPath;
        private readonly string _secondaryLogPath;

        public ConnectionLogger(string fileName)
        {
            // Prefer a shared location (ProgramData) so SYSTEM and user processes can both write.
            var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var primaryDir = Path.Combine(commonAppData, "ScreenDash", "logs");
            Directory.CreateDirectory(primaryDir);
            _primaryLogPath = Path.Combine(primaryDir, fileName);

            // Fallback to the app base directory (works when running as a regular user).
            var secondaryDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(secondaryDir);
            _secondaryLogPath = Path.Combine(secondaryDir, fileName);
        }

        public void Log(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllLines(_primaryLogPath, new[] { line });
            }
            catch
            {
                // fallback to secondary location (e.g., user AppData)
                try
                {
                    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                    File.AppendAllLines(_secondaryLogPath, new[] { line });
                }
                catch
                {
                    // swallow logging errors to avoid breaking network flow
                }
            }
        }
    }
}
