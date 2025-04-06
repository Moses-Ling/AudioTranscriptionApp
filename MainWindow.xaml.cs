﻿﻿﻿﻿﻿using AudioTranscriptionApp.Models;
using AudioTranscriptionApp.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography; // Keep for SettingsWindow interaction if needed later, or remove if truly unused
using System.Text; // Keep for SettingsWindow interaction if needed later, or remove if truly unused
using System.Windows;
using System.Windows.Controls;

namespace AudioTranscriptionApp
{
    public partial class MainWindow : Window
    {
        private AudioCaptureService _audioCaptureService;
        private TranscriptionService _transcriptionService;
        private bool _isRecording = false;
        private string _lastSaveDirectory = null; // To store the path of the last auto-save
        private int _audioTooShortWarningCount = 0; // Counter for specific API warnings

        public MainWindow()
        {
            Logger.Info("Application starting.");
            InitializeComponent();

            Logger.Info("Initializing services...");
            // Initialize services
            _transcriptionService = new TranscriptionService(string.Empty); // Initialize without key initially
            _audioCaptureService = new AudioCaptureService(_transcriptionService);

            // Set up event handlers
            Logger.Info("Setting up event handlers.");
            _audioCaptureService.AudioLevelChanged += (sender, level) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Update the text display
                    AudioLevelText.Text = $"Audio Level: {level:P0}";

                    // Update the visual bar width based on the level (0.0 to 1.0)
                    double containerWidth = ((Grid)AudioLevelBar.Parent).ActualWidth;
                    AudioLevelBar.Width = level * containerWidth;
                });
            };

            _audioCaptureService.TranscriptionReceived += (sender, text) =>
            {
                // Logger.Info($"Received transcription chunk: {text.Length} chars."); // Potentially too verbose
                Dispatcher.Invoke(() =>
                {
                    TranscriptionTextBox.AppendText($"{text}\n\n"); // Use AppendText for efficiency
                });
            };

            _audioCaptureService.StatusChanged += (sender, status) =>
            {
                Logger.Info($"Status changed: {status}");
                Dispatcher.Invoke(() => StatusTextBlock.Text = status);
            };

            _audioCaptureService.ErrorOccurred += AudioCaptureService_ErrorOccurred; // Use named method

            // Initialize audio devices
            Logger.Info("Initializing audio devices...");
            RefreshDevices();

            Logger.Info("Loading settings...");
            // Try to load API key from settings if available and update service
            try
            {
                string encryptedApiKey = Properties.Settings.Default.ApiKey ?? string.Empty;
                string decryptedApiKey = EncryptionHelper.DecryptString(encryptedApiKey); // Use helper
                if (!string.IsNullOrEmpty(decryptedApiKey))
                {
                     _transcriptionService.UpdateApiKey(decryptedApiKey); // Update the service on startup
                     Logger.Info("API key loaded from settings and applied to service.");
                }
                 else
                {
                    Logger.Info("No API key found in settings.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings on startup.", ex);
                // Settings might not be properly configured, just continue
            }

            // Show instructions
            ShowInstructions();
        }

        // Encryption/Decryption helpers moved to EncryptionHelper.cs

        private void ShowInstructions()
        {
            string instructions =
                "AUDIO TRANSCRIPTION APP INSTRUCTIONS:\n\n" +
                "1. Use the Settings button to configure your OpenAI API Key.\n" + // Updated instruction
                "2. Select an audio output device from the dropdown.\n" +
                "3. Click 'Start Recording' to begin capturing audio.\n" +
                "4. Audio will be transcribed in chunks (duration configured in Settings).\n" + // Updated instruction
                "5. Click 'Stop Recording' when finished.\n" +
                "6. Transcription is saved automatically to the folder configured in Settings.\n" + // Updated instruction
                "7. Use 'Open Folder' to view the saved transcription.\n\n" + // Updated instruction
                "Note: This app captures system audio from the selected device.\n";

            TranscriptionTextBox.Text = instructions;
            Logger.Info("Instructions displayed.");
        }

        private void RefreshDevices()
        {
            var devices = _audioCaptureService.GetAudioDevices();
            AudioDevicesComboBox.Items.Clear();

            foreach (var device in devices)
            {
                AudioDevicesComboBox.Items.Add(device);
            }

            if (AudioDevicesComboBox.Items.Count > 0)
            {
                AudioDevicesComboBox.SelectedIndex = 0;
            }

            StatusTextBlock.Text = "Audio devices refreshed.";
            Logger.Info($"Refreshed audio devices. Found {AudioDevicesComboBox.Items.Count}.");
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("StartButton clicked.");
            // Basic check: Ensure API key is loaded/set via settings before allowing recording.
            // A better check might involve a test API call in TranscriptionService.
            string encryptedKeyCheck = Properties.Settings.Default.ApiKey ?? string.Empty;
            if (string.IsNullOrEmpty(encryptedKeyCheck))
            {
                 Logger.Warning("Start recording attempted without API key configured.");
                 System.Windows.MessageBox.Show("Please configure your OpenAI API key via the Settings button first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                 return;
            }

            // Clear the text box and reset warnings before starting
            TranscriptionTextBox.Text = string.Empty;
            _lastSaveDirectory = null; // Reset last save path
            _audioTooShortWarningCount = 0; // Reset warning counter
            Logger.Info("Transcription text box cleared and warning count reset.");

            Logger.Info("Starting recording...");
            _audioCaptureService.StartRecording();
            _isRecording = true;

            // Update UI state
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            SaveButton.IsEnabled = false; // Disable "Open Folder" during recording
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("StopButton clicked. Stopping recording...");
            _audioCaptureService.StopRecording();
            _isRecording = false;

            // Update UI state
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;

            // --- Automatic Save Logic ---
            Dispatcher.InvokeAsync(() => // Ensure UI thread access if needed, though saving might be okay off-thread
            {
                string fullTranscription = TranscriptionTextBox.Text;
                if (string.IsNullOrWhiteSpace(fullTranscription) || fullTranscription.StartsWith("AUDIO TRANSCRIPTION APP INSTRUCTIONS"))
                {
                    Logger.Info("No significant transcription text found to auto-save.");
                    StatusTextBlock.Text = "Recording stopped. No text to save.";
                    return;
                }

                try
                {
                    string baseSavePath = Properties.Settings.Default.DefaultSavePath;
                    if (string.IsNullOrEmpty(baseSavePath))
                    {
                        baseSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AudioTranscriptions");
                        Logger.Info($"DefaultSavePath not set, using default: {baseSavePath}");
                    }

                    // Ensure base directory exists
                    if (!Directory.Exists(baseSavePath))
                    {
                        Logger.Info($"Base save directory does not exist, creating: {baseSavePath}");
                        Directory.CreateDirectory(baseSavePath);
                    }

                    // Create timestamped subdirectory
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string sessionDirectory = Path.Combine(baseSavePath, timestamp);
                    Directory.CreateDirectory(sessionDirectory);
                    Logger.Info($"Created session directory: {sessionDirectory}");

                    // Save transcription file
                    string filePath = Path.Combine(sessionDirectory, "transcription.txt");
                    File.WriteAllText(filePath, fullTranscription);
                    _lastSaveDirectory = sessionDirectory; // Store for "Open Folder" button
                    StatusTextBlock.Text = $"Transcription automatically saved to: {sessionDirectory}";
                    Logger.Info($"Transcription automatically saved to: {filePath}");
                    SaveButton.IsEnabled = true; // Enable the "Open Folder" button
                }
                catch (Exception ex)
                {
                    _lastSaveDirectory = null; // Reset on error
                    SaveButton.IsEnabled = false;
                    Logger.Error("Failed to automatically save transcription.", ex);
                    StatusTextBlock.Text = "Error saving transcription automatically.";
                    System.Windows.MessageBox.Show($"Failed to automatically save transcription: {ex.Message}", "Auto-Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            // --- End Automatic Save Logic ---
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Changed function: Open Last Save Folder
            Logger.Info("Open Folder button clicked.");
            if (!string.IsNullOrEmpty(_lastSaveDirectory) && Directory.Exists(_lastSaveDirectory))
            {
                try
                {
                    Logger.Info($"Opening folder: {_lastSaveDirectory}");
                    System.Diagnostics.Process.Start(_lastSaveDirectory);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to open folder: {_lastSaveDirectory}", ex);
                    System.Windows.MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (!string.IsNullOrEmpty(_lastSaveDirectory))
            {
                 Logger.Warning($"Last save directory not found: {_lastSaveDirectory}");
                 System.Windows.MessageBox.Show($"The last saved folder could not be found:\n{_lastSaveDirectory}", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                 SaveButton.IsEnabled = false; // Disable if folder is missing
            }
            else
            {
                Logger.Info("Open Folder button clicked, but no previous auto-save directory recorded.");
                System.Windows.MessageBox.Show("No transcription has been automatically saved in this session yet.", "No Folder", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("ClearButton clicked.");
            if (!string.IsNullOrEmpty(TranscriptionTextBox.Text))
            {
                // Use System.Windows.MessageBox explicitly
                if (System.Windows.MessageBox.Show("Are you sure you want to clear the transcription?",
                                    "Clear Transcription",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Logger.Info("Transcription cleared by user.");
                    TranscriptionTextBox.Text = string.Empty;
                    StatusTextBlock.Text = "Transcription cleared.";
                }
            }
            else
            {
                StatusTextBlock.Text = "Transcription is already empty.";
            }
        }

        private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("RefreshDevicesButton clicked.");
            RefreshDevices();
        }

        private void AudioDevicesComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (AudioDevicesComboBox.SelectedItem != null && AudioDevicesComboBox.SelectedItem is AudioDeviceModel selectedDevice)
            {
                Logger.Info($"Audio device selection changed to: {selectedDevice.DisplayName} (ID: {selectedDevice.Id})");
                if (!_isRecording)
                {
                     _audioCaptureService.InitializeAudioCapture(selectedDevice.Id);
                }
            }
        }

        // REMOVED ApiKeyBox_PasswordChanged event handler as the control is gone

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("SettingsButton clicked.");
            var settingsWindow = new SettingsWindow
            {
                Owner = this // Set the owner for centered startup
            };

            bool? result = settingsWindow.ShowDialog();
            Logger.Info($"Settings window closed with result: {result}");

            // If the user saved settings, reload them
            if (result == true)
            {
                Logger.Info("Reloading settings after save.");
                // Reload API key (decrypt) and update service
                try
                {
                    string encryptedApiKey = Properties.Settings.Default.ApiKey ?? string.Empty;
                    string decryptedApiKey = EncryptionHelper.DecryptString(encryptedApiKey);
                    // ApiKeyBox.Password = decryptedApiKey; // REMOVED - No UI element to update
                    _transcriptionService.UpdateApiKey(decryptedApiKey); // Update service
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to reload API key after settings change.", ex);
                    // Handle potential errors during reload if necessary
                    System.Windows.MessageBox.Show("Error reloading API key after settings change.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Explicit namespace
                }

                // Note: Changing ChunkDurationSeconds requires restarting the app
                // or re-initializing AudioCaptureService to take effect,
                // as it's read in the service constructor.
                // We could add a message here or enhance the service later.
                 int newChunkDuration = Properties.Settings.Default.ChunkDurationSeconds;
                 // Consider notifying the user if the duration changed and a restart is needed,
                 // or refactor AudioCaptureService to allow changing duration dynamically.
                 StatusTextBlock.Text = "Settings saved.";

            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Logger.Info("Window closing. Disposing resources.");
            _audioCaptureService?.Dispose();
            _transcriptionService?.Dispose();
            Logger.Info("--- Log Session Ended ---");
        }

        private void AudioCaptureService_ErrorOccurred(object sender, Exception ex)
        {
            // Check for the specific "audio_too_short" error
            // Note: This relies on the specific error message format from the API/TranscriptionService.
            // A more robust solution might involve custom exception types.
            bool isAudioTooShortError = ex.Message != null &&
                                        ex.Message.IndexOf("audio_too_short", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isAudioTooShortError)
            {
                _audioTooShortWarningCount++;
                string warningMsg = $"Warning ({_audioTooShortWarningCount}/3): Audio too short or silent. Check audio device or speak clearly.";
                Logger.Warning($"Audio too short warning received. Count: {_audioTooShortWarningCount}. Full error: {ex.Message}");

                Dispatcher.Invoke(() =>
                {
                    TranscriptionTextBox.AppendText($"\n--- {warningMsg} ---\n");
                    StatusTextBlock.Text = warningMsg;

                    if (_audioTooShortWarningCount >= 3)
                    {
                        Logger.Warning("Stopping recording due to repeated 'audio too short' warnings.");
                        StatusTextBlock.Text = "Stopping recording: Repeated audio issues detected.";
                        // Programmatically click the stop button to trigger cleanup and state change
                        StopButton_Click(null, null);
                    }
                });
            }
            else
            {
                // Handle other errors normally
                Logger.Error("Error occurred in AudioCaptureService.", ex);
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = $"Error: {ex.Message}";
                    System.Windows.MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Explicit namespace
                });
            }
        }
    }
}
