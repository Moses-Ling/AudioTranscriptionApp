using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AudioTranscriptionApp.Services
{
    public class OpenAiChatService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private string _apiKey; // Store the key locally

        // Simple internal classes for request/response structure
        private class ChatRequest
        {
            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("messages")]
            public List<ChatMessage> Messages { get; set; }

            // Add other parameters like temperature if needed later
            // [JsonProperty("temperature")]
            // public float Temperature { get; set; } = 0.7f;
        }

        private class ChatMessage
        {
            [JsonProperty("role")]
            public string Role { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }
        }

        private class ChatResponse
        {
            [JsonProperty("choices")]
            public List<ChatChoice> Choices { get; set; }
            // Add other fields like 'usage' if needed
        }

        private class ChatChoice
        {
            [JsonProperty("message")]
            public ChatMessage Message { get; set; }
            // Add other fields like 'finish_reason' if needed
        }


        public OpenAiChatService(string apiKey)
        {
            Logger.Info("OpenAiChatService initializing.");
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://api.openai.com/");
            UpdateApiKey(apiKey); // Use the method to set header and local field
        }

        public void UpdateApiKey(string apiKey)
        {
            _apiKey = apiKey; // Store locally
            // Avoid logging the key itself
            Logger.Info("Updating API key for OpenAiChatService.");
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _apiKey);
                Logger.Info("OpenAI Chat API key set.");
            }
            else
            {
                 _httpClient.DefaultRequestHeaders.Authorization = null;
                 Logger.Warning("OpenAI Chat API key cleared.");
            }
        }

        // Configuration for retry logic
        private const int MaxRetries = 3;
        private const int InitialDelayMs = 1000; // 1 second

        public async Task<string> GetResponseAsync(string systemPrompt, string userPrompt, string model)
        {
            Logger.Info($"Attempting chat completion with model: {model}");
            if (string.IsNullOrEmpty(_apiKey))
            {
                Logger.Error("Chat completion failed: API key is not set.");
                throw new InvalidOperationException("OpenAI API key for cleanup is not set. Please configure it in Settings.");
            }

            var requestBody = new ChatRequest
            {
                Model = model,
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = systemPrompt },
                    new ChatMessage { Role = "user", Content = userPrompt }
                }
                // Temperature = 0.7f // Example if needed
            };

            int currentRetry = 0;
            int delayMs = InitialDelayMs;

            while (currentRetry <= MaxRetries)
            {
                try
                {
                    string jsonRequest = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                Logger.Info("Sending chat completion request to OpenAI API...");
                var response = await _httpClient.PostAsync("/v1/chat/completions", content);
                Logger.Info($"Received API response status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ChatResponse>(jsonResponse);

                    if (result?.Choices != null && result.Choices.Count > 0 && result.Choices[0].Message != null)
                    {
                        string assistantResponse = result.Choices[0].Message.Content;
                        Logger.Info($"Successfully received chat completion. Response length: {assistantResponse?.Length ?? 0}");
                        return assistantResponse?.Trim();
                    }
                    else
                    {
                        Logger.Error($"API returned success status but response format was unexpected: {jsonResponse}");
                        throw new Exception("API returned an unexpected response format.");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Logger.Error($"API Error: {response.StatusCode} - {errorContent}");

                    // Check if retryable error (429 or 5xx)
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        currentRetry++;
                        if (currentRetry <= MaxRetries)
                        {
                            Logger.Warning($"Retryable API error encountered for chat completion. Retrying in {delayMs}ms... (Attempt {currentRetry}/{MaxRetries})");
                            await Task.Delay(delayMs);
                            delayMs *= 2; // Exponential backoff
                            continue; // Go to next iteration of the while loop
                        }
                        else
                        {
                            Logger.Error($"Max retries ({MaxRetries}) reached for chat completion. Giving up.");
                            throw new Exception($"API Error after {MaxRetries} retries: {response.StatusCode} - {errorContent}");
                        }
                    }
                    else
                    {
                        // Non-retryable API error
                        throw new Exception($"API Error: {response.StatusCode} - {errorContent}");
                    }
                }
            } // end try
            catch (HttpRequestException httpEx)
            {
                Logger.Error($"HTTP request error during chat completion (Attempt {currentRetry + 1}): {httpEx.Message}", httpEx);
                currentRetry++;
                if (currentRetry <= MaxRetries)
                {
                    Logger.Warning($"Retrying network error for chat completion in {delayMs}ms... (Attempt {currentRetry}/{MaxRetries})");
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                    continue; // Retry
                }
                else
                {
                    Logger.Error($"Max retries ({MaxRetries}) reached after network error for chat completion. Giving up.");
                    throw new Exception($"Network error after {MaxRetries} retries: {httpEx.Message}", httpEx);
                }
            }
            catch (JsonException jsonEx)
            {
                // Typically non-retryable
                Logger.Error($"JSON processing error during chat completion: {jsonEx.Message}", jsonEx);
                throw new Exception($"Error processing API response: {jsonEx.Message}", jsonEx);
            }
            catch (Exception ex)
            {
                 // Catch-all for other unexpected errors - non-retryable
                Logger.Error($"Generic chat completion error (Attempt {currentRetry + 1}): {ex.Message}", ex);
                throw new Exception($"Chat completion error: {ex.Message}", ex);
            }
          } // end while loop

          // Should not be reached if logic is correct
          Logger.Error("Exited chat completion retry loop unexpectedly.");
          throw new Exception("Chat completion failed after exhausting retries or due to an unexpected loop exit.");
        } // This closing brace belongs to GetResponseAsync

        // Removed extra closing brace here

        public void Dispose()
        {
            Logger.Info("Disposing OpenAiChatService resources.");
            _httpClient?.Dispose();
        }
    }
}
