using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AudioTranscriptionApp
{
    public partial class MainWindow : Window
    {
        private WasapiLoopbackCapture capture;
        private WaveFileWriter writer;
        private string tempFilePath;
        private bool isRecording = false;
        private string apiKey = string.Empty;
        private readonly HttpClient httpClient = new HttpClient();
        private readonly int audioChunkSeconds = 10; // Process in 10-second chunks
        private DateTime recordingStartTime;

        // Audio device selection
        private List<AudioDeviceInfo> audioDevices = new List<AudioDeviceInfo>();
        private MMDeviceEnumerator deviceEnumerator = new MMDeviceEnumerator();
        private string selectedDeviceId = string.Empty;

        // Audio level monitoring
        private DispatcherTimer audioLevelTimer;
        private float currentAudioLevel = 0;
        private float peakValue = 0;
        private readonly float audioLevelSmoothingFactor = 0.2f; // For smoother level changes

        public MainWindow()
        {
            InitializeComponent();

            LoadAudioDevices();
            SetupAudioLevelMonitoring();

            // Try to load API key from settings if available
            try
            {
                apiKey = Properties.Settings.Default.ApiKey ?? string.Empty;
                if (!string.IsNullOrEmpty(apiKey))
                {
                    ApiKeyBox.Password = apiKey;
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }
            }
            catch
            {
                // Settings might not be properly configured, just continue
            }

            StatusTextBlock.Text = "Ready to record. Please enter your OpenAI API key.";

            // Display app instructions
            ShowInstructions();
        }

        private void ShowInstructions()
        {
            string instructions =
                "AUDIO TRANSCRIPTION APP INSTRUCTIONS:\n\n" +
                "1. Enter your OpenAI API key in the field at the top\n" +
                "2. Select an audio output device from the dropdown\n" +
                "3. Click 'Start Recording' to begin capturing audio\n" +
                "4. Audio will be transcribed in 10-second chunks\n" +
                "5. Click 'Stop Recording' when finished\n" +
                "6. Use 'Save Transcript' to save the text to a file\n\n" +
                "Note: This app captures system audio from the selected device.\n";

            TranscriptionTextBox.Text = instructions;
        }

        private void LoadAudioDevices()
        {
            audioDevices.Clear();

            try
            {
                // Get all audio output devices
                var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in devices)
                {
                    audioDevices.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        DisplayName = device.FriendlyName,
                        Device = device
                    });
                }

                AudioDevicesComboBox.ItemsSource = audioDevices;

                if (audioDevices.Count > 0)
                {
                    AudioDevicesComboBox.SelectedIndex = 0;
                    selectedDeviceId = audioDevices[0].Id;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading audio devices: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetupAudioLevelMonitoring()
        {
            // Create timer and set to update frequently
            audioLevelTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50) // Update 20 times per second
            };

            audioLevelTimer.Tick += AudioLevelTimer_Tick;

            // Debug confirmation
            Console.WriteLine("Audio level monitoring initialized");
            StatusTextBlock.Text = "Audio level monitoring ready";
        }

        private void AudioLevelTimer_Tick(object sender, EventArgs e)
        {
            if (isRecording && capture != null)
            {
                UpdateAudioLevel();
            }
            else
            {
                // Reset level when not recording
                SetAudioLevel(0);
            }
        }

        private void UpdateAudioLevel()
        {
            try
            {
                // Use exponential smoothing for a more natural-looking meter
                currentAudioLevel = (audioLevelSmoothingFactor * peakValue) +
                                   ((1 - audioLevelSmoothingFactor) * currentAudioLevel);

                // Reset peak for next interval
                peakValue = 0;

                // Debug output
                Console.WriteLine($"Updating audio level: {currentAudioLevel:F2}");

                // Update the UI
                SetAudioLevel(currentAudioLevel);
            }
            catch (Exception ex)
            {
                // Log audio visualization errors
                Console.WriteLine($"Error updating audio level: {ex.Message}");
            }
        }

        private void SetAudioLevel(float level)
        {
            // Ensure we're on the UI thread
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Convert level to percentage (0-100)
                    int levelPercentage = (int)(level * 100);

                    // Cap at 100%
                    if (levelPercentage > 100)
                        levelPercentage = 100;

                    // Get the actual width of the container (this is crucial)
                    double containerWidth = AudioLevelBarBackground.ActualWidth;

                    // Check if the container has a valid width
                    if (containerWidth > 0)
                    {
                        // Calculate the width of the level bar
                        double barWidth = (levelPercentage / 100.0) * containerWidth;

                        // Update progress bar width
                        AudioLevelBar.Width = barWidth;

                        // Update text
                        AudioLevelText.Text = $"Audio Level: {levelPercentage}%";

                        // Change color based on level
                        if (levelPercentage > 80)
                            AudioLevelBar.Fill = System.Windows.Media.Brushes.Red;
                        else if (levelPercentage > 60)
                            AudioLevelBar.Fill = System.Windows.Media.Brushes.Orange;
                        else
                            AudioLevelBar.Fill = System.Windows.Media.Brushes.Green;

                        // Debug output
                        Console.WriteLine($"Set audio level to {levelPercentage}%, Width: {barWidth}/{containerWidth}");
                    }
                    else
                    {
                        Console.WriteLine("Container width is 0 or negative");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in SetAudioLevel: {ex.Message}");
                }
            });
        }

        private void InitializeAudioCapture()
        {
            try
            {
                // Clean up existing capture if any
                if (capture != null)
                {
                    if (isRecording)
                    {
                        capture.StopRecording();
                    }

                    capture.Dispose();
                    capture = null;
                }

                // Initialize WASAPI loopback capture for the selected device
                if (string.IsNullOrEmpty(selectedDeviceId))
                {
                    // Use default device if none selected
                    capture = new WasapiLoopbackCapture();
                }
                else
                {
                    // Use selected device
                    var selectedDevice = audioDevices.FirstOrDefault(d => d.Id == selectedDeviceId)?.Device;
                    if (selectedDevice != null)
                    {
                        capture = new WasapiLoopbackCapture(selectedDevice);
                    }
                    else
                    {
                        capture = new WasapiLoopbackCapture();
                    }
                }

                // Set up audio data handling
                capture.DataAvailable += Capture_DataAvailable;
                capture.RecordingStopped += Capture_RecordingStopped;

                var deviceInfo = audioDevices.FirstOrDefault(d => d.Id == selectedDeviceId);
                string deviceName = deviceInfo != null ? deviceInfo.DisplayName : "Default Device";

                StatusTextBlock.Text = $"Audio capture initialized with {deviceName} - This will capture system audio";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Audio capture initialization error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (isRecording && writer != null)
            {
                // Write audio data to temporary file
                writer.Write(e.Buffer, 0, e.BytesRecorded);

                // Calculate audio level for visualization
                CalculateAudioLevel(e.Buffer, e.BytesRecorded);

                // Check if we've reached the chunk duration
                TimeSpan elapsed = DateTime.Now - recordingStartTime;
                if (elapsed.TotalSeconds >= audioChunkSeconds)
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
            WaveFormat format = capture.WaveFormat;

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
            if (maxValue > peakValue)
            {
                peakValue = maxValue;
            }

            // Debug output to check values
            if (maxValue > 0.01)
            {
                Dispatcher.Invoke(() => {
                    Console.WriteLine($"Audio level: {maxValue:F2}, Peak: {peakValue:F2}");
                });
            }
        }

        private void ProcessCurrentAudioChunk()
        {
            // Close the current writer
            var fileToProcess = tempFilePath;
            writer.Close();
            writer = null;

            // Start a new recording segment
            CreateNewAudioFile();

            // Process the completed chunk in the background
            Task.Run(() => TranscribeAudioFileAsync(fileToProcess));
        }

        private void CreateNewAudioFile()
        {
            tempFilePath = Path.Combine(Path.GetTempPath(), $"audio_chunk_{DateTime.Now.Ticks}.wav");
            writer = new WaveFileWriter(tempFilePath, capture.WaveFormat);
            recordingStartTime = DateTime.Now;
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (writer != null)
            {
                // Process any remaining audio
                var fileToProcess = tempFilePath;
                writer.Close();
                writer = null;

                Task.Run(() => TranscribeAudioFileAsync(fileToProcess));
            }

            isRecording = false;
            Dispatcher.Invoke(() => {
                StatusTextBlock.Text = "Recording stopped.";
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                TranscriptionProgress.Visibility = Visibility.Collapsed;

                // Stop audio level monitoring
                audioLevelTimer.Stop();
                SetAudioLevel(0);
            });

            if (e.Exception != null)
            {
                MessageBox.Show($"Recording error: {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task TranscribeAudioFileAsync(string audioFilePath)
        {
            try
            {
                Dispatcher.Invoke(() => {
                    StatusTextBlock.Text = "Transcribing audio...";
                    TranscriptionProgress.Visibility = Visibility.Visible;
                });

                using (var formContent = new MultipartFormDataContent())
                {
                    // Add the audio file
                    var fileContent = new ByteArrayContent(File.ReadAllBytes(audioFilePath));
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                    formContent.Add(fileContent, "file", "audio.wav");

                    // Add the model parameter (using the smallest model to save costs)
                    formContent.Add(new StringContent("whisper-1"), "model");

                    // Send to OpenAI Whisper API
                    var response = await httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", formContent);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<WhisperResponse>(jsonResponse);

                        // Update UI with transcribed text
                        Dispatcher.Invoke(() => {
                            TranscriptionTextBox.AppendText($"{result.Text}\n\n");
                            StatusTextBlock.Text = isRecording ? "Recording and transcribing..." : "Transcription complete.";
                            TranscriptionProgress.Visibility = isRecording ? Visibility.Visible : Visibility.Collapsed;
                        });
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Dispatcher.Invoke(() => {
                            StatusTextBlock.Text = $"API Error: {response.StatusCode}";
                            MessageBox.Show($"OpenAI API Error: {errorContent}", "Transcription Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                }

                // Clean up the temporary file
                if (File.Exists(audioFilePath))
                {
                    File.Delete(audioFilePath);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    StatusTextBlock.Text = $"Transcription error";
                    TranscriptionProgress.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Transcription error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show("Please enter your OpenAI API key first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                ApiKeyBox.Focus();
                return;
            }

            if (!isRecording)
            {
                // Initialize audio capture with selected device
                InitializeAudioCapture();

                isRecording = true;
                CreateNewAudioFile();

                try
                {
                    // Reset audio levels
                    currentAudioLevel = 0;
                    peakValue = 0;
                    SetAudioLevel(0);

                    // Start recording
                    capture.StartRecording();

                    recordingStartTime = DateTime.Now;

                    StartButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                    StatusTextBlock.Text = "Recording and transcribing...";
                    TranscriptionProgress.Visibility = Visibility.Visible;

                    // Start audio level monitoring - make sure it's running
                    if (audioLevelTimer != null)
                    {
                        audioLevelTimer.Stop(); // Stop if already running
                        audioLevelTimer.Start();

                        StatusTextBlock.Text += " (Level monitoring active)";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error starting recording: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    isRecording = false;
                    StartButton.IsEnabled = true;
                    StopButton.IsEnabled = false;
                }
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRecording)
            {
                capture.StopRecording();
                // The RecordingStopped event will handle cleanup
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TranscriptionTextBox.Text))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = "txt",
                    Title = "Save Transcription"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveFileDialog.FileName, TranscriptionTextBox.Text);
                    StatusTextBlock.Text = "Transcription saved.";
                }
            }
            else
            {
                MessageBox.Show("There is no transcription to save.", "No Content", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TranscriptionTextBox.Text))
            {
                if (MessageBox.Show("Are you sure you want to clear the transcription?",
                                    "Clear Transcription",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    TranscriptionTextBox.Clear();
                    StatusTextBlock.Text = "Transcription cleared.";
                }
            }
            else
            {
                StatusTextBlock.Text = "Transcription is already empty.";
            }
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            apiKey = ApiKeyBox.Password;

            if (!string.IsNullOrEmpty(apiKey))
            {
                // Update HttpClient authorization header
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                // Try to save API key to settings
                try
                {
                    Properties.Settings.Default.ApiKey = apiKey;
                    Properties.Settings.Default.Save();
                }
                catch
                {
                    // Settings might not be properly configured, just continue
                }
            }
        }

        private void AudioDevicesComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selectedDevice = AudioDevicesComboBox.SelectedItem as AudioDeviceInfo;
            if (selectedDevice != null)
            {
                selectedDeviceId = selectedDevice.Id;
                StatusTextBlock.Text = $"Selected {selectedDevice.DisplayName} - This will capture system audio";

                // If recording is in progress, we don't change the device
                if (!isRecording)
                {
                    InitializeAudioCapture();
                }
            }
        }

        private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            LoadAudioDevices();
            StatusTextBlock.Text = "Audio devices refreshed.";
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Clean up resources
            if (isRecording)
            {
                capture.StopRecording();
            }

            if (writer != null)
            {
                writer.Close();
                writer = null;
            }

            audioLevelTimer?.Stop();
            capture?.Dispose();
            httpClient?.Dispose();

            // Clean up any temporary files
            try
            {
                if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    // Audio device information class
    public class AudioDeviceInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public MMDevice Device { get; set; }
    }

    // Response model for OpenAI Whisper API
    public class WhisperResponse
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
