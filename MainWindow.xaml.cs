﻿﻿using AudioTranscriptionApp.Models;
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
using Markdig; // Added for Markdown conversion
using System.Diagnostics; // Added for Process.Start
using System.Windows.Media; // Added for Brushes


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
        // Removed _finalRecordedDuration as service now tracks total time

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
            _audioCaptureService.RecordingTimeUpdate += AudioCaptureService_RecordingTimeUpdate; // Subscribe to timer event

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
            // Updated instructions reflecting UI changes
            // Updated instructions reflecting UI changes and feedback
            string instructions =
                "AUDIO TRANSCRIPTION APP INSTRUCTIONS:\n\n" +
                "1. Click 'Settings' to configure API Keys (Whisper, Cleanup, Summarize) and other options.\n" +
                "2. Select the desired Audio Device for recording system output.\n" +
                "3. Click 'Start' to begin transcribing.\n" + // Changed "recording" to "transcribing"
                "4. Click 'Stop' when finished. Transcription is saved automatically.\n" +
                "5. Click 'Clean Up' to refine the transcription using AI.\n" + // Removed "(optional)"
                "6. Click 'Summarize' to generate a summary using AI.\n" + // Removed "(optional)"
                "7. Click 'Clear' to clear the text box.\n\n" + // Removed "for a new session"
                "Note: Recording duration and save location are set in Settings.";

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
            ElapsedTimeTextBlock.Text = "Rec: 0s"; // Initialize timer text
            ElapsedTimeTextBlock.Visibility = Visibility.Visible; // Show timer
            Logger.Info("Transcription text box cleared and warning count reset.");

            Logger.Info("Starting recording...");
            _audioCaptureService.StartRecording();
            _isRecording = true;
            SetUiBusyState(true); // Disable buttons
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("StopButton clicked. Stopping recording...");
            // Get final duration *before* stopping capture might reset it internally
            TimeSpan finalDuration = _audioCaptureService.RecordedDuration;
            _audioCaptureService.StopRecording(); // This triggers events including final transcription
            _isRecording = false;
            // Update timer display with final duration
            ElapsedTimeTextBlock.Text = $"Total: {finalDuration.TotalSeconds:F0}s";
            // Don't call SetUiBusyState(false) here immediately,
            // let the auto-save logic enable buttons once it's done (or failed)

            // --- Automatic Save Logic ---
            Dispatcher.InvokeAsync(() =>
            {
                string fullTranscription = TranscriptionTextBox.Text;
                bool hasTextToSave = !string.IsNullOrWhiteSpace(fullTranscription) && !fullTranscription.StartsWith("AUDIO TRANSCRIPTION APP INSTRUCTIONS");

                if (!hasTextToSave)
                {
                    Logger.Info("No significant transcription text found to auto-save.");
                    StatusTextBlock.Text = "Recording stopped. No text to save.";
                    SetUiBusyState(false); // Enable buttons now
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
                    // Enable buttons AFTER successful save
                    SetUiBusyState(false);
                }
                catch (Exception ex)
                {
                    _lastSaveDirectory = null;
                    Logger.Error("Failed to automatically save transcription.", ex);
                    StatusTextBlock.Text = "Error saving transcription automatically.";
                    System.Windows.MessageBox.Show($"Failed to automatically save transcription: {ex.Message}", "Auto-Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetUiBusyState(false); // Enable buttons even on save error
                }
            });
            // --- End Automatic Save Logic ---
        }

        private async void CleanupButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("CleanupButton clicked.");
            ElapsedTimeTextBlock.Visibility = Visibility.Collapsed; // Hide timer

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
                         // Explicitly disable after successful save
                         // CleanupButton.IsEnabled = false; // Let SetUiBusyState handle this based on file existence
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
                SetUiBusyState(false); // Re-enable most buttons
            }
        }

        private async void SummarizeButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("SummarizeButton clicked.");
            ElapsedTimeTextBlock.Visibility = Visibility.Collapsed; // Hide timer

            if (_isRecording)
            {
                System.Windows.MessageBox.Show("Please stop recording before summarizing text.", "Recording Active", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string textToSummarize = TranscriptionTextBox.Text;
            if (string.IsNullOrWhiteSpace(textToSummarize) || textToSummarize.StartsWith("AUDIO TRANSCRIPTION APP INSTRUCTIONS"))
            {
                Logger.Warning("Summarize attempted with no significant text.");
                System.Windows.MessageBox.Show("There is no text to summarize.", "No Text", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Read settings for summarize
            string summarizeModel = Properties.Settings.Default.SummarizeModel;
            string summarizePrompt = Properties.Settings.Default.SummarizePrompt;
            string encryptedSummarizeKey = Properties.Settings.Default.SummarizeApiKey ?? string.Empty;
            string decryptedSummarizeKey = EncryptionHelper.DecryptString(encryptedSummarizeKey);

            if (string.IsNullOrEmpty(decryptedSummarizeKey))
            {
                 Logger.Warning("Summarize attempted without Summarize API key configured.");
                 System.Windows.MessageBox.Show("Please configure your OpenAI API key for Summarize in the Settings window first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                 return;
            }
            // Use the same chat service instance, just update the key if needed
            _openAiChatService.UpdateApiKey(decryptedSummarizeKey);

            SetUiBusyState(true, "Summarizing text...");

            try
            {
                Logger.Info($"Calling OpenAI Chat API (Model: {summarizeModel}) for summarization...");

                // Call the chat service
                string summaryMd = await _openAiChatService.GetResponseAsync(summarizePrompt, textToSummarize, summarizeModel);

                if (summaryMd != null)
                {
                    // Update UI
                    TranscriptionTextBox.Text = summaryMd; // Display Markdown directly for now
                    Logger.Info("Summarization API call successful.");

                    // Save the summary file
                    if (!string.IsNullOrEmpty(_lastSaveDirectory) && Directory.Exists(_lastSaveDirectory))
                    {
                        try
                        {
                            string savePath = Path.Combine(_lastSaveDirectory, "summary.md");
                            File.WriteAllText(savePath, summaryMd);
                            StatusTextBlock.Text = $"Summarization complete. Saved summary.md to: {_lastSaveDirectory}";
                            Logger.Info($"Summary saved to: {savePath}");
                        }
                        catch (Exception saveEx)
                        {
                            Logger.Error($"Failed to save summary.md to {_lastSaveDirectory}", saveEx);
                            StatusTextBlock.Text = "Summarization complete, but failed to save summary.md.";
                            System.Windows.MessageBox.Show($"Summarization was successful, but failed to save summary.md: {saveEx.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        StatusTextBlock.Text = "Summarization complete. Could not save summary.md (Save directory missing).";
                        Logger.Warning("Summarization complete but could not save file as _lastSaveDirectory was not set or invalid.");
                    }
                     // --- Browser Opening Logic ---
                     try
                     {
                         string htmlContent = GenerateHtmlWithCopy(summaryMd); // Use updated helper
                         string tempHtmlPath = Path.Combine(Path.GetTempPath(), $"summary_{DateTime.Now.Ticks}.html");
                         File.WriteAllText(tempHtmlPath, htmlContent);
                         Logger.Info($"Saved temporary HTML summary to: {tempHtmlPath}");

                         Process.Start(tempHtmlPath);
                         Logger.Info("Opened summary in default browser.");
                     }
                     catch(Exception browserEx)
                     {
                          Logger.Error("Failed to convert summary to HTML or open in browser.", browserEx);
                          System.Windows.MessageBox.Show($"Could not open summary in browser: {browserEx.Message}", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                     }
                     // --- End Browser Opening Logic ---
                 }
                 else
                 {
                    Logger.Error("Summarization API call returned null response.");
                    StatusTextBlock.Text = "Summarization failed: Received empty response from API.";
                    System.Windows.MessageBox.Show("Summarization failed: Received an empty response from the API.", "Summarize Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error during summarization process.", ex);
                StatusTextBlock.Text = $"Summarization failed: {ex.Message}";
                System.Windows.MessageBox.Show($"Summarization failed: {ex.Message}", "Summarize Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetUiBusyState(false); // Re-enable buttons
            }
        }


        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("ClearButton clicked.");
            ElapsedTimeTextBlock.Visibility = Visibility.Collapsed; // Hide timer
            if (!string.IsNullOrEmpty(TranscriptionTextBox.Text))
            {
                if (System.Windows.MessageBox.Show("Are you sure you want to clear the transcription?",
                                    "Clear Transcription", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Logger.Info("Transcription cleared by user.");
                    TranscriptionTextBox.Text = string.Empty;
                    StatusTextBlock.Text = "Transcription cleared.";
                    _lastSaveDirectory = null; // Reset save directory as well
                    SetUiBusyState(false); // Update button states
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

                    // Reload Summarize Key
                    string encryptedSummarizeKey = Properties.Settings.Default.SummarizeApiKey ?? string.Empty;
                    string decryptedSummarizeKey = EncryptionHelper.DecryptString(encryptedSummarizeKey);
                    // Assuming Summarize uses the same service instance for now
                    // If a separate service existed: _summarizeService.UpdateApiKey(decryptedSummarizeKey);
                    _openAiChatService.UpdateApiKey(decryptedSummarizeKey); // Update chat service again if key is different

                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to reload API key after settings change.", ex);
                    System.Windows.MessageBox.Show("Error reloading API key after settings change.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                StatusTextBlock.Text = "Settings saved.";
            }
        }

         private string GenerateHtmlWithCopy(string markdownContent)
         {
             // Convert Markdown to HTML using Markdig
             var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
             string htmlBody = Markdown.ToHtml(markdownContent, pipeline);

             // Simple HTML template with basic styling and copy functionality
             return $@"
 <!DOCTYPE html>
 <html lang=""en"">
 <head>
     <meta charset=""UTF-8"">
     <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
     <title>Summary</title>
     <style>
         body {{ font-family: sans-serif; line-height: 1.6; padding: 20px; }}
         /* Add styles for common markdown elements if needed */
         h1, h2, h3 {{ margin-top: 1em; margin-bottom: 0.5em; }}
         ul, ol {{ margin-left: 20px; }}
         code {{ background-color: #f0f0f0; padding: 2px 4px; font-family: monospace; }}
         pre {{ background-color: #f4f4f4; padding: 10px; border: 1px solid #ddd; white-space: pre-wrap; word-wrap: break-word; }}
         button {{ padding: 8px 15px; margin-bottom: 15px; cursor: pointer; }}
     </style>
 </head>
 <body>
     <button id=""copyButton"" onclick=""copySummary()"">Copy Summary</button>
     <hr>
     <div id='summaryBody'>
     {htmlBody}
     </div>

     <script>
         function copySummary() {{
             const contentToCopy = document.getElementById('summaryBody');
             const button = document.getElementById('copyButton');
             let success = false;
             try {{
                 const range = document.createRange();
                 range.selectNodeContents(contentToCopy);
                 window.getSelection().removeAllRanges(); // Clear previous selection
                 window.getSelection().addRange(range); // Select the content
                 success = document.execCommand('copy'); // Execute copy command
                 window.getSelection().removeAllRanges(); // Deselect
             }} catch (err) {{
                 console.error('Failed to copy using execCommand:', err);
                 success = false;
             }}

             if (success) {{
                 button.textContent = 'Copied!';
                 setTimeout(() => {{ button.textContent = 'Copy Summary'; }}, 2000);
             }} else {{
                 alert('Failed to copy summary. Your browser might not support this action.');
             }}
         }}
     </script>
 </body>
 </html>";
         }

         private void SetUiBusyState(bool isBusy, string statusText = null)
        {
             StartButton.IsEnabled = !isBusy;
             StopButton.IsEnabled = isBusy && _isRecording;
             // Enable Cleanup/Summarize only if NOT busy AND text exists
             bool canProcessText = !isBusy && !string.IsNullOrEmpty(TranscriptionTextBox.Text) && !TranscriptionTextBox.Text.StartsWith("AUDIO TRANSCRIPTION APP INSTRUCTIONS");
             // Check if cleaned.txt exists
             bool cleanedFileExists = !string.IsNullOrEmpty(_lastSaveDirectory) && File.Exists(Path.Combine(_lastSaveDirectory, "cleaned.txt"));
             CleanupButton.IsEnabled = canProcessText && !string.IsNullOrEmpty(_lastSaveDirectory) && !cleanedFileExists; // Enable only if not busy, save dir exists, AND cleaned.txt doesn't exist
             SummarizeButton.IsEnabled = canProcessText; // Enable summarize if text exists
             ClearButton.IsEnabled = !isBusy;
             RefreshDevicesButton.IsEnabled = !isBusy;
             SettingsButton.IsEnabled = !isBusy;
             AudioDevicesComboBox.IsEnabled = !isBusy;

             // Control Busy Indicator visibility and color
             if (isBusy)
             {
                 BusyIndicator.Visibility = Visibility.Visible;
                 // Set color based on whether recording is active
                 if (_isRecording)
                 {
                     BusyIndicator.Foreground = Brushes.Red; // Red when recording
                 }
                 else
                 {
                     // Use default system highlight color for non-recording busy states (Cleanup/Summarize)
                     BusyIndicator.Foreground = SystemColors.HighlightBrush;
                 }
             }
             else
             {
                 BusyIndicator.Visibility = Visibility.Collapsed;
             }


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

        // Event handler for recording time updates from the service
        private void AudioCaptureService_RecordingTimeUpdate(object sender, TimeSpan elapsed)
        {
            // Update the UI on the main thread
            Dispatcher.Invoke(() =>
            {
                ElapsedTimeTextBlock.Text = $"Rec: {elapsed.TotalSeconds:F0}s";
            });
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
