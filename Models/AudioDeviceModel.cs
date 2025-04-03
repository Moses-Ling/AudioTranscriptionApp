using NAudio.CoreAudioApi;

namespace AudioTranscriptionApp.Models
{
    public class AudioDeviceModel
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public MMDevice Device { get; set; }
    }
}
