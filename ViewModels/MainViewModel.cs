using AudioTranscriptionApp.Models;
using AudioTranscriptionApp.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AudioTranscriptionApp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly AudioCaptureService _audioCaptureService;
        private readonly TranscriptionService _transcriptionService;
        private readonly DispatcherTimer _audioLevelTimer;

        private string _apiKey = string.Empty;
        private ObservableCollection<AudioDeviceModel> _audioDevices;
        private AudioDeviceModel _selectedAudioDevice;
        private string _transcriptionText = string.Empty;
        private string _statusText = "Ready to record. Please enter your OpenAI API key.";
        private bool _isRecording = false;
        private bool _isTranscribing = false;
        private float _audioLevel = 0;

        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (SetProperty(ref _apiKey, value))
                {
                    _transcriptionService.UpdateApiKey(value);
                    
                    // Try to save API key to settings
                    try
                    {
                        Properties.Settings.Default.ApiKey = value;
                        Properties.Settings.Default.Save();
                    }
                    catch
                    {
                        // Settings might not be properly configured, just continue
                    }
                }
            }
        }

        public ObservableCollection<AudioDeviceModel> AudioDevices
        {
            get => _audioDevices;
            set => SetProperty(ref _audioDevices, value);
        }

        public AudioDeviceModel SelectedAudioDevice
        {
            get => _selectedAudioDevice;
            set
            {
                if (SetProperty(ref _selectedAudioDevice, value) && value != null)
                {
                    if (!_isRecording)
                    {
                        _audioCaptureService.InitializeAudioCapture(value.Id);
                    }
                }
            }
        }

        public string TranscriptionText
        {
            get => _transcriptionText;
            set => SetProperty(ref _transcriptionText, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (SetProperty(ref _isRecording, value))
                {
                    ((RelayCommand)StartRecordingCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)StopRecordingCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsTranscribing
        {
            get => _isTranscribing;
            set => SetProperty(ref _isTranscribing, value);
        }

        public float AudioLevel
        {
            get => _audioLevel;
            set => SetProperty(ref _audioLevel, value);
        }

        public ICommand StartRecordingCommand { get; }
        public ICommand StopRecordingCommand { get; }
        public ICommand SaveTranscriptCommand { get; }
        public ICommand ClearTranscriptCommand { get; }
        public ICommand RefreshDevicesCommand { get; }

        public MainViewModel()
        {
            // Initialize services
            _transcriptionService = new TranscriptionService(string.Empty);
            _audioCaptureService = new AudioCaptureService(_transcriptionService);

            // Set up commands
            StartRecordingCommand = new RelayCommand(
                param => StartRecording(),
                param => !IsRecording && !string.IsNullOrEmpty(ApiKey));

            StopRecordingCommand = new RelayCommand(
                param => StopRecording(),
                param => IsRecording);

            SaveTranscriptCommand = new RelayCommand(
                param => SaveTranscript(),
                param => !string.IsNullOrEmpty(TranscriptionText));

            ClearTranscriptCommand = new RelayCommand(
                param => ClearTranscript(),
                param => !string.IsNullOrEmpty(TranscriptionText));

            RefreshDevicesCommand = new RelayCommand(
                param => RefreshDevices());

            // Set up event handlers
            _audioCaptureService.AudioLevelChanged += (sender, level) =>
            {
                Application.Current.Dispatcher.Invoke(() => AudioLevel = level);
            };

            _audioCaptureService.TranscriptionReceived += (sender, text) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TranscriptionText += $"{text}\n\n";
                    IsTranscribing = _audioCaptureService.IsRecording;
                });
            };

            _audioCaptureService.StatusChanged += (sender, status) =>
            {
                Application.Current.Dispatcher.Invoke(() => StatusText = status);
            };

            _audioCaptureService.ErrorOccurred += (sender, ex) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusText = $"Error: {ex.Message}";
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            };

            // Initialize audio devices
            RefreshDevices();

            // Try to load API key from settings if available
            try
            {
                ApiKey = Properties.Settings.Default.ApiKey ?? string.Empty;
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

            TranscriptionText = instructions;
        }

        private void StartRecording()
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                MessageBox.Show("Please enter your OpenAI API key first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _audioCaptureService.StartRecording();
            IsRecording = true;
            IsTranscribing = true;
        }

        private void StopRecording()
        {
            _audioCaptureService.StopRecording();
            IsRecording = false;
        }

        private void SaveTranscript()
        {
            if (!string.IsNullOrEmpty(TranscriptionText))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = "txt",
                    Title = "Save Transcription"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveFileDialog.FileName, TranscriptionText);
                    StatusText = "Transcription saved.";
                }
            }
            else
            {
                MessageBox.Show("There is no transcription to save.", "No Content", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearTranscript()
        {
            if (!string.IsNullOrEmpty(TranscriptionText))
            {
                if (MessageBox.Show("Are you sure you want to clear the transcription?",
                                    "Clear Transcription",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    TranscriptionText = string.Empty;
                    StatusText = "Transcription cleared.";
                }
            }
            else
            {
                StatusText = "Transcription is already empty.";
            }
        }

        private void RefreshDevices()
        {
            var devices = _audioCaptureService.GetAudioDevices();
            AudioDevices = new ObservableCollection<AudioDeviceModel>(devices);
            
            if (AudioDevices.Count > 0)
            {
                SelectedAudioDevice = AudioDevices.FirstOrDefault();
            }
            
            StatusText = "Audio devices refreshed.";
        }

        public void Cleanup()
        {
            _audioCaptureService?.Dispose();
            _transcriptionService?.Dispose();
        }
    }
}
