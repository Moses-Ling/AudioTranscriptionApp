# Product Requirements Document
# Audio Transcription Application - Enhancement Plan

## Version 1.1
## Date: April 5, 2025

---

## 1. Introduction

### 1.1 Purpose
This document outlines the current state of the Audio Transcription Application and proposes a plan for enhancements to improve its configuration, file management, error handling, security, and overall robustness.

### 1.2 Scope
This document covers:
- An evaluation of the application's current features and architecture.
- Proposed enhancements including configuration management, automated file handling, improved error handling, logging, security hardening, and testing.
- A phased implementation plan for these enhancements.

### 1.3 Current Application Location
- `D:\VS\source\repos\AudioTranscriptionApp`

---

## 2. Current State Evaluation

### 2.1 Overview
The application is a WPF desktop tool built with .NET Framework 4.8. It records system audio using NAudio, transcribes it in chunks using the OpenAI Whisper API, and displays the text. The main UI uses a code-behind approach, while core logic resides in `AudioCaptureService` and `TranscriptionService`.

### 2.2 Key Features (Current)
- System audio recording via NAudio (`WasapiLoopbackCapture`).
- Audio device selection.
- Transcription using OpenAI Whisper (`whisper-1` model) in 10-second chunks.
- Progressive display of transcription text.
- Visual audio level indicator (bar and percentage).
- Basic UI controls (Start/Stop, Save, Clear, Refresh Devices, API Key input).
- OpenAI API key persistence using `Properties.Settings`.
- Recent UI/Build fixes committed to GitHub.

### 2.3 Strengths
- Core recording and transcription functionality is operational.
- Utilizes the capable NAudio library.
- Provides useful real-time audio level feedback.
- Service-based separation for audio capture and API interaction.
- Basic API key persistence.

### 2.4 Weaknesses / Areas for Improvement
- **Configuration:** Very limited; only the API key is configurable. Key parameters (e.g., 10s chunk duration) are hardcoded. Lacks a dedicated settings UI.
- **File Management:** Transcription saving is manual. No automatic saving or organized output structure (e.g., timestamped folders).
- **Error Handling:** Basic `try-catch` blocks exist but could be more specific and resilient (e.g., handling API rate limits, network issues, file access errors).
- **Logging:** No dedicated logging mechanism for diagnostics.
- **Testing:** Lacks automated tests (unit or integration).
- **Security:** API key stored insecurely (plain text) in standard application settings.
- **User Experience (UX):** Functional but basic. Could benefit from clearer status/progress indicators.
- **Code Structure:** Potential unused MVVM code (`ViewModels`, `Converters` folders) from previous architecture needs review/cleanup.
- **Extensibility:** Tightly coupled to OpenAI Whisper API.

---

## 3. Proposed Enhancements (Requirements)

### 3.1 Configuration Management
- **REQ-CONF-01:** Create a dedicated Settings window/UI.
- **REQ-CONF-02:** Allow configuration of OpenAI API Key (masked input).
- **REQ-CONF-03:** Allow configuration of Recording Chunk Duration (slider/numeric, 5-60 seconds).
- **REQ-CONF-04:** Allow configuration of a Default Save Directory (text input with browse button).
- **REQ-CONF-05:** Persist settings using `Properties.Settings` or a custom configuration mechanism.
- **REQ-CONF-06:** **Encrypt the stored OpenAI API Key** using `System.Security.Cryptography.ProtectedData`.
- **REQ-CONF-07:** Load settings on application start and apply them (chunk duration, API key).
- **REQ-CONF-08:** Add a "Settings" button to the main window to access the configuration UI.

### 3.2 File Management & Logging
- **REQ-FILE-01:** Automatically create a timestamped directory (e.g., `yyyyMMdd_HHmmss`) within the configured Default Save Directory when recording stops and transcription is available.
- **REQ-FILE-02:** Automatically save the full transcription text to a file (e.g., `transcription.txt`) within the timestamped directory.
- **REQ-FILE-03:** Modify or remove the manual "Save Transcript" button in favor of automatic saving. Consider changing it to "Open Save Folder".
- **REQ-LOG-01:** Implement a simple `Logger` class.
- **REQ-LOG-02:** Log key events (app start/stop, recording start/stop, transcription requests/results, errors, settings changes) with timestamps to a file (e.g., `app.log`) in a designated location (e.g., AppData or the Default Save Directory).

### 3.3 Error Handling & UX
- **REQ-ERR-01:** Enhance `TranscriptionService` to catch specific `HttpRequestException`s and handle common HTTP status codes (401, 429, 5xx).
- **REQ-ERR-02:** Implement retry logic with exponential backoff for API rate limit errors (HTTP 429).
- **REQ-UX-01:** Provide more granular status updates in the main window's status bar (e.g., "Transcribing chunk...", "Saving file...").
- **REQ-UX-02:** Consider adding a subtle progress indicator during transcription API calls.

### 3.4 Code Quality & Structure
- **REQ-CODE-01:** Review and remove any unused code artifacts from the previous MVVM architecture (ViewModels, Converters) if the code-behind approach is final.
- **REQ-CODE-02 (Optional):** Refactor services (`AudioCaptureService`, `TranscriptionService`) to implement interfaces (`IAudioCaptureService`, `ITranscriptionService`) for improved testability.

### 3.5 Testing
- **REQ-TEST-01:** Set up a Unit Test project within the solution.
- **REQ-TEST-02:** Write unit tests for core logic in services, mocking external dependencies (HttpClient, NAudio components where feasible).
- **REQ-TEST-03:** Write unit tests for settings loading, saving, and encryption/decryption logic.

---

## 4. Non-Functional Requirements

- **Performance:** UI should remain responsive. API calls should provide feedback if long-running.
- **Security:** API keys must be encrypted at rest.
- **Usability:** Settings should be clear. Error messages should be informative.
- **Reliability:** Application should handle common errors gracefully. Logging should aid troubleshooting.

---

## 5. Implementation Plan (Phased Approach)

### 5.1 Phase 1: Code Cleanup & Configuration Backend
1.  Review/remove unused MVVM code.
2.  Implement settings persistence class/logic, including API key encryption (`ProtectedData`).
3.  Modify services/main window to *read* settings (API key, chunk duration - use defaults initially).

### 5.2 Phase 2: Configuration UI & Integration
1.  Build the Settings window UI (`SettingsWindow.xaml`).
2.  Implement logic to load/save settings from the UI, including directory browsing.
3.  Add "Settings" button to `MainWindow` and wire it up.
4.  Ensure configured settings are correctly applied at runtime.

### 5.3 Phase 3: File Management & Logging
1.  Implement the `Logger` class and add logging calls throughout the application.
2.  Implement automatic timestamped directory creation.
3.  Implement automatic saving of the final transcript.
4.  Update the main UI regarding the save button.

### 5.4 Phase 4: Error Handling & UX Improvements
1.  Refactor `TranscriptionService` error handling (specific exceptions, status codes, retry logic).
2.  Enhance status bar messages for better user feedback.
3.  (Optional) Add a subtle progress indicator.

### 5.5 Phase 5: Testing
1.  Set up the Unit Test project.
2.  Write unit tests covering critical logic (services, settings).

---

## 6. Technical Details

- **Platform:** .NET Framework 4.8
- **UI Framework:** WPF
- **Key Libraries:** NAudio, Newtonsoft.Json
- **Target OS:** Windows 10+

---
