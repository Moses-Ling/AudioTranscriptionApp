using AudioTranscriptionApp.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AudioTranscriptionApp.Services
{
    public class AudioCaptureService : IDisposable
    {
        private WasapiLoopbackCapture _capture;
        private WaveFileWriter _writer;
        private string _tempFilePath;
        private bool _isRecording = false;
        private DateTime _recordingStartTime;
        private int _audioChunkSeconds; // Process in configurable chunks
        private readonly TranscriptionService _transcriptionService;

        // Audio level monitoring
        private float _currentAudioLevel = 0;
        private float _peakValue = 0;
        private readonly float _audioLevelSmoothingFactor = 0.2f; // For smoother level changes

        // Events
        public event EventHandler<float> AudioLevelChanged;
        public event EventHandler<string> TranscriptionReceived;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<Exception> ErrorOccurred;

        public bool IsRecording => _isRecording;

        public AudioCaptureService(TranscriptionService transcriptionService)
        {
            _transcriptionService = transcriptionService;
            // Load chunk duration from settings
            _audioChunkSeconds = Properties.Settings.Default.ChunkDurationSeconds;
            // Add validation if needed (e.g., ensure it's within 5-60 range)
            if (_audioChunkSeconds < 5) _audioChunkSeconds = 5;
            if (_audioChunkSeconds > 60) _audioChunkSeconds = 60;
        }

        public List<AudioDeviceModel> GetAudioDevices()
        {
            var audioDevices = new List<AudioDeviceModel>();
            var deviceEnumerator = new MMDeviceEnumerator();

            try
            {
                // Get all audio output devices
                var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in devices)
                {
                    audioDevices.Add(new AudioDeviceModel
                    {
                        Id = device.ID,
                        DisplayName = device.FriendlyName,
                        Device = device
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }

            return audioDevices;
        }

        public void InitializeAudioCapture(string deviceId)
        {
            try
            {
                // Clean up existing capture if any
                if (_capture != null)
                {
                    if (_isRecording)
                    {
                        _capture.StopRecording();
                    }

                    _capture.Dispose();
                    _capture = null;
                }

                // Get the device list
                var audioDevices = GetAudioDevices();

                // Initialize WASAPI loopback capture for the selected device
                if (string.IsNullOrEmpty(deviceId))
                {
                    // Use default device if none selected
                    _capture = new WasapiLoopbackCapture();
                }
                else
                {
                    // Use selected device
                    var selectedDevice = audioDevices.FirstOrDefault(d => d.Id == deviceId)?.Device;
                    if (selectedDevice != null)
                    {
                        _capture = new WasapiLoopbackCapture(selectedDevice);
                    }
                    else
                    {
                        _capture = new WasapiLoopbackCapture();
                    }
                }

                // Set up audio data handling
                _capture.DataAvailable += Capture_DataAvailable;
                _capture.RecordingStopped += Capture_RecordingStopped;

                var deviceInfo = audioDevices.FirstOrDefault(d => d.Id == deviceId);
                string deviceName = deviceInfo != null ? deviceInfo.DisplayName : "Default Device";

                StatusChanged?.Invoke(this, $"Audio capture initialized with {deviceName} - This will capture system audio");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public void StartRecording()
        {
            if (!_isRecording && _capture != null)
            {
                _isRecording = true;
                CreateNewAudioFile();

                try
                {
                    // Reset audio levels
                    _currentAudioLevel = 0;
                    _peakValue = 0;
                    AudioLevelChanged?.Invoke(this, 0);

                    // Start recording
                    _capture.StartRecording();

                    _recordingStartTime = DateTime.Now;

                    StatusChanged?.Invoke(this, "Recording and transcribing...");
                }
                catch (Exception ex)
                {
                    _isRecording = false;
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }

        public void StopRecording()
        {
            if (_isRecording && _capture != null)
            {
                _capture.StopRecording();
                // The RecordingStopped event will handle cleanup
            }
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (_isRecording && _writer != null)
            {
                // Write audio data to temporary file
                _writer.Write(e.Buffer, 0, e.BytesRecorded);

                // Calculate audio level for visualization
                CalculateAudioLevel(e.Buffer, e.BytesRecorded);

                // Check if we've reached the chunk duration
                TimeSpan elapsed = DateTime.Now - _recordingStartTime;
                if (elapsed.TotalSeconds >= _audioChunkSeconds)
                {
                    ProcessCurrentAudioChunk();
                }
            }
        }

        private void CalculateAudioLevel(byte[] buffer, int bytesRecorded)
        {
            // Simplified approach - find max amplitude in buffer
            float maxValue = 0;

            // Get format from current capture
            WaveFormat format = _capture.WaveFormat;

            if (format != null)
            {
                // Get bytes per sample based on bits per sample
                int bytesPerSample = format.BitsPerSample / 8;
                int channels = format.Channels;

                if (bytesPerSample == 2) // 16-bit audio
                {
                    for (int i = 0; i < bytesRecorded; i += (bytesPerSample * channels))
                    {
                        // Make sure we have enough bytes for a complete sample
                        if (i + 1 < bytesRecorded)
                        {
                            // Get absolute value of sample (little endian format)
                            short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                            float absoluteSample = Math.Abs(sample) / 32768f; // Normalize to 0-1

                            maxValue = Math.Max(maxValue, absoluteSample);
                        }
                    }
                }
                else if (bytesPerSample == 4) // 32-bit floating point audio
                {
                    for (int i = 0; i < bytesRecorded; i += (bytesPerSample * channels))
                    {
                        if (i + 3 < bytesRecorded)
                        {
                            // Convert bytes to float
                            byte[] sampleBytes = new byte[4];
                            Array.Copy(buffer, i, sampleBytes, 0, 4);
                            float sample = Math.Abs(BitConverter.ToSingle(sampleBytes, 0));

                            maxValue = Math.Max(maxValue, sample);
                        }
                    }
                }
            }

            // Apply some scaling to make the meter more responsive
            maxValue = Math.Min(1.0f, maxValue * 2.0f);

            // Update peak if this level is higher
            if (maxValue > _peakValue)
            {
                _peakValue = maxValue;
            }

            // Use exponential smoothing for a more natural-looking meter
            _currentAudioLevel = (_audioLevelSmoothingFactor * _peakValue) +
                               ((1 - _audioLevelSmoothingFactor) * _currentAudioLevel);

            // Notify listeners of the audio level change
            AudioLevelChanged?.Invoke(this, _currentAudioLevel);

            // Reset peak for next interval
            _peakValue = 0;
        }

        private void ProcessCurrentAudioChunk()
        {
            // Close the current writer
            var fileToProcess = _tempFilePath;
            _writer.Close();
            _writer = null;

            // Start a new recording segment
            CreateNewAudioFile();

            // Process the completed chunk in the background
            Task.Run(async () =>
            {
                try
                {
                    StatusChanged?.Invoke(this, "Transcribing audio...");
                    string transcriptionText = await _transcriptionService.TranscribeAudioFileAsync(fileToProcess);
                    TranscriptionReceived?.Invoke(this, transcriptionText);
                    StatusChanged?.Invoke(this, _isRecording ? "Recording and transcribing..." : "Transcription complete.");
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            });
        }

        private void CreateNewAudioFile()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"audio_chunk_{DateTime.Now.Ticks}.wav");
            _writer = new WaveFileWriter(_tempFilePath, _capture.WaveFormat);
            _recordingStartTime = DateTime.Now;
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (_writer != null)
            {
                // Process any remaining audio
                var fileToProcess = _tempFilePath;
                _writer.Close();
                _writer = null;

                Task.Run(async () =>
                {
                    try
                    {
                        StatusChanged?.Invoke(this, "Transcribing final audio chunk...");
                        string transcriptionText = await _transcriptionService.TranscribeAudioFileAsync(fileToProcess);
                        TranscriptionReceived?.Invoke(this, transcriptionText);
                        StatusChanged?.Invoke(this, "Transcription complete.");
                    }
                    catch (Exception ex)
                    {
                        ErrorOccurred?.Invoke(this, ex);
                    }
                });
            }

            _isRecording = false;
            StatusChanged?.Invoke(this, "Recording stopped.");
            AudioLevelChanged?.Invoke(this, 0);

            if (e.Exception != null)
            {
                ErrorOccurred?.Invoke(this, e.Exception);
            }
        }

        public void Dispose()
        {
            // Clean up resources
            if (_isRecording)
            {
                _capture?.StopRecording();
            }

            if (_writer != null)
            {
                _writer.Close();
                _writer = null;
            }

            _capture?.Dispose();

            // Clean up any temporary files
            try
            {
                if (!string.IsNullOrEmpty(_tempFilePath) && File.Exists(_tempFilePath))
                {
                    File.Delete(_tempFilePath);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }
}
