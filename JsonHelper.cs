using System;
using System.IO;
using System.Text.Json;

namespace ScreenDash
{
    public static class JsonHelper
    {
        /// <summary>
        /// Verifica se um arquivo contém JSON válido.
        /// </summary>
        /// <param name="filePath">Caminho completo para o arquivo JSON.</param>
        /// <param name="errorMessage">Retorna a mensagem de erro se for inválido.</param>
        /// <returns>True se for válido, False caso contrário.</returns>
        public static bool IsJsonFileValid(string filePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!File.Exists(filePath))
            {
                errorMessage = "O arquivo não foi encontrado.";
                return false;
            }

            try
            {
                // Ler o arquivo como UTF-8, que é o padrão para JSON.
                string content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                using (JsonDocument doc = JsonDocument.Parse(content))
                {
                    // Se chegou aqui, a sintaxe do JSON está correta.
                    return true;
                }
            }
            catch (JsonException ex)
            {
                // Captura erros de sintaxe JSON ou de encoding (se não for UTF-8 válido).
                errorMessage = $"Erro de sintaxe/encoding no JSON: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Erro ao ler o arquivo: {ex.Message}";
                return false;
            }
        }
    }
}