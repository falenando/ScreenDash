using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace RemoteCore
{
    public static class Localization
    {
        private static Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static Localization()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                // Determine language: check app config first, fallback to system UI culture
                var defaultLang = "en";
                var appLang = defaultLang;
                try
                {
                    // hostconfig.json or viewerconfig.json will be read by callers; try both and prefer a non-default
                    var hostLang = Config.GetLanguageFromFile("hostconfig.json", defaultLang);
                    var viewerLang = Config.GetLanguageFromFile("viewerconfig.json", defaultLang);
                    if (!string.IsNullOrEmpty(hostLang) && hostLang != defaultLang)
                        appLang = hostLang;
                    else if (!string.IsNullOrEmpty(viewerLang) && viewerLang != defaultLang)
                        appLang = viewerLang;
                }
                catch { appLang = defaultLang; }

                var lang = string.IsNullOrEmpty(appLang) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : appLang;
                string fileName = lang switch
                {
                    "pt" => "pt.json",
                    "es" => "es.json",
                    _ => "en.json",
                };

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path1 = Path.Combine(baseDir, "locales", fileName);
                var path2 = Path.Combine(baseDir, fileName);

                string path = null;
                if (File.Exists(path1)) path = path1;
                else if (File.Exists(path2)) path = path2;

                if (path == null)
                {
                    // fallback to embedded defaults (English)
                    LoadDefaults();
                    return;
                }

                var json = File.ReadAllText(path);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in root.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }

                _strings = dict;
            }
            catch
            {
                LoadDefaults();
            }
        }

        private static void LoadDefaults()
        {
            _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ErrorFormat", "Error: {0}" },
                { "InvalidInput", "Invalid input. Provide a 6-char access code or an IP address." },
                { "LocalIPv4Invalid", "Local IPv4 invalid; cannot resolve target IP." },
                { "DifferentNetworkCode", "Invalid code: host on a different network than yours (LAN /24 only)." },
                { "DifferentNetworkIP", "Invalid IP: host on a different network than yours (LAN /24 only)." },
                { "ConnectTimeout", "Connect timeout" },
                { "ConfirmTerminate", "An active connection will be terminated. Do you want to continue?" },
                { "Confirm", "Confirm" }
            };
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_strings.TryGetValue(key, out var v)) return v;
            return key;
        }

        public static string Format(string key, params object[] args)
        {
            var fmt = Get(key);
            try
            {
                return string.Format(fmt, args);
            }
            catch
            {
                return fmt;
            }
        }
    }
}
