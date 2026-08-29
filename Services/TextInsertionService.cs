using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using JustSTT.Native;

namespace JustSTT.Services
{
    public class TextInsertionService
    {
        private readonly ConfigService _configService;

        public TextInsertionService(ConfigService configService)
        {
            _configService = configService;
        }

        public async Task InsertTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            string method = _configService.Settings.TextInsertionMethod;

            if (method == "SendInput" || (method == "Auto" && text.Length < 15 && !text.Contains('\n')))
            {
                SendTextInputDirect(text);
            }
            else
            {
                await InsertViaClipboardAsync(text);
            }
        }

        private async Task InsertViaClipboardAsync(string text)
        {
            IDataObject? originalClipboardData = null;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (Clipboard.ContainsText() || Clipboard.ContainsImage())
                    {
                        originalClipboardData = Clipboard.GetDataObject();
                    }
                    Clipboard.SetText(text);
                }
                catch
                {
                    // Fallback to direct input if clipboard fails
                    SendTextInputDirect(text);
                    return;
                }
            });

            await Task.Delay(40);

            // Send Ctrl+V
            SendCtrlV();

            // Allow sufficient time for the foreground app (VS Code, Word, Terminal) to process the paste message
            await Task.Delay(250);

            // Restore clipboard
            if (originalClipboardData != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        Clipboard.SetDataObject(originalClipboardData);
                    }
                    catch { }
                });
            }
        }

        private static void SendCtrlV()
        {
            var inputs = new Win32.INPUT[4];

            // Ctrl down
            inputs[0].type = Win32.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = Win32.VK_CONTROL;

            // V down
            inputs[1].type = Win32.INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = 0x56; // 'V'

            // V up
            inputs[2].type = Win32.INPUT_KEYBOARD;
            inputs[2].u.ki.wVk = 0x56;
            inputs[2].u.ki.dwFlags = Win32.KEYEVENTF_KEYUP;

            // Ctrl up
            inputs[3].type = Win32.INPUT_KEYBOARD;
            inputs[3].u.ki.wVk = Win32.VK_CONTROL;
            inputs[3].u.ki.dwFlags = Win32.KEYEVENTF_KEYUP;

            Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        }

        private static void SendTextInputDirect(string text)
        {
            // Normalize CRLF to avoid duplicate newlines in edit controls
            string normalized = text.Replace("\r\n", "\r");

            var inputs = new Win32.INPUT[normalized.Length * 2];
            int i = 0;

            foreach (char c in normalized)
            {
                // Key down Unicode
                inputs[i].type = Win32.INPUT_KEYBOARD;
                inputs[i].u.ki.wVk = 0;
                inputs[i].u.ki.wScan = c;
                inputs[i].u.ki.dwFlags = Win32.KEYEVENTF_UNICODE;

                // Key up Unicode
                inputs[i + 1].type = Win32.INPUT_KEYBOARD;
                inputs[i + 1].u.ki.wVk = 0;
                inputs[i + 1].u.ki.wScan = c;
                inputs[i + 1].u.ki.dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP;

                i += 2;
            }

            Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        }
    }
}
