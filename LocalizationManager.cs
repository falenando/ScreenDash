using System;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Globalization;
using System.Threading;
using ScreenDash.Resources;

namespace ScreenDash
{
    public static class LocalizationManager
    {
        public static string CurrentLanguage { get; private set; } = "en"; // Idioma padrão

        /// <summary>
        /// Lê um arquivo de configuração para definir o idioma da aplicação.
        /// </summary>
        public static void LoadLanguage(string configFileName)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName);
            string lang = "en";

            // 1. Tentar ler a configuração de idioma
            if (File.Exists(configPath))
            {
                try
                {
                    string configContent = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
                    using (JsonDocument doc = JsonDocument.Parse(configContent))
                    {
                        if (doc.RootElement.TryGetProperty("Language", out JsonElement langElement))
                        {
                            lang = langElement.GetString() ?? "en";
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Se falhar ao ler config, mantém "en"
                    Debug.WriteLine($"[LocalizationManager] Falha ao ler ou analisar '{configPath}'. Erro: {ex.Message}. Usando 'en' como padrão.");
                }
            }
            else
            {
                Debug.WriteLine($"[LocalizationManager] Arquivo de configuração não encontrado em '{configPath}'. Usando 'en' como padrão.");
            }

            CurrentLanguage = lang;

            try
            {
                var culture = new CultureInfo(lang);
                Thread.CurrentThread.CurrentUICulture = culture;
                Strings.Culture = culture; // Define a cultura para a classe de recursos fortemente tipada
                Debug.WriteLine($"[LocalizationManager] Cultura da UI definida para '{lang}'.");
            }
            catch (CultureNotFoundException)
            {
                Debug.WriteLine($"[LocalizationManager] Cultura '{lang}' não encontrada. Usando o padrão do sistema.");
            }
        }

        public static string GetString(string key)
        {
            // Usa o ResourceManager para obter a string para a cultura da UI atual.
            // O fallback para inglês (ou o idioma neutro) é automático.
            string? value = Strings.ResourceManager.GetString(key, Strings.Culture);
            return value ?? key; // Retorna a própria chave se a tradução não for encontrada
        }
    }
}