using AudioTranscriptionApp.Models;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;

namespace AudioTranscriptionApp.Services
{
    public class TranscriptionService
    {
        private readonly HttpClient _httpClient;

        public TranscriptionService(string apiKey)
        {
            Logger.Info("TranscriptionService initializing.");
            _httpClient = new HttpClient();
            if (!string.IsNullOrEmpty(apiKey))
            {
                Logger.Info("Setting initial API key.");
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        public void UpdateApiKey(string apiKey)
        {
            // Avoid logging the key itself
            Logger.Info("Updating API key for TranscriptionService.");
            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        // Configuration for retry logic
        private const int MaxRetries = 3;
        private const int InitialDelayMs = 1000; // 1 second

        public async Task<string> TranscribeAudioFileAsync(string audioFilePath)
        {
            Logger.Info($"Attempting to transcribe audio file: {audioFilePath}");
            if (string.IsNullOrEmpty(_httpClient.DefaultRequestHeaders.Authorization?.Parameter))
            {
                Logger.Error("Transcription failed: API key is not set.");
                throw new InvalidOperationException("OpenAI API key is not set. Please configure it in Settings.");
            }

            int currentRetry = 0;
            int delayMs = InitialDelayMs;

            while (currentRetry <= MaxRetries)
            {
                try
                {
                    using (var formContent = new MultipartFormDataContent())
                    {
                        // Add the audio file
                        var fileContent = new ByteArrayContent(File.ReadAllBytes(audioFilePath));
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                        formContent.Add(fileContent, "file", "audio.wav");

                        // Add the model parameter (using the smallest model to save costs)
                        formContent.Add(new StringContent("whisper-1"), "model");

                        // Send to OpenAI Whisper API
                        Logger.Info($"Sending transcription request (Attempt {currentRetry + 1}/{MaxRetries + 1})...");
                        var response = await _httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", formContent);
                        Logger.Info($"Received API response status: {response.StatusCode}");

                        if (response.IsSuccessStatusCode)
                        {
                            var jsonResponse = await response.Content.ReadAsStringAsync();
                            var result = JsonConvert.DeserializeObject<WhisperResponse>(jsonResponse);
                            Logger.Info($"Successfully transcribed chunk. Text length: {result?.Text?.Length ?? 0}");
                            return result.Text; // Success, exit retry loop
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
                                    Logger.Warning($"Retryable API error encountered. Retrying in {delayMs}ms... (Attempt {currentRetry}/{MaxRetries})");
                                    await Task.Delay(delayMs);
                                    delayMs *= 2; // Exponential backoff
                                    continue; // Go to next iteration of the while loop
                                }
                                else
                                {
                                    Logger.Error($"Max retries ({MaxRetries}) reached. Giving up.");
                                    throw new Exception($"API Error after {MaxRetries} retries: {response.StatusCode} - {errorContent}");
                                }
                            }
                            else
                            {
                                // Non-retryable API error (e.g., 400 Bad Request, 401 Unauthorized)
                                throw new Exception($"API Error: {response.StatusCode} - {errorContent}");
                            }
                        }
                    } // end using formContent
                } // end try
                catch (HttpRequestException httpEx)
                {
                    Logger.Error($"HTTP request error during transcription (Attempt {currentRetry + 1}): {httpEx.Message}", httpEx);
                    currentRetry++;
                    if (currentRetry <= MaxRetries)
                    {
                        Logger.Warning($"Retrying network error in {delayMs}ms... (Attempt {currentRetry}/{MaxRetries})");
                        await Task.Delay(delayMs);
                        delayMs *= 2;
                        continue; // Retry
                    }
                    else
                    {
                         Logger.Error($"Max retries ({MaxRetries}) reached after network error. Giving up.");
                         throw new Exception($"Network error after {MaxRetries} retries: {httpEx.Message}", httpEx);
                    }
                }
                catch (JsonException jsonEx)
                {
                    // Typically non-retryable
                    Logger.Error($"JSON deserialization error during transcription: {jsonEx.Message}", jsonEx);
                    throw new Exception($"Error processing API response: {jsonEx.Message}", jsonEx);
                }
                 catch (IOException ioEx)
                {
                    // Error reading the audio file - non-retryable in this context
                    Logger.Error($"IO error reading audio file {audioFilePath}: {ioEx.Message}", ioEx);
                    throw new Exception($"Error reading audio file: {ioEx.Message}", ioEx);
                }
                catch (Exception ex)
                {
                    // Catch-all for other unexpected errors - potentially non-retryable
                    Logger.Error($"Generic transcription error (Attempt {currentRetry + 1}): {ex.Message}", ex);
                    // Decide if generic errors should be retried or not. Let's not retry for now.
                    throw new Exception($"Transcription error: {ex.Message}", ex);
                }
                finally // Moved inside the while loop
                {
                    // Clean up the temporary file *after each attempt*
                    try
                    {
                        if (File.Exists(audioFilePath))
                        {
                            Logger.Info($"Deleting temporary audio file: {audioFilePath}");
                            File.Delete(audioFilePath);
                        }
                    }
                    catch (IOException ioEx)
                    {
                        // Log failure to delete but don't throw, as transcription might have succeeded
                        Logger.Warning($"Failed to delete temporary audio file {audioFilePath} in finally block: {ioEx.Message}");
                    }
                }
            } // end while loop

            // Should not be reached if logic is correct, but compiler needs a return path
            throw new Exception("Transcription failed after exhausting retries or due to an unexpected error.");
        }


        public void Dispose()
        {
            Logger.Info("Disposing TranscriptionService resources.");
            _httpClient?.Dispose();
        }
    }
}
