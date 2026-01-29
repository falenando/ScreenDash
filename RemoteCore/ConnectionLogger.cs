using System;
using System.IO;

namespace RemoteCore
{
    public class ConnectionLogger
    {
        private readonly string _logPath;

        public ConnectionLogger(string fileName)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, fileName);
        }

        public void Log(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllLines(_logPath, new[] { line });
            }
            catch
            {
                // swallow logging errors to avoid breaking network flow
            }
        }
    }
}
