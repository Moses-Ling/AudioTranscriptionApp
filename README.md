# Audio Transcription App

A WPF desktop application that captures audio from Windows devices and transcribes it using OpenAI's Whisper API.

## Features

- **Audio Capture**: Records system audio using WASAPI loopback capture
- **System Audio Capture**: Records audio output from any selected Windows audio device
- **Real-time Transcription**: Processes audio in 10-second chunks for continuous transcription
- **Audio Visualization**: Includes real-time audio level monitoring
- **Flexible Device Selection**: Choose from available audio output devices
- **Transcription Storage**: Save transcriptions to text files

## Requirements

- Windows operating system
- .NET Framework 4.8
- OpenAI API key for Whisper transcription

## Dependencies

- NAudio 2.2.1 for audio processing
- Newtonsoft.Json 13.0.3 for JSON serialization
- Microsoft.Win32.Registry 4.7.0 (used by NAudio)

## Setup

1. Clone this repository
2. Open the solution in Visual Studio
3. Restore NuGet packages
4. Build and run the application
5. Enter your OpenAI API key in the application
6. Select an audio device from the dropdown
7. Click "Start Recording" to begin capturing and transcribing audio

## License

MIT
