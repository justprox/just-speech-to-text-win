# AGENTS.md — Developer & AI Assistant Guide

This document provides architectural context, codebase structure, design principles, and operational instructions for AI coding assistants (e.g., Antigravity, Cursor, Claude, Copilot) and human contributors working on **Just Speech to Text (Just STT)**.

---

## 🧭 Project Overview

**Just Speech to Text (Just STT)** is a lightweight, high-performance, privacy-first Windows voice dictation application built natively with **C# (.NET 10 LTS) and WPF**.

- **Goal**: Provide a seamless push-to-talk and hands-free voice dictation experience anywhere on Windows.
- **Inspiration**: Inspired by [Google's Jot for macOS](https://github.com/google-gemini/jot-gemini-transcribe-macOS) and reimagined as a standalone native Windows solution.
- **Binary Output**: `publish\JsttWin.exe` (Single-file self-contained or framework-dependent Windows executable).

---

## 🏗️ Architecture & Technology Stack

| Component | Technology | Purpose |
|---|---|---|
| **Language & Framework** | C# 14 / C# 13, .NET 10 (`net10.0-windows`) | Core engine and UI |
| **UI Framework** | WPF (Windows Presentation Foundation) | Modern translucent HUD, live speech bubble, and Settings dashboard |
| **Audio Capture** | NAudio (`WasapiCapture` / `WaveInEvent`) | 16kHz mono 16-bit PCM in-memory RAM recording & live streaming |
| **Input Hooking** | Low-Level Windows API (`SetWindowsHookEx`) | Low-latency global keyboard (`WH_KEYBOARD_LL`) and mouse (`WH_MOUSE_LL`) hooks |
| **Live Speech (Streaming)**| WebSocket (`ClientWebSocket` / WSS) | Low-latency bidirectional real-time transcription via `gemini-3.5-transcribe-live` |
| **Batch Transcription** | HttpClient (REST API) | High-accuracy post-recording transcription via `gemini-3.5-transcribe` |
| **Security & Privacy** | Windows DPAPI (`ProtectedData`) + UI Automation + Zero-Disk RAM Storage | Local API key encryption, password field suppression, and RAM-only zero-footprint history |
| **Native Interop** | Win32 DWM, Shell32, User32 | Immersive dark/light titlebars, non-stealing window overlays (`WS_EX_NOACTIVATE`), tray icon message pumps |

---

## 📂 Codebase Directory Map

```
c:\projects\just-talk-win/
├── App.xaml / App.xaml.cs                # Application lifecycle, service wiring, hotkey events
├── JustSTT.csproj                        # Project build definition (.NET 10 WinExe, AssemblyName: JsttWin)
├── icon.ico                              # Multi-resolution studio mic & squircle app icon
├── app.manifest                          # Per-Monitor V2 DPI awareness and Windows 10/11 compatibility
├── LICENSE                               # Apache License 2.0
├── README.md                             # User-facing documentation and quick start
├── PRIVACY.md                            # Privacy guarantees and security commitments
├── Controls/
│   └── WaveformVisualizer.cs             # Real-time multi-bar soundwave visualizer with dynamic BarBrush
├── Models/
│   ├── AppSettings.cs                    # Persistent configuration schema
│   ├── RecentRecording.cs                # Audio history metadata model (in-memory AudioBytes support)
│   └── TriggerBinding.cs                 # Keyboard/mouse trigger definitions & default bindings
├── Native/
│   └── Win32.cs                          # P/Invoke signatures (DWM, hooks, NOTIFYICONDATA, window styles)
├── Services/
│   ├── AudioCaptureService.cs            # Microphone enumeration, WASAPI/WaveIn capture, in-memory WAV stream
│   ├── ConfigService.cs                  # JSON settings persistence with DPAPI encryption & auto-migration
│   ├── GeminiClientService.cs            # Batch REST API transcription with system prompts & vocabulary
│   ├── GeminiLiveWebSocketService.cs     # Real-time streaming WebSocket client with multi-utterance deduplication
│   ├── InputHookService.cs               # Low-level keyboard & mouse hook dispatcher (Push-to-Talk, Hands-free)
│   ├── PrivacyGuardService.cs            # UI Automation cursor password field detection
│   ├── RecentHistoryService.cs           # Rolling history retention (RAM-only zero-disk mode support)
│   ├── TextInsertionService.cs           # Smart text injection (Clipboard Ctrl+V / SendInput Unicode)
│   ├── ThemeService.cs                   # Dynamic System/Dark/Light palette engine & DWM titlebar sync
│   └── TrayIconManager.cs                # Notification area tray icon, Win32 message host, themed ContextMenu
└── Views/
    ├── MainWindow.xaml / .xaml.cs        # Comprehensive Settings & History dashboard
    ├── OverlayPillWindow.xaml / .xaml.cs # Compact bottom voice HUD (32px mini pill)
    └── LiveTranscriptBubbleWindow.xaml   # Floating translucent glass speech bubble
```

---

## 🔑 Key Engineering Guidelines for AI Agents

1. **Window Focus Non-Interference**:
   - `OverlayPillWindow` and `LiveTranscriptBubbleWindow` must **NEVER steal input focus** from the user's active application.
   - Always retain `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST` in `OnSourceInitialized`.
2. **Post-Speech Tail Padding (Grace Period)**:
   - When the user releases the push-to-talk trigger, maintain an extra **~300ms audio recording/streaming grace period** (`await Task.Delay(300)`) before cutting the stream. This prevents truncating the final syllables or trailing words.
3. **In-Memory Zero-Disk Storage (Default Privacy)**:
   - Audio is captured directly to `MemoryStream` and sent to Gemini via `byte[]`. By default (`InMemoryHistoryOnly = true`), 0 bytes are saved to disk, and all audio buffers are freed upon exit.
4. **Theme System & Dynamic Resources**:
   - Never hardcode fixed hex colors in XAML views. Use `{DynamicResource ...}` bindings (`BgWindow`, `TextPrimary`, `OverlayBg`, `OverlayBubbleBg`, etc.).
   - When introducing new UI elements, register their palette brushes in `ThemeService.cs` for both Dark and Light modes.
5. **Live WebSocket Deduplication**:
   - The Gemini Live WebSocket stream (`BidiGenerateContent`) sends chunked interim recognition.
   - Always preserve the turn-based multi-utterance accumulator in `GeminiLiveWebSocketService.cs` so pauses during speech do not delete previous sentences.
6. **Zero-Telemetry & Privacy-by-Default**:
   - Never introduce analytics or telemetry network requests.
   - Never write unencrypted API keys to disk. Always route through `ProtectedData.Protect` (Windows DPAPI).
7. **Clean Single-File Build**:
   - Run compilation via:
     `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish`
   - Output binary is always `publish\JsttWin.exe`.

---

## 🤝 Attribution & Open Source

This project is an independent Windows native implementation inspired by [google-gemini/jot-gemini-transcribe-macOS](https://github.com/google-gemini/jot-gemini-transcribe-macOS) and is licensed under the **Apache License 2.0**.
