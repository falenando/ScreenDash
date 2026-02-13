using System;
using System.IO;

namespace RemoteCore
{
    public class ConnectionLogger
    {
        private readonly string? _primaryLogPath;
        private readonly string? _secondaryLogPath;
        private readonly string? _fallbackLogPath;

        public ConnectionLogger(string fileName)
        {
            // Prefer a shared location (ProgramData) so SYSTEM and user processes can both write.
            try
            {
                var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrWhiteSpace(commonAppData))
                {
                    var primaryDir = Path.Combine(commonAppData, "ScreenDash", "logs");
                    Directory.CreateDirectory(primaryDir);
                    _primaryLogPath = Path.Combine(primaryDir, fileName);
                }
            }
            catch
            {
                _primaryLogPath = null;
            }

            // Fallback to the app base directory (works when running as a regular user).
            try
            {
                var secondaryDir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(secondaryDir);
                _secondaryLogPath = Path.Combine(secondaryDir, fileName);
            }
            catch
            {
                _secondaryLogPath = null;
            }

            // Last resort: TEMP
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "ScreenDash", "logs");
                Directory.CreateDirectory(tempDir);
                _fallbackLogPath = Path.Combine(tempDir, fileName);
            }
            catch
            {
                _fallbackLogPath = null;
            }
        }

        public void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            if (TryAppend(_primaryLogPath, line))
                return;

            if (TryAppend(_secondaryLogPath, line))
                return;

            _ = TryAppend(_fallbackLogPath, line);
        }

        private static bool TryAppend(string? path, string line)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                File.AppendAllLines(path, new[] { line });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
