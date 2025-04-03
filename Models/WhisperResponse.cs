using Newtonsoft.Json;

namespace AudioTranscriptionApp.Models
{
    public class WhisperResponse
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
