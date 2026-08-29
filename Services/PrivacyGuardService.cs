using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using JustSTT.Native;

namespace JustSTT.Services
{
    public class PrivacyGuardService
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public Win32.POINT rcCaret;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);

        private const int EM_GETPASSWORDCHAR = 0x00D2;

        public static bool IsFocusedElementPasswordField()
        {
            // 1. Fast Win32 Classic Edit control inspection
            try
            {
                var guiInfo = new GUITHREADINFO();
                guiInfo.cbSize = Marshal.SizeOf(guiInfo);
                if (GetGUIThreadInfo(0, ref guiInfo) && guiInfo.hwndFocus != IntPtr.Zero)
                {
                    int pwChar = SendMessage(guiInfo.hwndFocus, EM_GETPASSWORDCHAR, 0, 0);
                    if (pwChar != 0)
                    {
                        return true;
                    }
                }
            }
            catch { }

            // 2. UI Automation inspection on MTA thread with strict 60ms timeout to avoid hanging
            try
            {
                var uiaTask = Task.Run(() =>
                {
                    try
                    {
                        var focused = AutomationElement.FocusedElement;
                        return focused != null && focused.Current.IsPassword;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (uiaTask.Wait(TimeSpan.FromMilliseconds(60)))
                {
                    return uiaTask.Result;
                }
            }
            catch { }

            return false;
        }
    }
}
