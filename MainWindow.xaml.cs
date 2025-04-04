﻿﻿using AudioTranscriptionApp.Models;
using AudioTranscriptionApp.Services;
using AudioTranscriptionApp.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AudioTranscriptionApp
{
    public partial class MainWindow : Window
    {
        private AudioCaptureService _audioCaptureService;
        private TranscriptionService _transcriptionService;
        private bool _isRecording = false;

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize services
            _transcriptionService = new TranscriptionService(string.Empty);
            _audioCaptureService = new AudioCaptureService(_transcriptionService);
            
            // Set up event handlers
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
                Dispatcher.Invoke(() =>
                {
                    TranscriptionTextBox.Text += $"{text}\n\n";
                });
            };

            _audioCaptureService.StatusChanged += (sender, status) =>
            {
                Dispatcher.Invoke(() => StatusTextBlock.Text = status);
            };

            _audioCaptureService.ErrorOccurred += (sender, ex) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = $"Error: {ex.Message}";
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            };

            // Initialize audio devices
            RefreshDevices();
            
            // Try to load API key from settings if available
            try
            {
                ApiKeyBox.Password = Properties.Settings.Default.ApiKey ?? string.Empty;
                if (!string.IsNullOrEmpty(ApiKeyBox.Password))
                {
                    _transcriptionService.UpdateApiKey(ApiKeyBox.Password);
                }
            }
            catch
            {
                // Settings might not be properly configured, just continue
            }

            // Show instructions
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
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ApiKeyBox.Password))
            {
                MessageBox.Show("Please enter your OpenAI API key first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _audioCaptureService.StartRecording();
            _isRecording = true;
            
            // Update UI state
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _audioCaptureService.StopRecording();
            _isRecording = false;
            
            // Update UI state
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
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
            RefreshDevices();
        }

        private void AudioDevicesComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (AudioDevicesComboBox.SelectedItem != null && AudioDevicesComboBox.SelectedItem is AudioDeviceModel selectedDevice)
            {
                if (!_isRecording)
                {
                    _audioCaptureService.InitializeAudioCapture(selectedDevice.Id);
                }
            }
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _transcriptionService.UpdateApiKey(ApiKeyBox.Password);
            
            // Try to save API key to settings
            try
            {
                Properties.Settings.Default.ApiKey = ApiKeyBox.Password;
                Properties.Settings.Default.Save();
            }
            catch
            {
                // Settings might not be properly configured, just continue
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _audioCaptureService?.Dispose();
            _transcriptionService?.Dispose();
        }
    }
}
