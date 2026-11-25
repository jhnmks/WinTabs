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

        internal enum ActivationResult { Activated, Superseded, Refused }

        internal static IntPtr ForegroundRoot() => WindowExtensions.GetRootWindow(PInvoke.GetForegroundWindow().Value);

        public static Task<ActivationResult> ActivateTabAsync(IntPtr hwnd, Func<bool> canRetry)
        {
            int version = Interlocked.Increment(ref s_activationVersion);
            return ActivateTabAsync(hwnd, ForegroundRoot, TryActivate,
                () => version == Volatile.Read(ref s_activationVersion) && canRetry(), () => Task.Delay(50));
        }

        internal static async Task<ActivationResult> ActivateTabAsync(IntPtr hwnd, Func<IntPtr> foreground,
            Func<IntPtr, bool> activate, Func<bool> stillRequested, Func<Task> wait)
        {
            var previous = foreground();
            if (previous == hwnd)
                return ActivationResult.Activated;
            // A successful SetForegroundWindow can still be pending on the target's UI thread.
            activate(hwnd);
            for (int i = 0; i <= 2; i++)
            {
                var current = foreground();
                if (current == hwnd)
                    return ActivationResult.Activated;
                if (!stillRequested() || current != previous && current != IntPtr.Zero)
                    return ActivationResult.Superseded;
                if (i == 2)
                    break;
                if (i == 1 && current == previous)
                    activate(hwnd);
                await wait();
            }
            return ActivationResult.Refused;
        }

        public static bool ForceActivate(IntPtr hwnd)
        {
            Interlocked.Increment(ref s_activationVersion);
            return TryActivate(hwnd);
        }

        private static bool TryActivate(IntPtr hwnd)
        {
            if (ForegroundRoot() == hwnd || PInvoke.SetForegroundWindow(new(hwnd)))
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
