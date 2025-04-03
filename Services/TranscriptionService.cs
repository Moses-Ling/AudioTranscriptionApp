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
            _httpClient = new HttpClient();
            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        public void UpdateApiKey(string apiKey)
        {
            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }
        
        public async Task<string> TranscribeAudioFileAsync(string audioFilePath)
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
                    var response = await _httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", formContent);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<WhisperResponse>(jsonResponse);
                        return result.Text;
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"API Error: {response.StatusCode} - {errorContent}");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Transcription error: {ex.Message}", ex);
            }
            finally
            {
                // Clean up the temporary file
                if (File.Exists(audioFilePath))
                {
                    File.Delete(audioFilePath);
                }
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
