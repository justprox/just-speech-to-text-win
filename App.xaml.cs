using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using JustSTT.Models;
using JustSTT.Services;
using JustSTT.Views;

namespace JustSTT
{
    public partial class App : Application
    {
        private static Mutex? _appMutex;

        private ConfigService _configService = null!;
        private RecentHistoryService _historyService = null!;
        private AudioCaptureService _audioService = null!;
        private GeminiClientService _geminiService = null!;
        private GeminiLiveWebSocketService _liveWsService = null!;
        private TextInsertionService _textInsertionService = null!;
        private InputHookService _hookService = null!;
        private TrayIconManager _trayManager = null!;
        private ThemeService _themeService = null!;

        private OverlayPillWindow _overlayWindow = null!;
        private LiveTranscriptBubbleWindow _bubbleWindow = null!;
        private MainWindow? _mainWindow;

        private bool _isRecording = false;
        private bool _isTranscribing = false;
        private bool _isHandsFreeActive = false;
        private string? _currentRecordingPath;
        private DateTime _recordingStartTime;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Single Instance Application Guard
            _appMutex = new Mutex(true, "JustSpeechToText_SingleInstance_AppMutex", out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    string msg = args.ExceptionObject?.ToString() ?? "Unknown exception";
                    File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JustSpeechToText", "crash_domain.txt"), msg);
                }
                catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JustSpeechToText", "crash_dispatcher.txt"), args.Exception.ToString());
                    args.Handled = true;
                }
                catch { }
            };

            // 1. Initialize Configuration and Core Services
            _configService = new ConfigService();
            _themeService = new ThemeService(_configService);
            _themeService.ApplyTheme();

            _historyService = new RecentHistoryService(_configService);
            _audioService = new AudioCaptureService();
            _geminiService = new GeminiClientService(_configService);
            _liveWsService = new GeminiLiveWebSocketService(_configService);
            _textInsertionService = new TextInsertionService(_configService);

            // Recreate HTTP client whenever settings/proxy changes
            _configService.SettingsChanged += () => _geminiService.RefreshHttpClient();

            // 2. Initialize InputHookService BEFORE MainWindow
            _hookService = new InputHookService(_configService);
            _hookService.TriggerPressed += OnTriggerPressed;
            _hookService.TriggerReleased += OnTriggerReleased;
            _hookService.CancelPressed += OnCancelPressed;
            _hookService.HandsFreeToggleRequested += OnHandsFreeToggleRequested;

            // 3. Create Windows
            _overlayWindow = new OverlayPillWindow();
            _bubbleWindow = new LiveTranscriptBubbleWindow();

            try
            {
                _mainWindow = new MainWindow(
                    _configService,
                    _geminiService,
                    _historyService,
                    _hookService,
                    _audioService,
                    _textInsertionService,
                    _themeService);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JustSpeechToText", "crash_mainwindow_ctor.txt"), ex.ToString());
            }

            // 4. Live streaming callbacks
            _liveWsService.InterimTranscriptReceived += text =>
            {
                if (_isRecording)
                {
                    _bubbleWindow.ShowLiveText(text);
                }
            };

            _audioService.RawAudioChunkAvailable += async chunk =>
            {
                if (_isRecording && _configService.Settings.ModelName == "gemini-3.5-transcribe-live")
                {
                    await _liveWsService.SendAudioChunkAsync(chunk);
                }
            };

            _audioService.AudioLevelUpdated += level =>
            {
                if (_isRecording)
                {
                    _overlayWindow.UpdateAudioLevel(level);
                }
            };

            // 5. Setup Tray Icon
            _trayManager = new TrayIconManager(
                _historyService,
                _themeService,
                OpenMainWindow,
                ShutdownApp);

            // If API key is empty on first start, open Main window
            if (string.IsNullOrEmpty(_configService.Settings.ApiKey))
            {
                OpenMainWindow();
            }
            else
            {
                _overlayWindow.ShowStartupWelcome();
            }
        }

        private void OnTriggerPressed(TriggerBinding trigger)
        {
            if (_isHandsFreeActive)
            {
                _isHandsFreeActive = false;
                StopAndTranscribe();
                return;
            }

            if (!_isRecording && !_isTranscribing)
            {
                StartDictation(isHandsFree: false);
            }
        }

        private void OnTriggerReleased(TriggerBinding trigger)
        {
            if (_isHandsFreeActive)
            {
                return;
            }

            if (_isRecording)
            {
                StopAndTranscribe();
            }
        }

        private void OnHandsFreeToggleRequested()
        {
            if (!_configService.Settings.HandsFreeEnabled) return;

            if (_isRecording && !_isHandsFreeActive)
            {
                _isHandsFreeActive = true;
                _overlayWindow.ShowListening(isHandsFree: true);
            }
            else if (!_isRecording && !_isTranscribing)
            {
                _isHandsFreeActive = true;
                StartDictation(isHandsFree: true);
            }
        }

        private void OnCancelPressed()
        {
            if (_isRecording || _isTranscribing)
            {
                _isRecording = false;
                _isTranscribing = false;
                _isHandsFreeActive = false;
                _liveWsService.CancelSession();
                _audioService.CancelRecording();
                _overlayWindow.HideOverlay();
                _bubbleWindow.HideBubble();
            }
        }

        private void StartDictation(bool isHandsFree)
        {
            if (_isRecording || _isTranscribing) return;

            _bubbleWindow.HideBubble();

            if (_configService.Settings.SuppressInPasswordFields && PrivacyGuardService.IsFocusedElementPasswordField())
            {
                _overlayWindow.ShowError("🔒 Dictation blocked in password field for privacy.");
                return;
            }

            _currentRecordingPath = _historyService.GetNewRecordingAudioPath();
            _recordingStartTime = DateTime.Now;

            string selectedMic = _configService.Settings.SelectedMicrophoneDeviceName;
            bool started = _audioService.StartRecording(_currentRecordingPath, selectedMic);

            if (started)
            {
                _isRecording = true;
                _overlayWindow.ShowListening(isHandsFree);

                if (_configService.Settings.ModelName == "gemini-3.5-transcribe-live")
                {
                    _ = _liveWsService.StartLiveSessionAsync();
                }

                if (_configService.Settings.SoundFeedbackEnabled)
                {
                    try { SystemSounds.Asterisk.Play(); } catch { }
                }
            }
            else
            {
                _overlayWindow.ShowError("Could not access microphone.");
            }
        }

        private async void StopAndTranscribe()
        {
            if (!_isRecording || _isTranscribing) return;
            _isTranscribing = true;

            // Instantly transition HUD to Transcribing state for responsive visual feedback
            _overlayWindow.ShowTranscribing();

            // Post-Speech Grace Period (Tail Padding):
            // Continue recording & streaming for an extra 300ms so trailing syllables
            // and hardware buffer audio are never truncated when the key is released.
            await Task.Delay(300);

            _isRecording = false;
            _bubbleWindow.HideBubble();

            string currentModel = _configService.Settings.ModelName;
            string liveTranscript = "";

            try
            {
                if (currentModel == "gemini-3.5-transcribe-live")
                {
                    liveTranscript = await _liveWsService.StopLiveSessionAndGetTranscriptAsync();
                }

                double duration = await _audioService.StopRecordingAsync();
                byte[]? wavBytes = _audioService.LastRecordedWavBytes;
                string? audioPath = _currentRecordingPath;

                if (duration < 0.3)
                {
                    _audioService.CancelRecording();
                    _overlayWindow.HideOverlay();
                    return;
                }

                if ((wavBytes == null || wavBytes.Length == 0) && (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath)))
                {
                    _overlayWindow.ShowError("Audio recording data missing.");
                    return;
                }

                var recording = new RecentRecording
                {
                    Timestamp = _recordingStartTime,
                    DurationSeconds = duration,
                    AudioFilePath = audioPath ?? string.Empty,
                    AudioBytes = wavBytes,
                    ModelUsed = currentModel,
                    IsSuccess = false
                };

                try
                {
                    string transcript = liveTranscript;

                    if (string.IsNullOrWhiteSpace(transcript))
                    {
                        if (wavBytes != null && wavBytes.Length > 0)
                        {
                            transcript = await _geminiService.TranscribeAudioBytesAsync(wavBytes);
                        }
                        else if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
                        {
                            transcript = await _geminiService.TranscribeAudioAsync(audioPath);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(transcript))
                    {
                        recording.IsSuccess = false;
                        recording.ErrorMessage = "No speech detected";
                        _historyService.AddRecording(recording);
                        _overlayWindow.ShowError("No speech detected");
                        return;
                    }

                    recording.TranscriptText = transcript;
                    recording.IsSuccess = true;
                    _historyService.AddRecording(recording);

                    await _textInsertionService.InsertTextAsync(transcript);

                    if (_configService.Settings.SoundFeedbackEnabled)
                    {
                        try { SystemSounds.Asterisk.Play(); } catch { }
                    }

                    _overlayWindow.ShowSuccess(transcript);
                }
                catch (Exception ex)
                {
                    recording.IsSuccess = false;
                    recording.ErrorMessage = ex.Message;
                    _historyService.AddRecording(recording);

                    string shortError = ex.Message;
                    if (shortError.Length > 50) shortError = shortError.Substring(0, 47) + "...";
                    _overlayWindow.ShowError(shortError);
                }
            }
            finally
            {
                _isTranscribing = false;
            }
        }

        public void OpenMainWindow()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_mainWindow == null)
                    {
                        _mainWindow = new MainWindow(
                            _configService,
                            _geminiService,
                            _historyService,
                            _hookService,
                            _audioService,
                            _textInsertionService,
                            _themeService);
                    }

                    if (!_mainWindow.IsVisible)
                    {
                        _mainWindow.Show();
                    }

                    if (_mainWindow.WindowState == WindowState.Minimized)
                    {
                        _mainWindow.WindowState = WindowState.Normal;
                    }

                    _mainWindow.Activate();
                    _mainWindow.Focus();
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JustSpeechToText", "crash_open_mainwindow.txt"), ex.ToString());
                }
            });
        }

        private void ShutdownApp()
        {
            _trayManager?.Dispose();
            _hookService?.Dispose();
            _audioService?.Dispose();
            _liveWsService?.Dispose();
            _themeService?.Dispose();
            _overlayWindow?.Close();
            _bubbleWindow?.Close();
            _mainWindow?.Close();
            _appMutex?.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            _hookService?.Dispose();
            _audioService?.Dispose();
            _themeService?.Dispose();
            _appMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
