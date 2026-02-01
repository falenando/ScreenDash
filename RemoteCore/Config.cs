using System;
using System.IO;
using System.Text.Json;

namespace RemoteCore
{
    public static class Config
    {
        /// <summary>
        /// Read a JSON config file located in the application base directory and return the Port value if present.
        /// Falls back to defaultPort on any error.
        /// </summary>
        public static int GetPortFromFile(string fileName, int defaultPort)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.Combine(baseDir, fileName);
                if (!File.Exists(path))
                    return defaultPort;

                using var fs = File.OpenRead(path);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("Port", out var el) && el.TryGetInt32(out var p))
                {
                    return p;
                }
            }
            catch
            {
                // ignore and return default
            }

            return defaultPort;
        }

        public static string GetLanguageFromFile(string fileName, string defaultLanguage)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.Combine(baseDir, fileName);
                if (!File.Exists(path))
                    return defaultLanguage;

                using var fs = File.OpenRead(path);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("Language", out var el))
                {
                    var lang = el.GetString();
                    if (!string.IsNullOrEmpty(lang))
                        return lang;
                }
            }
            catch
            {
                // ignore and use default
            }

            return defaultLanguage;
        }
    }
}
