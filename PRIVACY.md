# Privacy Policy & Security Architecture for Just Speech to Text (Just STT)

---

## 🛡️ The Promise

Your voice goes from your Windows PC **directly to Google's Gemini API**, using your own API key. 
There is **no middleman server, no proxy, no third-party account, no analytics, and zero telemetry**.
Everything else stays strictly on your PC. The code is 100% open-source for independent audit.

---

## 📡 What Leaves Your Machine (The Complete List)

1. **The audio of each dictation**: Sent directly over HTTPS / WebSocket (WSS) to `generativelanguage.googleapis.com` (the only network endpoint the application connects to).
2. **Your custom vocabulary terms**: Sent with the prompt to bias recognition towards your custom names and technical terms.
3. **The formatting prompt & tone preset**: Dictation instructions for punctuation and formatting. Never surrounding text, never screen contents, never window titles.
4. **Your Gemini API Key**: Sent directly to Google in the authorization request. Stored locally encrypted via **Windows Data Protection API (DPAPI)**.

---

## 🔒 What Never Leaves Your PC & Privacy Protections

- **Zero-Disk In-Memory Storage (RAM-Only)**: By default, voice recordings and transcription history live strictly in volatile system memory (RAM). **0 bytes are saved to disk**, and all audio is purged immediately from memory upon closing the application.
- **No Keylogging or Screen Capture**: No screenshots, no background typing logging, no window scraping.
- **Password Protection Guard**: Dictation is automatically blocked whenever the active focus is on a password input field (`UIAutomation.IsPassword` and Win32 `EM_GETPASSWORDCHAR`).
- **Encrypted Local Storage**: Your API Key is encrypted using Windows DPAPI (`CurrentUser` scope) and cannot be decrypted by other users or other computers.
- **Instant Data Purge**: At any time, you can click "Clear All History Now" to wipe all in-memory buffers and local files.
- **Zero Analytics**: No telemetry, no error reporting servers, no tracking identifiers, and no background pings.
