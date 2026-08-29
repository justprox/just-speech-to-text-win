using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using JustSTT.Native;

namespace JustSTT.Views
{
    public partial class OverlayPillWindow : Window
    {
        private readonly DispatcherTimer _recordDurationTimer;
        private DateTime _recordStartTime;

        public OverlayPillWindow()
        {
            InitializeComponent();

            _recordDurationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _recordDurationTimer.Tick += OnDurationTimerTick;

            Loaded += OnWindowLoaded;
            SizeChanged += (s, e) => RepositionWindow();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            // Apply WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST so it never steals focus
            IntPtr exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
            long newExStyle = exStyle.ToInt64() | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(newExStyle));
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            RepositionWindow();
        }

        public void RepositionWindow()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double workAreaBottom = SystemParameters.WorkArea.Bottom;

            double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
            double currentHeight = ActualHeight > 0 ? ActualHeight : Height;

            Left = (screenWidth - currentWidth) / 2.0;
            Top = workAreaBottom - currentHeight - 24;
        }

        public void ShowListening(bool isHandsFree = false)
        {
            Dispatcher.Invoke(() =>
            {
                RepositionWindow();

                RecordingTimerText.Text = "0:00";
                _recordStartTime = DateTime.Now;
                _recordDurationTimer.Start();

                RecordingStateGrid.Visibility = Visibility.Visible;
                TranscribingStateGrid.Visibility = Visibility.Collapsed;
                SuccessStateGrid.Visibility = Visibility.Collapsed;
                ErrorStateGrid.Visibility = Visibility.Collapsed;
                WelcomeStateGrid.Visibility = Visibility.Collapsed;

                Show();
            });
        }

        public void UpdateAudioLevel(float level)
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                WaveVisualizer.SetLevel(level);
            }));
        }

        public void ShowTranscribing()
        {
            Dispatcher.Invoke(() =>
            {
                _recordDurationTimer.Stop();

                RecordingStateGrid.Visibility = Visibility.Collapsed;
                TranscribingStateGrid.Visibility = Visibility.Visible;
                SuccessStateGrid.Visibility = Visibility.Collapsed;
                ErrorStateGrid.Visibility = Visibility.Collapsed;
                WelcomeStateGrid.Visibility = Visibility.Collapsed;

                Show();
            });
        }

        public void ShowStartupWelcome()
        {
            Dispatcher.Invoke(async () =>
            {
                _recordDurationTimer.Stop();

                RecordingStateGrid.Visibility = Visibility.Collapsed;
                TranscribingStateGrid.Visibility = Visibility.Collapsed;
                SuccessStateGrid.Visibility = Visibility.Collapsed;
                ErrorStateGrid.Visibility = Visibility.Collapsed;
                WelcomeStateGrid.Visibility = Visibility.Visible;

                RepositionWindow();
                Show();

                await Task.Delay(4000);

                if (WelcomeStateGrid.Visibility == Visibility.Visible)
                {
                    Hide();
                }
            });
        }

        public void ShowSuccess(string text)
        {
            Dispatcher.Invoke(async () =>
            {
                _recordDurationTimer.Stop();

                SuccessPreviewText.Text = "Inserted";

                RecordingStateGrid.Visibility = Visibility.Collapsed;
                TranscribingStateGrid.Visibility = Visibility.Collapsed;
                SuccessStateGrid.Visibility = Visibility.Visible;
                ErrorStateGrid.Visibility = Visibility.Collapsed;
                WelcomeStateGrid.Visibility = Visibility.Collapsed;

                Show();
                RepositionWindow();

                await Task.Delay(1000);

                // Only hide if still on success
                if (SuccessStateGrid.Visibility == Visibility.Visible)
                {
                    Hide();
                }
            });
        }

        public void ShowError(string message)
        {
            Dispatcher.Invoke(async () =>
            {
                _recordDurationTimer.Stop();

                string displayMsg = message;
                if (displayMsg.StartsWith("No speech recognized", StringComparison.OrdinalIgnoreCase))
                {
                    displayMsg = "No speech detected";
                }

                ErrorDetailText.Text = displayMsg;

                RecordingStateGrid.Visibility = Visibility.Collapsed;
                TranscribingStateGrid.Visibility = Visibility.Collapsed;
                SuccessStateGrid.Visibility = Visibility.Collapsed;
                ErrorStateGrid.Visibility = Visibility.Visible;
                WelcomeStateGrid.Visibility = Visibility.Collapsed;

                Show();
                RepositionWindow();

                await Task.Delay(1600);

                if (ErrorStateGrid.Visibility == Visibility.Visible)
                {
                    Hide();
                }
            });
        }

        public void HideOverlay()
        {
            Dispatcher.Invoke(() =>
            {
                _recordDurationTimer.Stop();
                Hide();
            });
        }

        private void OnPillMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Instantly hide the welcome badge or status overlay on user click
            HideOverlay();
        }

        private void OnDurationTimerTick(object? sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _recordStartTime;
            RecordingTimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        }
    }
}
