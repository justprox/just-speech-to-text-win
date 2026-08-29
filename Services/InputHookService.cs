using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using JustSTT.Models;
using JustSTT.Native;

namespace JustSTT.Services
{
    public class InputHookService : IDisposable
    {
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;

        private Win32.LowLevelKeyboardProc? _keyboardProc;
        private Win32.LowLevelMouseProc? _mouseProc;

        private readonly ConfigService _configService;

        private readonly HashSet<string> _pressedTriggers = new();
        private DateTime _lastTriggerPressTime = DateTime.MinValue;
        private bool _isCapturingCustomTrigger = false;
        private Action<TriggerBinding>? _triggerCapturedCallback;

        public event Action<TriggerBinding>? TriggerPressed;
        public event Action<TriggerBinding>? TriggerReleased;
        public event Action? CancelPressed;
        public event Action? HandsFreeToggleRequested;

        public bool IsAnyTriggerHeld => _pressedTriggers.Count > 0;

        public InputHookService(ConfigService configService)
        {
            _configService = configService;
            InstallHooks();
        }

        public void StartCapturingTrigger(Action<TriggerBinding> callback)
        {
            _isCapturingCustomTrigger = true;
            _triggerCapturedCallback = callback;
        }

        public void CancelCapturingTrigger()
        {
            _isCapturingCustomTrigger = false;
            _triggerCapturedCallback = null;
        }

        private void InstallHooks()
        {
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;

            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule? curModule = curProcess.MainModule;
            IntPtr moduleHandle = Win32.GetModuleHandle(curModule?.ModuleName);

            _keyboardHookId = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            _mouseHookId = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var kbdStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)kbdStruct.vkCode;
                int message = wParam.ToInt32();

                bool isKeyDown = message == Win32.WM_KEYDOWN || message == Win32.WM_SYSKEYDOWN;
                bool isKeyUp = message == Win32.WM_KEYUP || message == Win32.WM_SYSKEYUP;

                // Capturing trigger mode for Settings UI
                if (_isCapturingCustomTrigger && isKeyDown)
                {
                    if (vkCode != Win32.VK_ESCAPE)
                    {
                        string name = GetKeyDisplayName(vkCode, kbdStruct.flags);
                        var newTrigger = new TriggerBinding
                        {
                            Type = TriggerType.KeyboardKey,
                            KeyCode = vkCode,
                            DisplayName = name
                        };

                        _isCapturingCustomTrigger = false;
                        var cb = _triggerCapturedCallback;
                        _triggerCapturedCallback = null;
                        ThreadPool.QueueUserWorkItem(_ => cb?.Invoke(newTrigger));
                        return (IntPtr)1; // Consume key
                    }
                    else
                    {
                        _isCapturingCustomTrigger = false;
                        var cb = _triggerCapturedCallback;
                        _triggerCapturedCallback = null;
                        ThreadPool.QueueUserWorkItem(_ => cb?.Invoke(null!));
                        return (IntPtr)1;
                    }
                }

                // Check Escape key for Cancel
                if (isKeyDown && vkCode == Win32.VK_ESCAPE)
                {
                    ThreadPool.QueueUserWorkItem(_ => CancelPressed?.Invoke());
                }

                // Check Space while trigger is held for Hands-Free toggle
                if (isKeyDown && vkCode == Win32.VK_SPACE && _pressedTriggers.Count > 0)
                {
                    ThreadPool.QueueUserWorkItem(_ => HandsFreeToggleRequested?.Invoke());
                    return (IntPtr)1; // Consume space
                }

                // Check configured keyboard triggers
                foreach (var trigger in _configService.Settings.ActiveTriggers)
                {
                    if (trigger.Type == TriggerType.KeyboardKey && MatchesVirtualKey(trigger.KeyCode, vkCode, kbdStruct.flags))
                    {
                        string keyId = $"key_{trigger.KeyCode}";

                        if (isKeyDown)
                        {
                            if (!_pressedTriggers.Contains(keyId))
                            {
                                _pressedTriggers.Add(keyId);

                                // Check double-tap for Hands-Free
                                var now = DateTime.Now;
                                if ((now - _lastTriggerPressTime).TotalMilliseconds < 350 && _configService.Settings.HandsFreeEnabled)
                                {
                                    ThreadPool.QueueUserWorkItem(_ => HandsFreeToggleRequested?.Invoke());
                                }
                                else
                                {
                                    var tr = trigger;
                                    ThreadPool.QueueUserWorkItem(_ => TriggerPressed?.Invoke(tr));
                                }
                                _lastTriggerPressTime = now;
                            }
                        }
                        else if (isKeyUp)
                        {
                            if (_pressedTriggers.Contains(keyId))
                            {
                                _pressedTriggers.Remove(keyId);
                                var tr = trigger;
                                ThreadPool.QueueUserWorkItem(_ => TriggerReleased?.Invoke(tr));
                            }
                        }
                    }
                }
            }

