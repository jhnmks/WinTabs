using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using wintabs.DllImports;

namespace wintabs.Utilities
{
    internal static class FocusHelper
    {
        /// <summary>
        /// Marks keyboard input that WinTabs injects itself, so its own keyboard hook can ignore it.
        /// </summary>
        public const nuint InjectedInputTag = 0x57545342; // "WTSB"

        private static int s_activationVersion;

        public static async Task<bool> ActivateTabAsync(IntPtr hwnd, Func<bool> canRetry)
        {
            var foreground = PInvoke.GetForegroundWindow();
            if (ForceActivate(hwnd, out int version))
                return true;

            // SendInput queues the Alt tap. Let Windows process it before retrying, without blocking the UI.
            await Task.Delay(50);
            var current = PInvoke.GetForegroundWindow();
            if (current.Value == hwnd)
                return true;

            return version == Volatile.Read(ref s_activationVersion) && current.Value == foreground.Value &&
                canRetry() && PInvoke.SetForegroundWindow(new(hwnd));
        }

        /// <summary>
        /// Brings <paramref name="hwnd"/> to the foreground. Runs synchronously on the calling thread so no
        /// input attachment can outlive the call and block the next activation.
        /// </summary>
        public static bool ForceActivate(IntPtr hwnd) => ForceActivate(hwnd, out _);

        private static bool ForceActivate(IntPtr hwnd, out int version)
        {
            version = Interlocked.Increment(ref s_activationVersion);
            if (PInvoke.GetForegroundWindow().Value == hwnd || PInvoke.SetForegroundWindow(new(hwnd)))
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
