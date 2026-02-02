using System;
using System.Globalization;
using ScreenDash.Resources;

namespace RemoteCore
{
    public static class Localization
    {
        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            var value = Strings.ResourceManager.GetString(key, Strings.Culture);
            return value ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            var format = Get(key);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, args);
            }
            catch
            {
                return format;
            }
        }
    }
}