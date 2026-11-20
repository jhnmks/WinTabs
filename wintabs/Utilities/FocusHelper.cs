using System;
using System.Runtime.InteropServices;

using wintabs.DllImports;

namespace wintabs.Utilities
{
    internal static class FocusHelper
    {
        /// <summary>
        /// Marks keyboard input that WinTabs injects itself, so its own keyboard hook can ignore it.
        /// </summary>
        public const nuint InjectedInputTag = 0x57545342; // "WTSB"

        /// <summary>
        /// Brings <paramref name="hwnd"/> to the foreground. Runs synchronously on the calling thread so no
        /// input attachment can outlive the call and block the next activation.
        /// </summary>
        public static bool ForceActivate(IntPtr hwnd)
        {
            if (PInvoke.SetForegroundWindow(new(hwnd)))
                return true;

            // Windows only lets the process that received the last input change the foreground window.
            // Tap Alt (tagged, so our own hook ignores it) to satisfy that rule, then try again.
            INPUT[] inp = new INPUT[2];
            inp[0].type = inp[1].type = INPUT_typeFlags.INPUT_KEYBOARD;
            inp[0].Anonymous.ki.wVk = inp[1].Anonymous.ki.wVk = (ushort)Constants.VK_MENU;
            inp[0].Anonymous.ki.dwFlags = keybd_eventFlags.KEYEVENTF_EXTENDEDKEY;
            inp[1].Anonymous.ki.dwFlags = keybd_eventFlags.KEYEVENTF_EXTENDEDKEY | keybd_eventFlags.KEYEVENTF_KEYUP;
            inp[0].Anonymous.ki.dwExtraInfo = inp[1].Anonymous.ki.dwExtraInfo = InjectedInputTag;
            PInvoke.SendInput(inp, Marshal.SizeOf<INPUT>());

            return PInvoke.SetForegroundWindow(new(hwnd));
        }
    }
}
