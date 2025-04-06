﻿using AudioTranscriptionApp.Models;
using AudioTranscriptionApp.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography; // Keep for SettingsWindow interaction if needed later, or remove if truly unused
using System.Text; // Keep for SettingsWindow interaction if needed later, or remove if truly unused
using System.Threading.Tasks; // Added for async void handler
using System.Windows;
using System.Windows.Controls;


namespace AudioTranscriptionApp
{
    public partial class MainWindow : Window
    {
        private AudioCaptureService _audioCaptureService;
        private TranscriptionService _transcriptionService;
        private OpenAiChatService _openAiChatService; // Added Cleanup Service
        private bool _isRecording = false;
        private string _lastSaveDirectory = null; // To store the path of the last auto-save
        private int _audioTooShortWarningCount = 0; // Counter for specific API warnings

        public MainWindow()
        {
            Logger.Info("Application starting.");
            InitializeComponent();

            Logger.Info("Initializing services...");
            // Initialize services
            _transcriptionService = new TranscriptionService(string.Empty); // Initialize whisper without key initially
            _openAiChatService = new OpenAiChatService(string.Empty); // Initialize cleanup without key initially
            _audioCaptureService = new AudioCaptureService(_transcriptionService);

            // Set up event handlers
            Logger.Info("Setting up event handlers.");
            _audioCaptureService.AudioLevelChanged += (sender, level) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AudioLevelText.Text = $"Audio Level: {level:P0}";
                    double containerWidth = ((Grid)AudioLevelBar.Parent).ActualWidth;
                    AudioLevelBar.Width = level * containerWidth;
                });
            };

            _audioCaptureService.TranscriptionReceived += (sender, text) =>
            {
                Dispatcher.Invoke(() =>
                {
                    TranscriptionTextBox.AppendText($"{text}\n\n");
                });
            };

            _audioCaptureService.StatusChanged += (sender, status) =>
            {
                Logger.Info($"Status changed: {status}");
                Dispatcher.Invoke(() => StatusTextBlock.Text = status);
            };

            _audioCaptureService.ErrorOccurred += AudioCaptureService_ErrorOccurred;

            // Initialize audio devices
            Logger.Info("Initializing audio devices...");
            RefreshDevices();

            Logger.Info("Loading settings...");
            // Try to load API keys from settings if available and update services
            try
            {
                // Whisper Key
                string encryptedApiKey = Properties.Settings.Default.ApiKey ?? string.Empty;
                string decryptedApiKey = EncryptionHelper.DecryptString(encryptedApiKey);
                if (!string.IsNullOrEmpty(decryptedApiKey))
                {
                     _transcriptionService.UpdateApiKey(decryptedApiKey);
                     Logger.Info("Whisper API key loaded from settings and applied to service.");
                }
                 else
                {
                    Logger.Info("No Whisper API key found in settings.");
                }

                // Cleanup Key
                string encryptedCleanupKey = Properties.Settings.Default.CleanupApiKey ?? string.Empty;
                string decryptedCleanupKey = EncryptionHelper.DecryptString(encryptedCleanupKey);
                if (!string.IsNullOrEmpty(decryptedCleanupKey))
                {
                    _openAiChatService.UpdateApiKey(decryptedCleanupKey);
                    Logger.Info("Cleanup API key loaded from settings and applied to service.");
                }
                else
                {
                     Logger.Info("No Cleanup API key found in settings.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings on startup.", ex);
            }

            // Show instructions
            ShowInstructions();
        }

        private void ShowInstructions()
        {
            string instructions =
                "AUDIO TRANSCRIPTION APP INSTRUCTIONS:\n\n" +
                "1. Use the Settings button to configure your OpenAI API Keys.\n" + // Updated
                "2. Select an audio output device from the dropdown.\n" +
                "3. Click 'Start Recording' to begin capturing audio.\n" +
                "4. Audio will be transcribed in chunks (duration configured in Settings).\n" +
                "5. Click 'Stop Recording' when finished.\n" +
                "6. Transcription is saved automatically to the folder configured in Settings.\n" +
                "7. Click 'Clean Up' to process the text with the configured LLM.\n" + // Updated
                "8. The cleaned text is saved automatically as 'cleaned.txt'.\n\n" + // Updated
                "Note: This app captures system audio from the selected device.\n";

            TranscriptionTextBox.Text = instructions;
            Logger.Info("Instructions displayed.");
        }

        private void RefreshDevices()
        {
            var devices = _audioCaptureService.GetAudioDevices();
            AudioDevicesComboBox.Items.Clear();
            foreach (var device in devices) { AudioDevicesComboBox.Items.Add(device); }
            if (AudioDevicesComboBox.Items.Count > 0) { AudioDevicesComboBox.SelectedIndex = 0; }
            StatusTextBlock.Text = "Audio devices refreshed.";
            Logger.Info($"Refreshed audio devices. Found {AudioDevicesComboBox.Items.Count}.");
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("StartButton clicked.");
            string encryptedKeyCheck = Properties.Settings.Default.ApiKey ?? string.Empty;
            if (string.IsNullOrEmpty(encryptedKeyCheck))
            {
                 Logger.Warning("Start recording attempted without Whisper API key configured.");
                 System.Windows.MessageBox.Show("Please configure your OpenAI API key for Whisper in the Settings window first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                 return;
            }

            TranscriptionTextBox.Text = string.Empty;
            _lastSaveDirectory = null;
            _audioTooShortWarningCount = 0;
            Logger.Info("Transcription text box cleared and warning count reset.");

            Logger.Info("Starting recording...");
            _audioCaptureService.StartRecording();
            _isRecording = true;
            SetUiBusyState(true); // Disable buttons
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("StopButton clicked. Stopping recording...");
            _audioCaptureService.StopRecording(); // This triggers events including final transcription
            _isRecording = false;
            SetUiBusyState(false); // Re-enable buttons (Cleanup might be enabled below)

            // --- Automatic Save Logic ---
            Dispatcher.InvokeAsync(() =>
            {
                string fullTranscription = TranscriptionTextBox.Text;
                if (string.IsNullOrWhiteSpace(fullTranscription) || fullTranscription.StartsWith("AUDIO TRANSCRIPTION APP INSTRUCTIONS"))
                {
                    Logger.Info("No significant transcription text found to auto-save.");
                    StatusTextBlock.Text = "Recording stopped. No text to save.";
                    CleanupButton.IsEnabled = false; // Ensure disabled
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

                    if (!Directory.Exists(baseSavePath))
                    {
                        Logger.Info($"Base save directory does not exist, creating: {baseSavePath}");
                        Directory.CreateDirectory(baseSavePath);
                    }

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string sessionDirectory = Path.Combine(baseSavePath, timestamp);
                    Directory.CreateDirectory(sessionDirectory);
                    Logger.Info($"Created session directory: {sessionDirectory}");

                    string filePath = Path.Combine(sessionDirectory, "transcription.txt");
                    File.WriteAllText(filePath, fullTranscription);
                    _lastSaveDirectory = sessionDirectory;
                    StatusTextBlock.Text = $"Transcription automatically saved to: {sessionDirectory}";
                    Logger.Info($"Transcription automatically saved to: {filePath}");
                    CleanupButton.IsEnabled = true; // Enable the "Clean Up" button
                }
                catch (Exception ex)
                {
                    _lastSaveDirectory = null;
                    CleanupButton.IsEnabled = false; // Ensure disabled on error
                    Logger.Error("Failed to automatically save transcription.", ex);
                    StatusTextBlock.Text = "Error saving transcription automatically.";
                    System.Windows.MessageBox.Show($"Failed to automatically save transcription: {ex.Message}", "Auto-Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            // --- End Automatic Save Logic ---
        }

        private async void CleanupButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("CleanupButton clicked.");

            if (_isRecording)
            {
                System.Windows.MessageBox.Show("Please stop recording before cleaning up text.", "Recording Active", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string originalText = TranscriptionTextBox.Text;
            if (string.IsNullOrWhiteSpace(originalText) || originalText.StartsWith("AUDIO TRANSCRIPTION APP INSTRUCTIONS"))
            {
                Logger.Warning("Cleanup attempted with no significant text.");
                System.Windows.MessageBox.Show("There is no transcription text to clean up.", "No Text", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(_lastSaveDirectory) || !Directory.Exists(_lastSaveDirectory))
            {
                 Logger.Warning("Cleanup attempted but no valid save directory exists for this session.");
                 System.Windows.MessageBox.Show("Cannot clean up text as the automatic save directory for this session was not created or found.", "Save Directory Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                 CleanupButton.IsEnabled = false;
                 return;
            }

            // Read settings for cleanup
            string cleanupModel = Properties.Settings.Default.CleanupModel;
            string cleanupPrompt = Properties.Settings.Default.CleanupPrompt;
            string encryptedCleanupKey = Properties.Settings.Default.CleanupApiKey ?? string.Empty;
            string decryptedCleanupKey = EncryptionHelper.DecryptString(encryptedCleanupKey);

            if (string.IsNullOrEmpty(decryptedCleanupKey))
            {
                 Logger.Warning("Cleanup attempted without Cleanup API key configured.");
                 System.Windows.MessageBox.Show("Please configure your OpenAI API key for Cleanup in the Settings window first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                 return;
            }
            _openAiChatService.UpdateApiKey(decryptedCleanupKey);

            SetUiBusyState(true, "Cleaning up text...");

            try
            {
                Logger.Info($"Calling OpenAI Chat API (Model: {cleanupModel}) for cleanup...");

                // Call the actual service
                string cleanedText = await _openAiChatService.GetResponseAsync(cleanupPrompt, originalText, cleanupModel);

                if (cleanedText != null) // Check if service returned a valid response
                {
                    // Update UI on success
                    TranscriptionTextBox.Text = cleanedText;
                    Logger.Info("Cleanup API call successful.");

                    // Save the cleaned text
                    try
                    {
                        string savePath = Path.Combine(_lastSaveDirectory, "cleaned.txt");
                        File.WriteAllText(savePath, cleanedText);
                        StatusTextBlock.Text = $"Cleanup complete. Saved cleaned.txt to: {_lastSaveDirectory}";
                        Logger.Info($"Cleaned text saved to: {savePath}");
                    }
                    catch (Exception saveEx)
                    {
                        Logger.Error($"Failed to save cleaned text to {_lastSaveDirectory}", saveEx);
                        StatusTextBlock.Text = "Cleanup complete, but failed to save cleaned.txt.";
                        System.Windows.MessageBox.Show($"Cleanup was successful, but failed to save cleaned.txt: {saveEx.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    // Handle case where service returns null unexpectedly
                    Logger.Error("Cleanup API call returned null response.");
                    StatusTextBlock.Text = "Cleanup failed: Received empty response from API.";
                    System.Windows.MessageBox.Show("Cleanup failed: Received an empty response from the API.", "Cleanup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error during cleanup process.", ex);
                StatusTextBlock.Text = $"Cleanup failed: {ex.Message}";
                System.Windows.MessageBox.Show($"Cleanup failed: {ex.Message}", "Cleanup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetUiBusyState(false);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("ClearButton clicked.");
            if (!string.IsNullOrEmpty(TranscriptionTextBox.Text))
            {
                if (System.Windows.MessageBox.Show("Are you sure you want to clear the transcription?",
                                    "Clear Transcription", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Logger.Info("Transcription cleared by user.");
                    TranscriptionTextBox.Text = string.Empty;
                    StatusTextBlock.Text = "Transcription cleared.";
                    CleanupButton.IsEnabled = false; // Disable cleanup if text is cleared
                    _lastSaveDirectory = null; // Reset save directory as well
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

        // REMOVED ApiKeyBox_PasswordChanged event handler

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("SettingsButton clicked.");
            var settingsWindow = new SettingsWindow { Owner = this };
            bool? result = settingsWindow.ShowDialog();
            Logger.Info($"Settings window closed with result: {result}");

            if (result == true)
            {
                Logger.Info("Reloading settings after save.");
                try
                {
                    // Reload Whisper Key
                    string encryptedApiKey = Properties.Settings.Default.ApiKey ?? string.Empty;
                    string decryptedApiKey = EncryptionHelper.DecryptString(encryptedApiKey);
                    _transcriptionService.UpdateApiKey(decryptedApiKey);

                    // Reload Cleanup Key
                    string encryptedCleanupKey = Properties.Settings.Default.CleanupApiKey ?? string.Empty;
                    string decryptedCleanupKey = EncryptionHelper.DecryptString(encryptedCleanupKey);
                    _openAiChatService.UpdateApiKey(decryptedCleanupKey);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to reload API key after settings change.", ex);
                    System.Windows.MessageBox.Show("Error reloading API key after settings change.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                StatusTextBlock.Text = "Settings saved.";
            }
        }

         private void SetUiBusyState(bool isBusy, string statusText = null)
        {
             StartButton.IsEnabled = !isBusy;
             StopButton.IsEnabled = isBusy && _isRecording;
             // Enable Cleanup only if NOT busy AND a save directory exists
             CleanupButton.IsEnabled = !isBusy && !string.IsNullOrEmpty(_lastSaveDirectory);
             ClearButton.IsEnabled = !isBusy;
             RefreshDevicesButton.IsEnabled = !isBusy;
             SettingsButton.IsEnabled = !isBusy;
             AudioDevicesComboBox.IsEnabled = !isBusy;

             if (statusText != null)
             {
                 StatusTextBlock.Text = statusText;
             }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Logger.Info("Window closing. Disposing resources.");
            _audioCaptureService?.Dispose();
            _transcriptionService?.Dispose();
            _openAiChatService?.Dispose(); // Dispose chat service
            Logger.Info("--- Log Session Ended ---");
        }

        private void AudioCaptureService_ErrorOccurred(object sender, Exception ex)
        {
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
                        StopButton_Click(null, null);
                    }
                });
            }
            else
            {
                Logger.Error("Error occurred in AudioCaptureService.", ex);
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = $"Error: {ex.Message}";
                    System.Windows.MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }
}
