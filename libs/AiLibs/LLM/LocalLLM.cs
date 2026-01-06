using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AI
{
    public class LocalLLM : ILLM
    {
        private readonly string _endpoint;
        private readonly string _model;
        private readonly HttpClient _httpClient;

        public LocalLLM(string endpoint = "http://localhost:1234/v1/chat/completions", string model = "gpt-oss-20b", string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentNullException(nameof(endpoint));
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            _endpoint = endpoint;
            _model = model;
            _httpClient = new HttpClient();

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }
        }

        public async Task<string> SendRequestAsync(string systemPrompt, string userPrompt, uint maxTokens = 256)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userPrompt == null) throw new ArgumentNullException(nameof(userPrompt));
            if (maxTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxTokens), "maxTokens must be > 0");

            var payload = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = maxTokens
            };

            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync(_endpoint, content);
                await HandleApiErrorAsync(response);

                string responseJson = await response.Content.ReadAsStringAsync();

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(responseJson);
                    var root = doc.RootElement;

                    JsonElement choices = root.GetProperty("choices");
                    if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    {
                        throw new Exception("Local LLM returned no choices.");
                    }

                    JsonElement first = choices[0];
                    // Try OpenAI-compatible schema first
                    if (first.TryGetProperty("message", out var message))
                    {
                        string? result = message.GetProperty("content").GetString();
                        return result ?? string.Empty;
                    }
                    // Fallback to simple text property some servers use
                    if (first.TryGetProperty("text", out var textEl))
                    {
                        string? result = textEl.GetString();
                        return result ?? string.Empty;
                    }

                    throw new Exception("Unrecognized local LLM response format.");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to parse Local LLM response: {ex.Message}. Raw response: {responseJson}");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new NoInternetException(
                    "Cannot reach local LLM server. Ensure it is running.",
                    ex
                );
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("Request to Local LLM timed out.", ex);
            }
        }

        private static async Task HandleApiErrorAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            string body = await response.Content.ReadAsStringAsync();

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new InvalidApiKeyException();

                case (HttpStatusCode)429:
                    throw new RateLimitException();

                case HttpStatusCode.BadRequest:
                    throw new ApiException("Bad request sent to local LLM.");

                default:
                    throw new ApiException(
                        $"Local LLM error ({(int)response.StatusCode}): {body}"
                    );
            }
        }
    }
}
