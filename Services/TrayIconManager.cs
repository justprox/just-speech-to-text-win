using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using JustSTT.Native;
using JustSTT.Services;
using Clipboard = System.Windows.Clipboard;

namespace JustSTT.Services
{
    public class TrayIconManager : IDisposable
    {
        private readonly RecentHistoryService _historyService;
        private readonly ThemeService _themeService;
        private readonly Action _openMainWindowAction;
        private readonly Action _exitAction;

        private HwndSource? _hwndSource;
        private System.Drawing.Icon? _iconRef;
        private IntPtr _hIcon = IntPtr.Zero;
        private Win32.NOTIFYICONDATA _notifyIconData;
        private bool _isCreated = false;

        public TrayIconManager(
            RecentHistoryService historyService,
            ThemeService themeService,
            Action openMainWindowAction,
            Action exitAction)
        {
            _historyService = historyService;
            _themeService = themeService;
            _openMainWindowAction = openMainWindowAction;
            _exitAction = exitAction;

            CreateTrayIcon();
        }

        private void CreateTrayIcon()
        {
            var parameters = new HwndSourceParameters("JustSTTTrayMsgHost")
            {
                WindowStyle = 0,
                Width = 0,
                Height = 0
            };
            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);

            _hIcon = LoadAppIconHandle();

