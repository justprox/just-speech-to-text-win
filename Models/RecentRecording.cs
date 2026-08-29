using System;
using System.IO;
using System.Text.Json.Serialization;

namespace JustSTT.Models
{
    public class RecentRecording
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double DurationSeconds { get; set; } = 0;
        public string AudioFilePath { get; set; } = string.Empty;
        public string TranscriptText { get; set; } = string.Empty;
        public string ModelUsed { get; set; } = "gemini-3.5-transcribe";
        public bool IsSuccess { get; set; } = true;
        public string? ErrorMessage { get; set; }

        [JsonIgnore]
        public byte[]? AudioBytes { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss");
        public string FormattedDate => Timestamp.ToString("dd.MM.yyyy");
        public string FormattedDuration => $"{DurationSeconds:F1}s";
        public bool HasAudio => (AudioBytes != null && AudioBytes.Length > 0) || (!string.IsNullOrEmpty(AudioFilePath) && File.Exists(AudioFilePath));
    }
}
