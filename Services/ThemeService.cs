using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using JustSTT.Native;
using Microsoft.Win32;

namespace JustSTT.Services
{
    public class ThemeService : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly UserPreferenceChangedEventHandler _userPreferenceChangedHandler;
        public event Action? ThemeChanged;

        public bool IsDarkTheme { get; private set; } = true;

        public ThemeService(ConfigService configService)
        {
            _configService = configService;
            _userPreferenceChangedHandler = (s, e) =>
            {
                if (_configService.Settings.ThemeMode == "System")
                {
                    Application.Current?.Dispatcher.Invoke(ApplyTheme);
                }
            };
            SystemEvents.UserPreferenceChanged += _userPreferenceChangedHandler;
        }

        public void ApplyTheme()
        {
            string mode = _configService.Settings.ThemeMode ?? "System";
            bool isDark;

            if (mode == "Dark")
            {
                isDark = true;
            }
            else if (mode == "Light")
            {
                isDark = false;
            }
            else // System
            {
                isDark = IsSystemDarkTheme();
            }

            IsDarkTheme = isDark;
            ApplyPaletteToAppResources(isDark);
            ThemeChanged?.Invoke();
        }

        public void ApplyWindowTheme(Window window)
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle != IntPtr.Zero)
            {
                int darkMode = IsDarkTheme ? 1 : 0;
                Win32.DwmSetWindowAttribute(helper.Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    object? val = key.GetValue("AppsUseLightTheme");
                    if (val is int intVal)
                    {
                        return intVal == 0; // 0 = Dark, 1 = Light
                    }
                }
            }
            catch { }

            return true; // Default fallback to dark
        }

        private static void SetBrush(ResourceDictionary res, string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze(); // Free-threaded performance boost for WPF rendering
            res[key] = brush;
        }

        private static void ApplyPaletteToAppResources(bool isDark)
        {
            var app = Application.Current;
            if (app == null) return;

            var res = app.Resources;

            if (isDark)
            {
                // ================= DARK PALETTE =================
                SetBrush(res, "BgWindow", Color.FromRgb(10, 10, 14));
                SetBrush(res, "BgSidebar", Color.FromRgb(14, 14, 20));
                SetBrush(res, "BgCard", Color.FromRgb(19, 19, 27));
                SetBrush(res, "BgInput", Color.FromRgb(24, 24, 34));
                SetBrush(res, "BgInputHover", Color.FromRgb(34, 34, 48));
                SetBrush(res, "BorderDefault", Color.FromRgb(39, 39, 56));
                SetBrush(res, "BorderFocus", Color.FromRgb(124, 58, 237));
                SetBrush(res, "TextPrimary", Color.FromRgb(243, 243, 247));
                SetBrush(res, "TextSecondary", Color.FromRgb(142, 142, 160));
                SetBrush(res, "ScrollThumb", Color.FromRgb(58, 58, 78));
                SetBrush(res, "ScrollThumbHover", Color.FromRgb(92, 92, 122));
                SetBrush(res, "NavActiveBg", Color.FromRgb(38, 26, 66));
                SetBrush(res, "NavActiveBorder", Color.FromRgb(94, 58, 158));
                SetBrush(res, "BtnBg", Color.FromRgb(30, 30, 43));
                SetBrush(res, "BtnBorder", Color.FromRgb(46, 46, 66));
                SetBrush(res, "DropdownBg", Color.FromRgb(20, 20, 30));

                SetBrush(res, "PrimaryBtnBg", Color.FromRgb(124, 58, 237));
                SetBrush(res, "PrimaryBtnBorder", Color.FromRgb(139, 92, 246));
                SetBrush(res, "PrimaryBtnFg", Color.FromRgb(255, 255, 255));

                SetBrush(res, "CardBadgeBg", Color.FromRgb(38, 26, 66));
                SetBrush(res, "CardBadgeFg", Color.FromRgb(168, 85, 247));

                SetBrush(res, "PurgeBtnBg", Color.FromRgb(38, 18, 22));
                SetBrush(res, "PurgeBtnBorder", Color.FromRgb(90, 30, 38));
                SetBrush(res, "PurgeBtnFg", Color.FromRgb(248, 113, 113));

                SetBrush(res, "LogoBg", Color.FromRgb(38, 26, 66));
                SetBrush(res, "LogoBorder", Color.FromRgb(94, 58, 158));

                // Monochromatic HUD / Floating Overlay (Translucent Dark Glass)
                SetBrush(res, "OverlayBg", Color.FromArgb(240, 14, 14, 18));
                SetBrush(res, "OverlayBubbleBg", Color.FromArgb(135, 14, 14, 22)); // 53% translucent dark glass
                SetBrush(res, "OverlayBorder", Color.FromArgb(120, 80, 80, 105));
                SetBrush(res, "OverlayText", Color.FromRgb(255, 255, 255));
                SetBrush(res, "OverlayTextSecondary", Color.FromRgb(160, 160, 175));
                SetBrush(res, "OverlayWaveform", Color.FromRgb(255, 255, 255));
                SetBrush(res, "OverlayCapsuleBg", Color.FromRgb(28, 28, 36));
                SetBrush(res, "OverlayCapsuleBorder", Color.FromRgb(55, 55, 70));

                SetBrush(res, "CheckBg", Color.FromRgb(28, 30, 42));
                SetBrush(res, "CheckBorder", Color.FromRgb(60, 64, 86));
                SetBrush(res, "CheckActiveBg", Color.FromRgb(124, 58, 237));
            }
            else
            {
                // ================= LIGHT PALETTE (Linear / Apple Pro Light) =================
                SetBrush(res, "BgWindow", Color.FromRgb(248, 250, 252));
                SetBrush(res, "BgSidebar", Color.FromRgb(241, 245, 249));
                SetBrush(res, "BgCard", Color.FromRgb(255, 255, 255));
                SetBrush(res, "BgInput", Color.FromRgb(248, 250, 252));
                SetBrush(res, "BgInputHover", Color.FromRgb(241, 245, 249));
                SetBrush(res, "BorderDefault", Color.FromRgb(226, 232, 240));
                SetBrush(res, "BorderFocus", Color.FromRgb(79, 70, 229));
                SetBrush(res, "TextPrimary", Color.FromRgb(15, 23, 42));
                SetBrush(res, "TextSecondary", Color.FromRgb(100, 116, 139));
                SetBrush(res, "ScrollThumb", Color.FromRgb(203, 213, 225));
                SetBrush(res, "ScrollThumbHover", Color.FromRgb(148, 163, 184));
                SetBrush(res, "NavActiveBg", Color.FromRgb(238, 242, 255));
                SetBrush(res, "NavActiveBorder", Color.FromRgb(199, 210, 254));
                SetBrush(res, "BtnBg", Color.FromRgb(255, 255, 255));
                SetBrush(res, "BtnBorder", Color.FromRgb(226, 232, 240));
                SetBrush(res, "DropdownBg", Color.FromRgb(255, 255, 255));

                SetBrush(res, "PrimaryBtnBg", Color.FromRgb(15, 23, 42));
                SetBrush(res, "PrimaryBtnBorder", Color.FromRgb(15, 23, 42));
                SetBrush(res, "PrimaryBtnFg", Color.FromRgb(255, 255, 255));

                SetBrush(res, "CardBadgeBg", Color.FromRgb(238, 242, 255));
                SetBrush(res, "CardBadgeFg", Color.FromRgb(67, 56, 202));

                SetBrush(res, "PurgeBtnBg", Color.FromRgb(254, 242, 242));
                SetBrush(res, "PurgeBtnBorder", Color.FromRgb(254, 202, 202));
                SetBrush(res, "PurgeBtnFg", Color.FromRgb(220, 38, 38));

                SetBrush(res, "LogoBg", Color.FromRgb(238, 242, 255));
                SetBrush(res, "LogoBorder", Color.FromRgb(199, 210, 254));

                // Monochromatic HUD / Floating Overlay (Translucent White Frosted Glass)
                SetBrush(res, "OverlayBg", Color.FromArgb(245, 255, 255, 255));
                SetBrush(res, "OverlayBubbleBg", Color.FromArgb(150, 255, 255, 255)); // 58% translucent frosted white glass
                SetBrush(res, "OverlayBorder", Color.FromArgb(140, 215, 220, 230));
                SetBrush(res, "OverlayText", Color.FromRgb(15, 23, 42));
                SetBrush(res, "OverlayTextSecondary", Color.FromRgb(100, 116, 139));
                SetBrush(res, "OverlayWaveform", Color.FromRgb(15, 23, 42));
                SetBrush(res, "OverlayCapsuleBg", Color.FromRgb(241, 245, 249));
                SetBrush(res, "OverlayCapsuleBorder", Color.FromRgb(226, 232, 240));

                SetBrush(res, "CheckBg", Color.FromRgb(255, 255, 255));
                SetBrush(res, "CheckBorder", Color.FromRgb(203, 213, 225));
                SetBrush(res, "CheckActiveBg", Color.FromRgb(79, 70, 229));
            }
        }

        public void Dispose()
        {
            try
            {
                SystemEvents.UserPreferenceChanged -= _userPreferenceChangedHandler;
            }
            catch { }
        }
    }
}