            return Win32.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var mouseStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                int message = wParam.ToInt32();

                int mouseButton = 0;
                bool isDown = false;
                bool isUp = false;

                if (message == Win32.WM_LBUTTONDOWN) { mouseButton = 1; isDown = true; }
                else if (message == Win32.WM_LBUTTONUP) { mouseButton = 1; isUp = true; }
                else if (message == Win32.WM_RBUTTONDOWN) { mouseButton = 2; isDown = true; }
                else if (message == Win32.WM_RBUTTONUP) { mouseButton = 2; isUp = true; }
                else if (message == Win32.WM_MBUTTONDOWN) { mouseButton = 3; isDown = true; }
                else if (message == Win32.WM_MBUTTONUP) { mouseButton = 3; isUp = true; }
                else if (message == Win32.WM_XBUTTONDOWN || message == Win32.WM_XBUTTONUP)
                {
                    uint highWord = (mouseStruct.mouseData >> 16) & 0xFFFF;
                    if (highWord == Win32.XBUTTON1) mouseButton = 4; // Mouse 4 (Back)
                    else if (highWord == Win32.XBUTTON2) mouseButton = 5; // Mouse 5 (Forward)

                    isDown = message == Win32.WM_XBUTTONDOWN;
                    isUp = message == Win32.WM_XBUTTONUP;
                }

                if (mouseButton > 0)
                {
                    // Capturing trigger mode for Settings UI
                    if (_isCapturingCustomTrigger && isDown && mouseButton >= 3)
                    {
                        string name = mouseButton == 4 ? "Mouse 4 (Back)" :
                                     mouseButton == 5 ? "Mouse 5 (Forward)" : "Middle Mouse";

                        var newTrigger = new TriggerBinding
                        {
                            Type = TriggerType.MouseButton,
                            MouseButton = mouseButton,
                            DisplayName = name
                        };

                        _isCapturingCustomTrigger = false;
                        var cb = _triggerCapturedCallback;
                        _triggerCapturedCallback = null;
                        ThreadPool.QueueUserWorkItem(_ => cb?.Invoke(newTrigger));
                        return (IntPtr)1; // Consume button
                    }

                    // Check configured mouse triggers
                    foreach (var trigger in _configService.Settings.ActiveTriggers)
                    {
                        if (trigger.Type == TriggerType.MouseButton && trigger.MouseButton == mouseButton)
                        {
                            string mouseId = $"mouse_{mouseButton}";

                            if (isDown)
                            {
                                if (!_pressedTriggers.Contains(mouseId))
                                {
                                    _pressedTriggers.Add(mouseId);
                                    var tr = trigger;
                                    ThreadPool.QueueUserWorkItem(_ => TriggerPressed?.Invoke(tr));
                                }
                            }
                            else if (isUp)
                            {
                                if (_pressedTriggers.Contains(mouseId))
                                {
                                    _pressedTriggers.Remove(mouseId);
                                    var tr = trigger;
                                    ThreadPool.QueueUserWorkItem(_ => TriggerReleased?.Invoke(tr));
                                }
                            }
                        }
                    }
                }
            }

            return Win32.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private static bool MatchesVirtualKey(int targetVk, int actualVk, uint flags)
        {
            if (targetVk == Win32.VK_RCONTROL)
            {
                bool isExtended = (flags & 0x01) != 0;
                return (actualVk == Win32.VK_RCONTROL) || (actualVk == Win32.VK_CONTROL && isExtended);
            }
            if (targetVk == Win32.VK_LCONTROL)
            {
                bool isExtended = (flags & 0x01) != 0;
                return (actualVk == Win32.VK_LCONTROL) || (actualVk == Win32.VK_CONTROL && !isExtended);
            }
            return targetVk == actualVk;
        }

        public static string GetKeyDisplayName(int vkCode, uint flags = 0)
        {
            bool isExtended = (flags & 0x01) != 0;
            if (vkCode == Win32.VK_RCONTROL || (vkCode == Win32.VK_CONTROL && isExtended)) return "Right Ctrl";
            if (vkCode == Win32.VK_LCONTROL || (vkCode == Win32.VK_CONTROL && !isExtended)) return "Left Ctrl";
            if (vkCode == Win32.VK_RMENU || (vkCode == Win32.VK_MENU && isExtended)) return "Right Alt";
            if (vkCode == Win32.VK_LMENU || (vkCode == Win32.VK_MENU && !isExtended)) return "Left Alt";
            if (vkCode == Win32.VK_CAPITAL) return "Caps Lock";
            if (vkCode == Win32.VK_SPACE) return "Space";
            if (vkCode >= Win32.VK_F1 && vkCode <= Win32.VK_F12) return $"F{vkCode - Win32.VK_F1 + 1}";

            var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(vkCode);
            return key.ToString();
        }

        public void Dispose()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }
            if (_mouseHookId != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
        }
    }
}
