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
        private DateTime _chunkStartTime; // Start time of the current chunk file
        private DateTime _sessionStartTime; // Start time of the entire recording session
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
        public event EventHandler<TimeSpan> RecordingTimeUpdate; // Added event for timer

        public bool IsRecording => _isRecording;
        public TimeSpan RecordedDuration { get; private set; } // To store final duration

        public AudioCaptureService(TranscriptionService transcriptionService)
        {
            Logger.Info("AudioCaptureService initializing.");
            _transcriptionService = transcriptionService;
            // Load chunk duration from settings
            _audioChunkSeconds = Properties.Settings.Default.ChunkDurationSeconds;
            // Add validation if needed (e.g., ensure it's within 5-60 range)
            if (_audioChunkSeconds < 5) _audioChunkSeconds = 5;
            if (_audioChunkSeconds > 60) _audioChunkSeconds = 60;
            Logger.Info($"Using audio chunk duration: {_audioChunkSeconds} seconds.");
        }

        public List<AudioDeviceModel> GetAudioDevices()
        {
            var audioDevices = new List<AudioDeviceModel>();
            Logger.Info("Getting audio devices...");
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
                Logger.Error("Failed to enumerate audio devices.", ex);
                ErrorOccurred?.Invoke(this, ex);
            }

            Logger.Info($"Found {audioDevices.Count} active audio devices.");
            return audioDevices;
        }

        public void InitializeAudioCapture(string deviceId)
        {
            Logger.Info($"Initializing audio capture for device ID: {deviceId ?? "Default"}");
            try
            {
                 // Clean up existing capture if any
                if (_capture != null)
                {
                    if (_isRecording)
                    {
                        Logger.Info("Stopping existing recording during re-initialization.");
                        _capture.StopRecording();
                    }

                    Logger.Info("Disposing previous capture instance.");
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
                Logger.Info($"Audio capture successfully initialized with {deviceName}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize audio capture for device ID: {deviceId ?? "Default"}", ex);
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public void StartRecording()
        {
            Logger.Info("Attempting to start recording.");
            if (!_isRecording && _capture != null)
            {
                 _isRecording = true;
                 CreateNewAudioFile(); // Creates the initial temp file

                 try
                {
                    // Reset audio levels
                    _currentAudioLevel = 0;
                    _peakValue = 0;
                    AudioLevelChanged?.Invoke(this, 0);

                    // Start recording
                    Logger.Info("Calling _capture.StartRecording().");
                    // Start recording
                    _sessionStartTime = DateTime.Now; // Set session start time
                    _chunkStartTime = _sessionStartTime; // First chunk starts now
                    RecordedDuration = TimeSpan.Zero; // Reset duration
                    Logger.Info("Calling _capture.StartRecording().");
                    _capture.StartRecording();


                    StatusChanged?.Invoke(this, "Recording and transcribing...");
                    Logger.Info("Recording started successfully.");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to start recording.", ex);
                    _isRecording = false;
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }

        public void StopRecording()
        {
            Logger.Info("Attempting to stop recording.");
            if (_isRecording && _capture != null)
            {
                 Logger.Info("Calling _capture.StopRecording().");
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

                // Update TOTAL elapsed time and raise event
                TimeSpan totalElapsed = DateTime.Now - _sessionStartTime;
                RecordedDuration = totalElapsed; // Keep track of total duration
                // Raise event periodically (e.g., every ~500ms) - simple check for now
                 // Basic throttle, only update twice a second approx (can be improved with a dedicated Timer if needed)
                if (totalElapsed.Milliseconds < 500 || totalElapsed.Milliseconds >= 500 && totalElapsed.Milliseconds < 550)
                {
                     RecordingTimeUpdate?.Invoke(this, totalElapsed);
                }


                // Check if the CURRENT CHUNK has reached the duration
                TimeSpan chunkElapsed = DateTime.Now - _chunkStartTime;
                if (chunkElapsed.TotalSeconds >= _audioChunkSeconds)
                {
                    ProcessCurrentAudioChunk(); // This resets _chunkStartTime via CreateNewAudioFile
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
            Logger.Info($"Closing current audio chunk file: {fileToProcess}");
            _writer.Close();
            _writer = null;

            // Start a new recording segment if still recording
            if (_isRecording)
            {
                CreateNewAudioFile(); // Creates the next temp file
            }

            // Process the completed chunk in the background
            Logger.Info($"Queueing chunk for transcription: {fileToProcess}");
            Task.Run(async () =>
            {
                string statusMsg = $"Transcribing chunk {Path.GetFileName(fileToProcess)}...";
                StatusChanged?.Invoke(this, statusMsg);
                try
                {
                    string transcriptionText = await _transcriptionService.TranscribeAudioFileAsync(fileToProcess); // Deletes file internally
                    TranscriptionReceived?.Invoke(this, transcriptionText);
                    Logger.Info($"Transcription successful for chunk: {fileToProcess}");
                    // Update status only if recording is ongoing, otherwise Stop handler updates it
                    if (_isRecording) StatusChanged?.Invoke(this, "Recording and transcribing...");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Transcription failed for chunk: {fileToProcess}", ex);
                    ErrorOccurred?.Invoke(this, ex);
                }
            });
        }

        private void CreateNewAudioFile()
        {
            try
            {
                _tempFilePath = Path.Combine(Path.GetTempPath(), $"audio_chunk_{DateTime.Now.Ticks}.wav");
                _writer = new WaveFileWriter(_tempFilePath, _capture.WaveFormat);
                _chunkStartTime = DateTime.Now; // Reset chunk start time
                Logger.Info($"Created new audio chunk file: {_tempFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to create new audio chunk file.", ex);
                ErrorOccurred?.Invoke(this, ex);
                // Consider stopping recording if file creation fails
                StopRecording();
            }
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            Logger.Info("Capture_RecordingStopped event received.");
            if (_writer != null)
            {
                 // Process any remaining audio
                var fileToProcess = _tempFilePath;
                _writer.Close();
                _writer = null;

            Task.Run(async () =>
            {
                string statusMsg = $"Transcribing final chunk {Path.GetFileName(fileToProcess)}...";
                StatusChanged?.Invoke(this, statusMsg);
                try
                {
                    Logger.Info($"Queueing final chunk for transcription: {fileToProcess}");
                    string transcriptionText = await _transcriptionService.TranscribeAudioFileAsync(fileToProcess); // Deletes file internally
                    TranscriptionReceived?.Invoke(this, transcriptionText);
                    Logger.Info($"Transcription successful for final chunk: {fileToProcess}");
                    StatusChanged?.Invoke(this, "Transcription complete."); // Final status update
                    }
                catch (Exception ex)
                {
                    Logger.Error($"Transcription failed for final chunk: {fileToProcess}", ex);
                    ErrorOccurred?.Invoke(this, ex);
                    }
                });
            }

            _isRecording = false;
            // Final duration is already stored in RecordedDuration before stop was called
            // Status is updated after final chunk processing now
            AudioLevelChanged?.Invoke(this, 0);

            if (e.Exception != null)
            {
                Logger.Error("Recording stopped with exception.", e.Exception);
                ErrorOccurred?.Invoke(this, e.Exception);
            }
        }

        public void Dispose()
        {
            Logger.Info("Disposing AudioCaptureService resources.");
            // Clean up resources
            if (_isRecording)
            {
                 Logger.Warning("Dispose called while still recording. Stopping capture.");
                 _capture?.StopRecording();
            }

            if (_writer != null)
            {
                Logger.Info("Closing wave writer during dispose.");
                _writer.Close();
                _writer = null;
            }

            Logger.Info("Disposing capture object.");
            _capture?.Dispose();

            // Clean up any temporary files (TranscriptionService also does this, but belt-and-suspenders)
            try
            {
                // Check if the last known temp file exists (it might have been processed already)
                if (!string.IsNullOrEmpty(_tempFilePath) && File.Exists(_tempFilePath))
                {
                    Logger.Info($"Deleting leftover temp file during dispose: {_tempFilePath}");
                    File.Delete(_tempFilePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Ignoring error during temp file cleanup on dispose: {ex.Message}");
                /* Ignore cleanup errors */
            }
        }
    }
}
