using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JustSTT.Models;
using Microsoft.Win32;

namespace JustSTT.Services
{
    public class ConfigService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JustSpeechToText");

        private static readonly string LegacyAppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JotWin");

        private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "config.json");
        private static readonly string BackupFilePath = Path.Combine(AppDataFolder, "config.json.bak");
        private static readonly string TempFilePath = Path.Combine(AppDataFolder, "config.json.tmp");

        private readonly object _lock = new();

        public AppSettings Settings { get; private set; } = new();

        public event Action? SettingsChanged;

        public ConfigService()
        {
            MigrateLegacyAppDataIfNeeded();
            LoadSettings();
        }

        private void MigrateLegacyAppDataIfNeeded()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder) && Directory.Exists(LegacyAppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                    string oldConfig = Path.Combine(LegacyAppDataFolder, "config.json");
                    if (File.Exists(oldConfig))
                    {
                        File.Copy(oldConfig, ConfigFilePath, overwrite: true);
                    }
                }
            }
            catch { }
        }

        public void LoadSettings()
        {
            lock (_lock)
            {
                if (TryLoadConfigFile(ConfigFilePath))
                {
                    return;
                }

                // If primary file corrupted, try backup
                if (TryLoadConfigFile(BackupFilePath))
                {
                    SaveSettings(); // Restore primary from backup
                    return;
                }

                // Default fallback configuration
                Settings = new AppSettings();
                SaveSettings();
            }
        }

        private bool TryLoadConfigFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;

                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded == null) return false;

                Settings = loaded;

                // Decrypt API key with DPAPI
                if (!string.IsNullOrEmpty(Settings.EncryptedApiKey))
                {
                    try
                    {
                        byte[] encryptedBytes = Convert.FromBase64String(Settings.EncryptedApiKey);
                        byte[] secretBytes = ProtectedData.Unprotect(
                            encryptedBytes, null, DataProtectionScope.CurrentUser);
                        Settings.ApiKey = Encoding.UTF8.GetString(secretBytes);
                    }
                    catch
                    {
                        Settings.ApiKey = string.Empty;
                    }
                }

                // Ensure default triggers exist
                if (Settings.ActiveTriggers == null || Settings.ActiveTriggers.Count == 0)
                {
                    Settings.ActiveTriggers = new System.Collections.Generic.List<TriggerBinding>
                    {
                        TriggerBinding.RightControl,
                        TriggerBinding.Mouse5
                    };
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SaveSettings()
        {
            lock (_lock)
            {
                try
                {
                    EnsureDirectoryExists();

                    // Encrypt API key with DPAPI
                    if (!string.IsNullOrEmpty(Settings.ApiKey))
                    {
                        byte[] secretBytes = Encoding.UTF8.GetBytes(Settings.ApiKey);
                        byte[] encryptedBytes = ProtectedData.Protect(
                            secretBytes, null, DataProtectionScope.CurrentUser);
                        Settings.EncryptedApiKey = Convert.ToBase64String(encryptedBytes);
                    }
                    else
                    {
                        Settings.EncryptedApiKey = string.Empty;
                    }

                    string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    // Atomic write with temporary file swap and backup preservation
                    File.WriteAllText(TempFilePath, json);

                    if (File.Exists(ConfigFilePath))
                    {
                        File.Copy(ConfigFilePath, BackupFilePath, overwrite: true);
                    }

                    File.Move(TempFilePath, ConfigFilePath, overwrite: true);

                    UpdateAutoStartRegistry(Settings.StartWithWindows);

                    SettingsChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                }
            }
        }

        private void UpdateAutoStartRegistry(bool enable)
        {
            try
            {
                const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                const string appName = "JustSpeechToText";

                using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
                if (key == null) return;

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(appName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    if (key.GetValue(appName) != null)
                    {
                        key.DeleteValue(appName, false);
                    }
                }
            }
            catch { }
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }
        }
    }
}
