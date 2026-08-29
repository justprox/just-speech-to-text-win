using System;
using System.Windows;
using System.Windows.Interop;
using JustSTT.Native;

namespace JustSTT.Views
{
    public partial class LiveTranscriptBubbleWindow : Window
    {
        public LiveTranscriptBubbleWindow()
        {
            InitializeComponent();

            Loaded += (s, e) => Reposition();
            SizeChanged += (s, e) => Reposition();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            // Apply WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
            IntPtr exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
            long newExStyle = exStyle.ToInt64() | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(newExStyle));
        }

        public void Reposition()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double workAreaBottom = SystemParameters.WorkArea.Bottom;

            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;

            Left = (screenWidth - w) / 2.0;
            // Position directly above the bottom mini voice pill
            Top = Math.Max(20, workAreaBottom - 46 - 16 - h - 8);
        }

        public void ShowLiveText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            Dispatcher.Invoke(() =>
            {
                LiveSpeechTextBlock.Text = text;
                UpdateLayout();
                Reposition();
                if (!IsVisible)
                {
                    Show();
                }
            });
        }

        public void HideBubble()
        {
            Dispatcher.Invoke(() =>
            {
                LiveSpeechTextBlock.Text = string.Empty;
                Hide();
            });
        }
    }
}
