using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System;

namespace game
{
    public class LLM
    {
        private readonly string endpoint;
        private readonly string apiKey;
        private readonly HttpClient httpClient;

        public LLM(string apiKey, string endpoint = "https://api.deepseek.com/v1/chat/completions")
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentNullException(nameof(endpoint));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey));

            this.endpoint = endpoint;
            this.apiKey = apiKey;

            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }




        public async Task<string> SendRequestAsync(string systemPrompt, string userPrompt, int maxTokens = 256)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userPrompt == null) throw new ArgumentNullException(nameof(userPrompt));
            if (maxTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxTokens), "maxTokens must be > 0");

            var payload = new
            {
                model = "deepseek-chat",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }

                },
                max_tokens = maxTokens
            };

            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.PostAsync(endpoint, content);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {

                throw new Exception($"DeepSeek API request failed: {response.StatusCode} – {responseJson}");
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;


                JsonElement choices = root.GetProperty("choices");
                if (choices.GetArrayLength() == 0)
                {
                    throw new Exception("DeepSeek API returned no choices.");
                }

                JsonElement first = choices[0];
                string? result = first.GetProperty("message").GetProperty("content").GetString();
                return result ?? string.Empty;
            }
            catch (Exception ex)
            {

                throw new Exception($"Failed to parse DeepSeek API response: {ex.Message}. Raw response: {responseJson}");
            }
        }




    }
}