using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JustSTT.Models;
using JustSTT.Native;
using JustSTT.Services;
using NAudio.Wave;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace JustSTT.Views
{
    public partial class MainWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly GeminiClientService _geminiService;
        private readonly RecentHistoryService _historyService;
        private readonly InputHookService _hookService;
        private readonly AudioCaptureService _audioService;
        private readonly TextInsertionService _textInsertionService;
        private readonly ThemeService _themeService;

        private readonly List<TriggerBinding> _tempTriggers = new();
        private bool _isTestingMic = false;

        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioReader;
        private string? _currentlyPlayingPath;
        private Button? _currentPlayButton;
        private bool _isLoadedOnce = false;
        private bool _isInitializing = false;

        public MainWindow(
            ConfigService configService,
            GeminiClientService geminiService,
            RecentHistoryService historyService,
            InputHookService hookService,
            AudioCaptureService audioService,
            TextInsertionService textInsertionService,
            ThemeService themeService)
        {
            InitializeComponent();

            try
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/icon.ico", UriKind.Absolute));
            }
            catch { }

            _configService = configService;
            _geminiService = geminiService;
            _historyService = historyService;
            _hookService = hookService;
            _audioService = audioService;
            _textInsertionService = textInsertionService;
            _themeService = themeService;

            _historyService.HistoryChanged += OnHistoryChanged;
            _themeService.ThemeChanged += OnThemeChanged;

            Loaded += (s, e) =>
            {
                if (!_isLoadedOnce)
                {
                    _isLoadedOnce = true;
                    _themeService.ApplyWindowTheme(this);
                    LoadSettingsToUI();
                    RefreshHistoryList();
                    ThemeModeComboBox.SelectionChanged += OnThemeModeSelectionChanged;
                }
            };

            Closing += (s, e) =>
            {
                e.Cancel = true;
                StopPlayback();
                StopMicTest();
                Hide();
            };
        }

        private void OnHistoryChanged()
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.InvokeAsync(RefreshHistoryList);
        }

        private void OnThemeChanged()
        {
            if (_isInitializing || Dispatcher.HasShutdownStarted) return;
            Dispatcher.InvokeAsync(() =>
            {
                _themeService.ApplyWindowTheme(this);
                RefreshHistoryList();
            });
        }

        private void OnNavTabChanged(object sender, RoutedEventArgs e)
        {
            if (HistorySectionGrid == null) return;

            HistorySectionGrid.Visibility = NavHistoryRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            GeminiSectionGrid.Visibility = NavGeminiRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            TriggersSectionGrid.Visibility = NavTriggersRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AudioSectionGrid.Visibility = NavAudioRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            VocabSectionGrid.Visibility = NavVocabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PrivacySectionGrid.Visibility = NavPrivacyRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            GeneralSectionGrid.Visibility = NavGeneralRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (NavHistoryRadio.IsChecked == true)
            {
                RefreshHistoryList();
            }
        }

        #region History Section

        public void RefreshHistoryList()
        {
            RecordingsStackPanel.Children.Clear();
            var recordings = _historyService.GetRecentRecordings();

            var bgCard = (Brush)FindResource("BgCard");
            var borderDef = (Brush)FindResource("BorderDefault");
            var textSec = (Brush)FindResource("TextSecondary");

            if (recordings.Count == 0)
            {
                var emptyBorder = new Border
                {
                    Background = bgCard,
                    BorderBrush = borderDef,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(36),
                    Margin = new Thickness(0, 8, 0, 8)
                };

                var emptyStack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                emptyStack.Children.Add(new TextBlock
                {
                    Text = "🎙️ No dictations recorded yet",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = textSec,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                });
                emptyStack.Children.Add(new TextBlock
                {
                    Text = "Hold Right Ctrl or Mouse 5 anywhere on your PC, speak, and your words will appear here.",
                    FontSize = 12.5,
                    Foreground = textSec,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });

                emptyBorder.Child = emptyStack;
                RecordingsStackPanel.Children.Add(emptyBorder);
                return;
            }

            for (int i = 0; i < recordings.Count; i++)
            {
                var rec = recordings[i];
                RecordingsStackPanel.Children.Add(CreateRecordingCard(rec, i + 1));
            }
        }

        private Border CreateRecordingCard(RecentRecording rec, int index)
        {
            var bgCard = (Brush)FindResource("BgCard");
            var bgInput = (Brush)FindResource("BgInput");
            var borderDef = (Brush)FindResource("BorderDefault");
            var textPri = (Brush)FindResource("TextPrimary");
            var textSec = (Brush)FindResource("TextSecondary");

            var card = new Border
            {
                Background = bgCard,
                BorderBrush = borderDef,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Background = (Brush)FindResource("CardBadgeBg"),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 10, 0)
            };
            badge.Child = new TextBlock
            {
                Text = $"#{index}",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = (Brush)FindResource("CardBadgeFg")
            };
            headerGrid.Children.Add(badge);
            Grid.SetColumn(badge, 0);

            var metaText = new TextBlock
            {
                Text = $"{rec.FormattedDate} at {rec.FormattedTime}  •  {rec.FormattedDuration}  •  {rec.ModelUsed}",
                FontSize = 12,
                Foreground = textSec,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerGrid.Children.Add(metaText);
            Grid.SetColumn(metaText, 1);

            var statusTag = new TextBlock
            {
                Text = rec.IsSuccess ? "✓ Transcribed" : "⚠️ Error",
                Foreground = rec.IsSuccess ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerGrid.Children.Add(statusTag);
            Grid.SetColumn(statusTag, 2);

            grid.Children.Add(headerGrid);
            Grid.SetRow(headerGrid, 0);

            // Transcript text
            var transcriptBox = new TextBox
            {
                Text = string.IsNullOrEmpty(rec.TranscriptText) ? (rec.ErrorMessage ?? "No speech recognized.") : rec.TranscriptText,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = bgInput,
                Foreground = rec.IsSuccess ? textPri : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                BorderBrush = borderDef,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 10, 0, 12),
                FontSize = 13.5,
                MaxHeight = 130,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            grid.Children.Add(transcriptBox);
            Grid.SetRow(transcriptBox, 1);

            // Actions panel
            var actionsPanel = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            if (rec.HasAudio)
            {
                var playBtn = new Button
                {
                    Content = "▶ Play Voice",
                    Margin = new Thickness(0, 0, 8, 0)
                };
                playBtn.Click += (s, e) => ToggleAudioPlay(rec, playBtn);
                actionsPanel.Children.Add(playBtn);
            }

            var copyBtn = new Button
            {
                Content = "📋 Copy",
                Margin = new Thickness(0, 0, 8, 0)
            };
            copyBtn.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(rec.TranscriptText))
                {
                    Clipboard.SetText(rec.TranscriptText);
                    copyBtn.Content = "✓ Copied!";
                }
            };
            actionsPanel.Children.Add(copyBtn);

            var typeBtn = new Button
            {
                Content = "⌨️ Type at Cursor",
                Margin = new Thickness(0, 0, 8, 0)
            };
            typeBtn.Click += async (s, e) =>
            {
                if (!string.IsNullOrEmpty(rec.TranscriptText))
                {
                    WindowState = WindowState.Minimized;
                    await Task.Delay(150);
                    await _textInsertionService.InsertTextAsync(rec.TranscriptText);
                }
            };
            actionsPanel.Children.Add(typeBtn);

            if (rec.HasAudio)
            {
                var retryBtn = new Button
                {
                    Content = "🔄 Re-transcribe",
                    Background = (Brush)FindResource("PrimaryBtnBg"),
                    BorderBrush = (Brush)FindResource("PrimaryBtnBorder"),
                    Foreground = (Brush)FindResource("PrimaryBtnFg")
                };
                retryBtn.Click += async (s, e) =>
                {
                    retryBtn.Content = "⏳ Processing...";
                    retryBtn.IsEnabled = false;
                    try
                    {
                        string fresh = (rec.AudioBytes != null && rec.AudioBytes.Length > 0)
                            ? await _geminiService.TranscribeAudioBytesAsync(rec.AudioBytes)
                            : await _geminiService.TranscribeAudioAsync(rec.AudioFilePath);

                        rec.TranscriptText = fresh;
                        rec.IsSuccess = true;
                        rec.ErrorMessage = null;
                        _historyService.UpdateRecording(rec);
                        transcriptBox.Text = fresh;
                    }
                    catch (Exception ex)
                    {
                        transcriptBox.Text = $"Error: {ex.Message}";
                    }
                    finally
                    {
                        retryBtn.Content = "🔄 Re-transcribe";
                        retryBtn.IsEnabled = true;
                    }
                };
                actionsPanel.Children.Add(retryBtn);
            }

            grid.Children.Add(actionsPanel);
            Grid.SetRow(actionsPanel, 2);

            card.Child = grid;
            return card;
        }

        private EventHandler<StoppedEventArgs>? _playbackStoppedHandler;
        private WaveStream? _currentAudioStream;

        private void ToggleAudioPlay(RecentRecording rec, Button playButton)
        {
            if (_currentlyPlayingPath == rec.Id && _waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
            {
                StopPlayback();
                return;
            }

            StopPlayback();

            try
            {
                if (rec.AudioBytes != null && rec.AudioBytes.Length > 0)
                {
                    var ms = new MemoryStream(rec.AudioBytes);
                    _currentAudioStream = new WaveFileReader(ms);
                }
                else if (!string.IsNullOrEmpty(rec.AudioFilePath) && File.Exists(rec.AudioFilePath))
                {
                    _currentAudioStream = new AudioFileReader(rec.AudioFilePath);
                }
                else
                {
                    MessageBox.Show("Audio data is no longer in memory.", "Playback Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_currentAudioStream);
                _currentlyPlayingPath = rec.Id;
                _currentPlayButton = playButton;

                _playbackStoppedHandler = (s, e) => Dispatcher.Invoke(StopPlayback);
                _waveOut.PlaybackStopped += _playbackStoppedHandler;

                _waveOut.Play();
                playButton.Content = "⏹ Stop Voice";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not play audio: {ex.Message}", "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopPlayback();
            }
        }

        private void StopPlayback()
        {
            if (_currentPlayButton != null)
            {
                _currentPlayButton.Content = "▶ Play Voice";
                _currentPlayButton = null;
            }

            if (_waveOut != null)
            {
                try
                {
                    if (_playbackStoppedHandler != null)
                    {
                        _waveOut.PlaybackStopped -= _playbackStoppedHandler;
                        _playbackStoppedHandler = null;
                    }
                    _waveOut.Stop();
                    _waveOut.Dispose();
                }
                catch { }
                _waveOut = null;
            }

            if (_currentAudioStream != null)
            {
                try { _currentAudioStream.Dispose(); } catch { }
                _currentAudioStream = null;
            }

            if (_audioReader != null)
            {
                try { _audioReader.Dispose(); } catch { }
                _audioReader = null;
            }

            _currentlyPlayingPath = null;
        }

        private void OnRefreshHistoryClicked(object sender, RoutedEventArgs e)
        {
            RefreshHistoryList();
        }

        #endregion

        #region Settings & Configuration

        private void LoadSettingsToUI()
        {
            _isInitializing = true;
            try
            {
                var settings = _configService.Settings;

                ApiKeyBox.Password = settings.ApiKey;

                bool modelMatched = false;
                foreach (ComboBoxItem item in ModelComboBox.Items)
                {
                    if (item.Content.ToString() == settings.ModelName)
                    {
                        ModelComboBox.SelectedItem = item;
                        modelMatched = true;
                        break;
                    }
                }
                if (!modelMatched && ModelComboBox.Items.Count > 0)
                {
                    ModelComboBox.SelectedIndex = 0;
                }

                _tempTriggers.Clear();
                if (settings.ActiveTriggers != null)
                {
                    _tempTriggers.AddRange(settings.ActiveTriggers);
                }
                RefreshTriggersList();

                HandsFreeCheckBox.IsChecked = settings.HandsFreeEnabled;
                SoundFeedbackCheckBox.IsChecked = settings.SoundFeedbackEnabled;

                MicrophoneComboBox.Items.Clear();
                var mics = _audioService.GetAvailableInputDevices();
                foreach (var mic in mics)
                {
                    MicrophoneComboBox.Items.Add(mic);
                }
                MicrophoneComboBox.SelectedItem = mics.Contains(settings.SelectedMicrophoneDeviceName)
                    ? settings.SelectedMicrophoneDeviceName
                    : "Default";

                foreach (ComboBoxItem item in TonePresetComboBox.Items)
                {
                    if (item.Content.ToString() == settings.TonePreset)
                    {
                        TonePresetComboBox.SelectedItem = item;
                        break;
                    }
                }

                if (settings.CustomVocabulary != null)
                {
                    CustomVocabularyTextBox.Text = string.Join(", ", settings.CustomVocabulary);
                }

                CustomPromptTextBox.Text = settings.CustomPrompt ?? "";

                foreach (ComboBoxItem item in InsertionMethodComboBox.Items)
                {
                    if (item.Content.ToString()?.StartsWith(settings.TextInsertionMethod) == true)
                    {
                        InsertionMethodComboBox.SelectedItem = item;
                        break;
                    }
                }

                StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
                SuppressPasswordCheckBox.IsChecked = settings.SuppressInPasswordFields;
                InMemoryOnlyCheckBox.IsChecked = settings.InMemoryHistoryOnly;

                // History retention limit
                int limit = settings.MaxRecentHistoryCount;
                foreach (ComboBoxItem item in HistoryLimitComboBox.Items)
                {
                    if (item.Content.ToString()?.StartsWith(limit.ToString()) == true)
                    {
                        HistoryLimitComboBox.SelectedItem = item;
                        break;
                    }
                }

                // Theme Mode
                foreach (ComboBoxItem item in ThemeModeComboBox.Items)
                {
                    if (item.Content.ToString()?.StartsWith(settings.ThemeMode) == true)
                    {
                        ThemeModeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void OnThemeModeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _configService == null || _themeService == null) return;

            if (ThemeModeComboBox?.SelectedItem is ComboBoxItem selected)
            {
                string text = selected.Content.ToString() ?? "System";
                if (text.StartsWith("Dark")) _configService.Settings.ThemeMode = "Dark";
                else if (text.StartsWith("Light")) _configService.Settings.ThemeMode = "Light";
                else _configService.Settings.ThemeMode = "System";

                _themeService.ApplyTheme();
            }
        }

        private void OnClearAllHistoryClicked(object sender, RoutedEventArgs e)
        {
            _historyService.ClearAllHistory();
            RefreshHistoryList();

            SaveStatusNoticeText.Text = "All local history and recordings have been purged.";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (ts, te) =>
            {
                timer.Stop();
                SaveStatusNoticeText.Text = "";
            };
            timer.Start();
        }

        private void RefreshTriggersList()
        {
            TriggersListBox.Items.Clear();
            foreach (var trigger in _tempTriggers)
            {
                string icon = trigger.Type == TriggerType.MouseButton ? "🖱️" : "⌨️";
                TriggersListBox.Items.Add($"{icon} {trigger.DisplayName}");
            }
        }

        private async void OnTestKeyClicked(object sender, RoutedEventArgs e)
        {
            string key = ApiKeyBox.Password.Trim();
            if (string.IsNullOrEmpty(key))
            {
                KeyTestStatusText.Text = "⚠️ Please enter an API key first.";
                KeyTestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                return;
            }

            TestKeyButton.IsEnabled = false;
            TestKeyButton.Content = "⏳ Testing...";
            KeyTestStatusText.Text = "Connecting to Google Gemini API...";
            KeyTestStatusText.Foreground = (Brush)FindResource("TextSecondary");

            string model = (ModelComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "gemini-3.5-transcribe";
            var result = await _geminiService.TestApiKeyAsync(key, model);

            if (result.Success)
            {
                KeyTestStatusText.Text = result.Message;
                KeyTestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
            else
            {
                string fullMsg = string.IsNullOrEmpty(result.Detail) ? result.Message : $"{result.Message}\n{result.Detail}";
                KeyTestStatusText.Text = fullMsg;
                KeyTestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }

            TestKeyButton.IsEnabled = true;
            TestKeyButton.Content = "⚡ Test Connection";
        }

        private void OnRecordTriggerClicked(object sender, RoutedEventArgs e)
        {
            TriggerCaptureStatusText.Text = "👉 PRESS ANY KEY ON KEYBOARD OR BUTTON ON MOUSE (e.g. Mouse 4/5, Right Ctrl, F8)...";
            RecordTriggerButton.IsEnabled = false;

            _hookService.StartCapturingTrigger(newTrigger =>
            {
                Dispatcher.Invoke(() =>
                {
                    RecordTriggerButton.IsEnabled = true;
                    TriggerCaptureStatusText.Text = "";

                    if (newTrigger != null)
                    {
                        bool exists = _tempTriggers.Any(t =>
                            t.Type == newTrigger.Type &&
                            t.KeyCode == newTrigger.KeyCode &&
                            t.MouseButton == newTrigger.MouseButton);

                        if (!exists)
                        {
                            _tempTriggers.Add(newTrigger);
                            RefreshTriggersList();
                            TriggerCaptureStatusText.Text = $"✓ Added trigger: {newTrigger.DisplayName}";
                            TriggerCaptureStatusText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                        }
                    }
                });
            });
        }

        private void OnRemoveTriggerClicked(object sender, RoutedEventArgs e)
        {
            int index = TriggersListBox.SelectedIndex;
            if (index >= 0 && index < _tempTriggers.Count)
            {
                _tempTriggers.RemoveAt(index);
                RefreshTriggersList();
            }
        }

        private void OnResetDefaultTriggersClicked(object sender, RoutedEventArgs e)
        {
            _tempTriggers.Clear();
            _tempTriggers.Add(TriggerBinding.RightControl);
            _tempTriggers.Add(TriggerBinding.Mouse5);
            RefreshTriggersList();
        }

        private void OnTestMicClicked(object sender, RoutedEventArgs e)
        {
            if (_isTestingMic)
            {
                StopMicTest();
            }
            else
            {
                StartMicTest();
            }
        }

        private void StartMicTest()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "jstt_mictest.wav");
            string selectedDevice = MicrophoneComboBox.SelectedItem?.ToString() ?? "Default";

            _audioService.AudioLevelUpdated += OnMicTestAudioLevel;
            bool ok = _audioService.StartRecording(tempFile, selectedDevice);
            if (ok)
            {
                _isTestingMic = true;
                TestMicButton.Content = "⏹ Stop Test";
            }
        }

        private void StopMicTest()
        {
            if (_isTestingMic)
            {
                _audioService.AudioLevelUpdated -= OnMicTestAudioLevel;
                _audioService.CancelRecording();
                _isTestingMic = false;
                TestMicButton.Content = "🎤 Test Mic";
                MicTestProgressBar.Value = 0;
            }
        }

        private void OnMicTestAudioLevel(float level)
        {
            Dispatcher.Invoke(() =>
            {
                MicTestProgressBar.Value = Math.Min(1.0, level * 3.0);
            });
        }

        private void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            StopMicTest();

            var settings = _configService.Settings;

            settings.ApiKey = ApiKeyBox.Password.Trim();
            settings.ModelName = (ModelComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "gemini-3.5-transcribe";

            settings.ActiveTriggers = new List<TriggerBinding>(_tempTriggers);
            if (settings.ActiveTriggers.Count == 0)
            {
                settings.ActiveTriggers.Add(TriggerBinding.RightControl);
            }

            settings.HandsFreeEnabled = HandsFreeCheckBox.IsChecked == true;
            settings.SoundFeedbackEnabled = SoundFeedbackCheckBox.IsChecked == true;

            settings.SelectedMicrophoneDeviceName = MicrophoneComboBox.SelectedItem?.ToString() ?? "Default";
            settings.TonePreset = (TonePresetComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Natural Clean";

            string rawVocab = CustomVocabularyTextBox.Text ?? "";
            settings.CustomVocabulary = rawVocab
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToList();

            settings.CustomPrompt = CustomPromptTextBox.Text?.Trim() ?? "";

            string selectedMethod = (InsertionMethodComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Auto";
            if (selectedMethod.Contains("Clipboard")) settings.TextInsertionMethod = "Clipboard";
            else if (selectedMethod.Contains("SendInput")) settings.TextInsertionMethod = "SendInput";
            else settings.TextInsertionMethod = "Auto";

            settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
            settings.SuppressInPasswordFields = SuppressPasswordCheckBox.IsChecked == true;
            settings.InMemoryHistoryOnly = InMemoryOnlyCheckBox.IsChecked == true;

            string selectedTheme = (ThemeModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "System";
            if (selectedTheme.StartsWith("Dark")) settings.ThemeMode = "Dark";
            else if (selectedTheme.StartsWith("Light")) settings.ThemeMode = "Light";
            else settings.ThemeMode = "System";

            if (HistoryLimitComboBox.SelectedItem is ComboBoxItem limitItem)
            {
                string text = limitItem.Content.ToString() ?? "3";
                string digits = new string(text.TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(digits, out int val) && val > 0)
                {
                    settings.MaxRecentHistoryCount = val;
                }
            }

            _configService.SaveSettings();
            _themeService.ApplyTheme();
            _historyService.PruneToConfiguredLimit();
            RefreshHistoryList();

            SaveButton.Content = "✓ Saved & Applied!";
            SaveStatusNoticeText.Text = "Settings applied successfully.";

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
            timer.Tick += (ts, te) =>
            {
                timer.Stop();
                SaveButton.Content = "💾 Save & Apply Changes";
                SaveStatusNoticeText.Text = "";
            };
            timer.Start();
        }

        #endregion
    }
}
