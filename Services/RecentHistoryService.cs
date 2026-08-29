using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JustSTT.Models;

namespace JustSTT.Services
{
    public class RecentHistoryService
    {
        private static readonly string RecentFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JustSpeechToText", "recent");

        private readonly ConfigService _configService;
        private readonly List<RecentRecording> _recordings = new();
        private readonly object _lock = new();

        public event Action? HistoryChanged;

        public RecentHistoryService(ConfigService configService)
        {
            _configService = configService;
            MigrateOldHistoryIfNeeded();
            
            if (_configService.Settings.InMemoryHistoryOnly)
            {
                // Purge any lingering disk history for maximum privacy
                PurgeDiskFiles();
            }
            else
            {
                EnsureDirectoryExists();
                LoadHistory();
            }
        }

        private static void MigrateOldHistoryIfNeeded()
        {
            try
            {
                string oldRecent = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JotWin", "recent");
                if (Directory.Exists(oldRecent) && !Directory.Exists(RecentFolder))
                {
                    Directory.CreateDirectory(RecentFolder);
                    foreach (var file in Directory.GetFiles(oldRecent))
                    {
                        string dest = Path.Combine(RecentFolder, Path.GetFileName(file));
                        File.Copy(file, dest, true);
                    }
                }
            }
            catch { }
        }

        public string? GetNewRecordingAudioPath()
        {
            if (_configService.Settings.InMemoryHistoryOnly)
            {
                // Zero-disk mode: No file path created
                return null;
            }

            EnsureDirectoryExists();
            string filename = $"rec_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.wav";
            return Path.Combine(RecentFolder, filename);
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(RecentFolder))
            {
                Directory.CreateDirectory(RecentFolder);
            }
        }

        public IReadOnlyList<RecentRecording> GetRecentRecordings()
        {
            lock (_lock)
            {
                int limit = _configService?.Settings?.MaxRecentHistoryCount ?? 3;
                if (limit <= 0) limit = 3;
                return _recordings.OrderByDescending(r => r.Timestamp).Take(limit).ToList();
            }
        }

        public void ClearAllHistory()
        {
            lock (_lock)
            {
                foreach (var rec in _recordings)
                {
                    rec.AudioBytes = null;
                }
                _recordings.Clear();
                PurgeDiskFiles();
            }

            HistoryChanged?.Invoke();
        }

        private void PurgeDiskFiles()
        {
            try
            {
                if (Directory.Exists(RecentFolder))
                {
                    var files = Directory.GetFiles(RecentFolder);
                    foreach (var f in files)
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }

        public void AddRecording(RecentRecording recording)
        {
            lock (_lock)
            {
                _recordings.Insert(0, recording);

                // Save recording metadata to JSON only if disk storage is enabled
                if (!_configService.Settings.InMemoryHistoryOnly)
                {
                    SaveRecordingMetadata(recording);
                }

                int limit = _configService?.Settings?.MaxRecentHistoryCount ?? 3;
                if (limit <= 0) limit = 3;

                // Keep only configured count
                PruneOldRecordings(limit);
            }

            HistoryChanged?.Invoke();
        }

        public void PruneToConfiguredLimit()
        {
            lock (_lock)
            {
                int limit = _configService?.Settings?.MaxRecentHistoryCount ?? 3;
                if (limit <= 0) limit = 3;
                PruneOldRecordings(limit);

                if (_configService?.Settings?.InMemoryHistoryOnly == true)
                {
                    PurgeDiskFiles();
                }
            }

            HistoryChanged?.Invoke();
        }

        public void UpdateRecording(RecentRecording recording)
        {
            lock (_lock)
            {
                var existing = _recordings.FirstOrDefault(r => r.Id == recording.Id);
                if (existing != null)
                {
                    existing.TranscriptText = recording.TranscriptText;
                    existing.IsSuccess = recording.IsSuccess;
                    existing.ErrorMessage = recording.ErrorMessage;
                    
                    if (!_configService.Settings.InMemoryHistoryOnly)
                    {
                        SaveRecordingMetadata(existing);
                    }
                }
            }

            HistoryChanged?.Invoke();
        }

        private void LoadHistory()
        {
            lock (_lock)
            {
                _recordings.Clear();
                if (!Directory.Exists(RecentFolder)) return;

                var jsonFiles = Directory.GetFiles(RecentFolder, "*.json");
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(jsonFile);
                        var rec = JsonSerializer.Deserialize<RecentRecording>(json);
                        if (rec != null)
                        {
                            _recordings.Add(rec);
                        }
                    }
                    catch { }
                }

                _recordings.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
                int limit = _configService?.Settings?.MaxRecentHistoryCount ?? 3;
                if (limit <= 0) limit = 3;
                PruneOldRecordings(limit);
            }
        }

        private void SaveRecordingMetadata(RecentRecording recording)
        {
            try
            {
                EnsureDirectoryExists();
                string jsonPath = Path.Combine(RecentFolder, $"{recording.Id}.json");
                string json = JsonSerializer.Serialize(recording, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(jsonPath, json);
            }
            catch { }
        }

        private void PruneOldRecordings(int keepCount)
        {
            if (_recordings.Count <= keepCount) return;

            var toRemove = _recordings.Skip(keepCount).ToList();
            foreach (var rec in toRemove)
            {
                rec.AudioBytes = null;
                _recordings.Remove(rec);

                try
                {
                    if (!string.IsNullOrEmpty(rec.AudioFilePath) && File.Exists(rec.AudioFilePath))
                    {
                        File.Delete(rec.AudioFilePath);
                    }
                }
                catch { }

                try
                {
                    string jsonPath = Path.Combine(RecentFolder, $"{rec.Id}.json");
                    if (File.Exists(jsonPath))
                    {
                        File.Delete(jsonPath);
                    }
                }
                catch { }
            }
        }
    }
}
