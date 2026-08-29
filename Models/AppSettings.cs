using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JustSTT.Models
{
    public class AppSettings
    {
        public string EncryptedApiKey { get; set; } = string.Empty;

        [JsonIgnore]
        public string ApiKey { get; set; } = string.Empty;

        public string ModelName { get; set; } = "gemini-3.5-transcribe";

        public string CustomBaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

        public string CustomProxyUrl { get; set; } = "";

        public string SelectedMicrophoneDeviceName { get; set; } = "Default";

        public string TonePreset { get; set; } = "Natural Clean";

        public string CustomPrompt { get; set; } = "";

        public List<string> CustomVocabulary { get; set; } = new List<string>
        {
            "JustSTT", "Gemini", "GitHub", "API", "Windows", "ChatGPT", "TypeScript", "Python", "Kubernetes"
        };

        public List<TriggerBinding> ActiveTriggers { get; set; } = new List<TriggerBinding>
        {
            TriggerBinding.RightControl,
            TriggerBinding.Mouse5
        };

        public bool HandsFreeEnabled { get; set; } = true;

        public bool SoundFeedbackEnabled { get; set; } = true;

        public bool StartWithWindows { get; set; } = false;

        public string TextInsertionMethod { get; set; } = "Auto"; // "Auto", "Clipboard", "SendInput"

        public int MaxRecentHistoryCount { get; set; } = 3;
        public bool InMemoryHistoryOnly { get; set; } = true; // Store audio & transcripts in RAM only (zero disk footprint)
        public bool SuppressInPasswordFields { get; set; } = true;
        public string ThemeMode { get; set; } = "System"; // "System", "Dark", "Light"
    }
}