            _notifyIconData = new Win32.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(Win32.NOTIFYICONDATA)),
                hWnd = _hwndSource.Handle,
                uID = 1001,
                uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
                uCallbackMessage = Win32.WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "Just Speech to Text (Click to open dashboard)"
            };

            _isCreated = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _notifyIconData);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_TRAYICON)
            {
                int mouseMsg = lParam.ToInt32() & 0xFFFF;
                const int WM_LBUTTONUP = 0x0202;
                const int WM_RBUTTONUP = 0x0205;

                if (mouseMsg == WM_LBUTTONUP)
                {
                    Application.Current?.Dispatcher.BeginInvoke(_openMainWindowAction);
                    handled = true;
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    Application.Current?.Dispatcher.BeginInvoke(ShowThemedContextMenu);
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private void ShowThemedContextMenu()
        {
            bool isDark = _themeService.IsDarkTheme;

            var bgBrush = isDark
                ? new SolidColorBrush(Color.FromRgb(16, 17, 24))
                : new SolidColorBrush(Color.FromRgb(255, 255, 255));

            var borderBrush = isDark
                ? new SolidColorBrush(Color.FromRgb(42, 45, 62))
                : new SolidColorBrush(Color.FromRgb(226, 232, 240));

            var textPrimary = isDark
                ? new SolidColorBrush(Color.FromRgb(245, 245, 250))
                : new SolidColorBrush(Color.FromRgb(15, 23, 42));

            var textSecondary = isDark
                ? new SolidColorBrush(Color.FromRgb(140, 145, 165))
                : new SolidColorBrush(Color.FromRgb(100, 116, 139));

            var accentText = isDark
                ? new SolidColorBrush(Color.FromRgb(216, 180, 254))
                : new SolidColorBrush(Color.FromRgb(124, 58, 237));

            var separatorBrush = isDark
                ? new SolidColorBrush(Color.FromRgb(36, 38, 52))
                : new SolidColorBrush(Color.FromRgb(235, 238, 245));

            var hoverBrush = isDark
                ? new SolidColorBrush(Color.FromRgb(40, 30, 66))
                : new SolidColorBrush(Color.FromRgb(238, 233, 254));

            var menu = new ContextMenu
            {
                Background = bgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                FontSize = 13,
                MinWidth = 280
            };

            // Custom ContextMenu Template without any white icon gutter
            var menuTemplate = new ControlTemplate(typeof(ContextMenu));
            var menuBorder = new FrameworkElementFactory(typeof(Border));
            menuBorder.SetValue(Border.BackgroundProperty, bgBrush);
            menuBorder.SetValue(Border.BorderBrushProperty, borderBrush);
            menuBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            menuBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            menuBorder.SetValue(Border.PaddingProperty, new Thickness(6));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.IsItemsHostProperty, true);
            menuBorder.AppendChild(stack);
            menuTemplate.VisualTree = menuBorder;
            menu.Template = menuTemplate;

            var itemStyle = CreateThemedMenuItemStyle(hoverBrush);

            // 1. Open Dashboard
            var openItem = new MenuItem
            {
                Header = "🎙️ Open Just Speech to Text Dashboard",
                FontWeight = FontWeights.Bold,
                Foreground = accentText,
                Style = itemStyle
            };
            openItem.Click += (s, e) => _openMainWindowAction();
            menu.Items.Add(openItem);

            menu.Items.Add(new Separator { Background = separatorBrush, Margin = new Thickness(4, 4, 4, 4) });

            // 2. Recent Recordings
            var headerItem = new MenuItem
            {
                Header = "📝 RECENT DICTATIONS (CLICK TO COPY):",
                IsEnabled = false,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = textSecondary,
                Style = itemStyle
            };
            menu.Items.Add(headerItem);

            var recentRecordings = _historyService.GetRecentRecordings();
            if (recentRecordings.Count == 0)
            {
                var emptyItem = new MenuItem
                {
                    Header = "   (No recordings yet)",
                    IsEnabled = false,
                    Foreground = textSecondary,
                    Style = itemStyle
                };
                menu.Items.Add(emptyItem);
            }
            else
            {
                for (int i = 0; i < recentRecordings.Count; i++)
                {
                    var rec = recentRecordings[i];
                    string textPreview = string.IsNullOrWhiteSpace(rec.TranscriptText)
                        ? "No speech recognized"
                        : (rec.TranscriptText.Length > 34 ? rec.TranscriptText.Substring(0, 31) + "..." : rec.TranscriptText);

                    var recItem = new MenuItem
                    {
                        Header = $"#{i + 1} ({rec.FormattedTime}): {textPreview}",
                        Foreground = textPrimary,
                        Style = itemStyle
                    };

                    recItem.Click += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(rec.TranscriptText))
                        {
                            Clipboard.SetText(rec.TranscriptText);
                        }
                    };

                    menu.Items.Add(recItem);
                }
            }

            menu.Items.Add(new Separator { Background = separatorBrush, Margin = new Thickness(4, 4, 4, 4) });

            // 3. Exit
            var exitItem = new MenuItem
            {
                Header = "🚪 Exit Just Speech to Text",
                Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                Style = itemStyle
            };
            exitItem.Click += (s, e) => _exitAction();
            menu.Items.Add(exitItem);

            if (_hwndSource != null)
            {
                Win32.SetForegroundWindow(_hwndSource.Handle);
            }

            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private static Style CreateThemedMenuItemStyle(Brush hoverBrush)
        {
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));

            var template = new ControlTemplate(typeof(MenuItem));
            var borderFactory = new FrameworkElementFactory(typeof(Border), "Border");
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Left);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "Border"));
            template.Triggers.Add(hoverTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private IntPtr LoadAppIconHandle()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath))
                {
                    _iconRef = new System.Drawing.Icon(iconPath, 32, 32);
                    return Win32.CopyIcon(_iconRef.Handle);
                }

                var mainModule = Process.GetCurrentProcess().MainModule;
                if (mainModule?.FileName != null)
                {
                    _iconRef = System.Drawing.Icon.ExtractAssociatedIcon(mainModule.FileName);
                    if (_iconRef != null)
                    {
                        return Win32.CopyIcon(_iconRef.Handle);
                    }
                }
            }
            catch { }

            return System.Drawing.SystemIcons.Application.Handle;
        }

        public void Dispose()
        {
            if (_isCreated)
            {
                Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _notifyIconData);
                _isCreated = false;
            }

            if (_hIcon != IntPtr.Zero)
            {
                Win32.DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            _iconRef?.Dispose();
            _hwndSource?.Dispose();
        }
    }
}
