using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using JustSTT.Native;

namespace JustSTT.Views
{
    public partial class OverlayPillWindow : Window
    {
        private readonly DispatcherTimer _recordDurationTimer;
        private DispatcherTimer? _autoHideTimer;
        private DateTime _recordStartTime;

        public OverlayPillWindow()
        {
            InitializeComponent();

            _recordDurationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _recordDurationTimer.Tick += (s, e) =>
            {
                var elapsed = DateTime.Now - _recordStartTime;
                RecordingTimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
            };

            Loaded += (s, e) => RepositionWindow();
            SizeChanged += (s, e) => RepositionWindow();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            long exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            long newStyle = exStyle | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(newStyle));

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
                _autoHideTimer?.Stop();
                _autoHideTimer = null;

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
                _autoHideTimer?.Stop();
                _autoHideTimer = null;
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
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer?.Stop();
                _recordDurationTimer.Stop();

                RecordingStateGrid.Visibility = Visibility.Collapsed;
                TranscribingStateGrid.Visibility = Visibility.Collapsed;
                SuccessStateGrid.Visibility = Visibility.Collapsed;
                ErrorStateGrid.Visibility = Visibility.Collapsed;
                WelcomeStateGrid.Visibility = Visibility.Visible;

                RepositionWindow();
                Show();

                SetAutoHide(4.0, () => WelcomeStateGrid.Visibility == Visibility.Visible);
            });
        }

        public void ShowSuccess(string text)
        {
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer?.Stop();
                _recordDurationTimer.Stop();

                SuccessPreviewText.Text = "Inserted";

                RecordingStateGrid.Visibility = Visibility.Collapsed;
                TranscribingStateGrid.Visibility = Visibility.Collapsed;
                SuccessStateGrid.Visibility = Visibility.Visible;
                ErrorStateGrid.Visibility = Visibility.Collapsed;
                WelcomeStateGrid.Visibility = Visibility.Collapsed;

                Show();
                RepositionWindow();

                SetAutoHide(1.0, () => SuccessStateGrid.Visibility == Visibility.Visible);
            });
        }

        public void ShowError(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer?.Stop();
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

                SetAutoHide(1.6, () => ErrorStateGrid.Visibility == Visibility.Visible);
            });
        }

        private void SetAutoHide(double seconds, Func<bool> condition)
        {
            _autoHideTimer?.Stop();
            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _autoHideTimer.Tick += (s, e) =>
            {
                _autoHideTimer?.Stop();
                _autoHideTimer = null;
                if (condition())
                {
                    Hide();
                }
            };
            _autoHideTimer.Start();
        }

        public void HideOverlay()
        {
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer?.Stop();
                _autoHideTimer = null;
                _recordDurationTimer.Stop();
                Hide();
            });
        }

        private void OnPillMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _autoHideTimer?.Stop();
            _autoHideTimer = null;
            HideOverlay();
        }
    }
}
