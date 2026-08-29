# 🎙️ Just Speech to Text (Just STT) for Windows

<div align="center">
  <img src="icon.ico" width="96" height="96" alt="Just STT Icon" />
  <h3>Lightning-Fast, Privacy-First Intelligent Voice Dictation for Windows</h3>
  <p>Hold a key or mouse button. Speak naturally. High-accuracy transcribed text is typed right where your cursor is.</p>

  <p>
    <a href="https://github.com/justprox/just-speech-to-text-win/releases/latest"><img src="https://img.shields.io/github/v/release/justprox/just-speech-to-text-win?label=Latest%20Release&color=7C3AED" alt="Latest Release" /></a>
    <a href="https://github.com/justprox/just-speech-to-text-win/releases/latest"><img src="https://img.shields.io/badge/Download-JsttWin.exe%20(x64)-10B981?style=flat&logo=windows" alt="Download Executable" /></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache%202.0-blue.svg" alt="License" /></a>
  </p>
</div>

---

## ⚡ Quick Download

Download the standalone Windows executable directly from the **[Latest Release](https://github.com/justprox/just-speech-to-text-win/releases/latest)**:
- **[`JsttWin.exe`](https://github.com/justprox/just-speech-to-text-win/releases/latest/download/JsttWin.exe)** — Standalone single-file Windows executable (no installer required, runs on Windows 10 & 11).

---

## 💡 Inspiration & Acknowledgments

**Just Speech to Text** is an independent, native Windows implementation inspired by [**Jot for macOS** (`google-gemini/jot-gemini-transcribe-macOS`)](https://github.com/google-gemini/jot-gemini-transcribe-macOS) by Google, licensed under the Apache 2.0 License.

While the macOS original was crafted in Swift for Apple Silicon, **Just STT** is built from the ground up natively in **C# / .NET 10 (LTS) / WPF / Win32** to deliver the same lightning-fast push-to-talk experience on Windows 10 & Windows 11.

---

## ✨ Features

- **Push-to-Talk (Hold to Dictate)**: Hold **Right Ctrl** or side mouse button (**Mouse 5**), speak your thought, and release. Clean, formatted text is instantly inserted into any active app (VS Code, browser, Telegram, Word, Terminal, etc.).
- **Post-Speech Tail Padding**: 300ms intelligent trailing grace period on key release ensures your last syllables and words are never truncated.
- **Official Gemini Models**:
  - `gemini-3.5-transcribe-live`: Real-time streaming transcription over WebSocket with a sleek floating live speech bubble.
  - `gemini-3.5-transcribe`: High-accuracy batch transcription with smart punctuation and filler-word removal via Google Interactions API.
- **Custom Triggers (Keyboard + Mouse)**:
  - Supports `Right Ctrl`, `Mouse 4`, `Mouse 5`, `Middle Mouse`, `Caps Lock`, `F8`, and any custom keys.
  - Bind multiple active triggers simultaneously.
- **Hands-Free Mode**: Double-tap your trigger or press `Trigger + Space` to record hands-free.
- **Instant Cancel**: Press `Esc` at any time to cancel recording with zero text insertion.
- **Translucent Minimalist HUD**:
  - Compact bottom voice pill (waveform + recording timer).
  - Floating translucent frosted glass bubble for live text feedback.
  - Never steals active window focus (`WS_EX_NOACTIVATE`).
  - Startup welcome badge with one-click dismiss.
- **Full Theme Support (System / Dark / Light)**:
  - Clean **Linear / Apple Pro** Light Theme & Deep Obsidian Dark Theme.
  - Dynamic Windows DWM immersive titlebar and tray theme synchronization.
- **Privacy & Security Guard**:
  - **Zero-Disk In-Memory Storage (RAM-Only)**: Audio and history live strictly in volatile memory. 0 bytes are saved to disk, and buffers are wiped immediately upon closing.
  - **Password Protection**: Automatically suppresses dictation whenever your cursor is inside a password field.
  - **Zero Telemetry**: Direct client-to-API communication with no intermediary servers.
  - **Windows DPAPI**: API keys are encrypted locally using Windows DPAPI bound to your user account.
  - **Rolling Local History**: Quick-copy tray menu and in-memory history management.

---

## 🏗️ Architecture & Technology Stack

| Component | Technology | Purpose |
|---|---|---|
| **Language & Framework** | C# 14 / C# 13, .NET 10 (`net10.0-windows`) | Core engine and UI |
| **UI Framework** | WPF (Windows Presentation Foundation) | Modern translucent HUD, live speech bubble, and Settings dashboard |
| **Audio Capture** | NAudio (`WasapiCapture` / `WaveInEvent`) | 16kHz mono 16-bit PCM in-memory recording & live streaming |
| **Input Hooking** | Low-Level Windows API (`SetWindowsHookEx`) | Low-latency global keyboard (`WH_KEYBOARD_LL`) and mouse (`WH_MOUSE_LL`) hooks |
| **Live Speech (Streaming)**| WebSocket (`ClientWebSocket` / WSS) | Low-latency bidirectional real-time transcription via `gemini-3.5-transcribe-live` |
| **Batch Transcription** | HttpClient (REST API) | High-accuracy post-recording transcription via `gemini-3.5-transcribe` |
| **Security & Privacy** | Windows DPAPI (`ProtectedData`) + UI Automation + Zero-Disk RAM | Local API key encryption, password field suppression, and RAM-only zero-footprint storage |
| **Native Interop** | Win32 DWM, Shell32, User32 | Immersive dark/light titlebars, non-stealing window overlays (`WS_EX_NOACTIVATE`), tray icon message pumps |

---

## 🚀 Building from Source

### Prerequisites:
- Windows 10 / Windows 11 (64-bit).
- **.NET 10 SDK** (`winget install Microsoft.DotNet.SDK.10` or download from [dot.net](https://dotnet.microsoft.com/download/dotnet/10.0)).

### Build & Run:
```powershell
# Clone the repository
git clone https://github.com/justprox/just-speech-to-text-win.git
cd just-speech-to-text-win

# Run in development mode
dotnet run

# Publish a single-file compact binary to publish/JsttWin.exe
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

---

## ⚙️ Initial Setup

1. On first launch, open the **Settings** dashboard.
2. Enter your API key (available free at [Google AI Studio](https://aistudio.google.com/apikey)).
3. Test connection, select your preferred trigger keys in **Dictation Triggers**, and click **Save & Apply Changes**.
4. Hold your trigger key anywhere in Windows and start speaking!

---

## 📄 License

This project is licensed under the [Apache License 2.0](LICENSE).
