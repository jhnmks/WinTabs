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

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public int left, top, right, bottom;
        }

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetClassName(IntPtr hWnd, char[] className, int maxCount);

        private static string ClassOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return "-";
            var buffer = new char[128];
            int length = GetClassName(hwnd, buffer, buffer.Length);
            return $"{hwnd:X}:{(length > 0 ? new string(buffer, 0, length) : "?")}";
        }

        /// <summary>
        /// Window classes (never titles) of the foreground window, its root and the control with keyboard focus,
        /// for diagnosing where keyboard input goes.
        /// </summary>
        internal static string DescribeForeground()
        {
            var foreground = PInvoke.GetForegroundWindow().Value;
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            var focus = GetGUIThreadInfo(0, ref info) ? info.hwndFocus : IntPtr.Zero;
            return $"foreground={ClassOf(foreground)} root={ClassOf(WindowExtensions.GetRootWindow(foreground))} focus={ClassOf(focus)}";
        }

        internal static IntPtr ForegroundRoot() => WindowExtensions.GetRootWindow(PInvoke.GetForegroundWindow().Value);

        /// <summary>
        /// Like <see cref="ForegroundRoot"/>, but zero while the foreground is a hidden window embedded in its root
        /// (such as the remote viewer of a connection tab that a host app no longer shows). Keyboard input then goes
        /// nowhere visible, so the root still has to be activated to hand focus to its visible content.
        /// </summary>
        internal static IntPtr ActiveRoot()
        {
            var foreground = PInvoke.GetForegroundWindow().Value;
            var root = WindowExtensions.GetRootWindow(foreground);
            return foreground != root && !WindowExtensions.IsVisible(foreground) ? IntPtr.Zero : root;
        }

        public static Task<ActivationResult> ActivateTabAsync(IntPtr hwnd, Func<bool> canRetry)
        {
            int version = Interlocked.Increment(ref s_activationVersion);
            return ActivateTabAsync(hwnd, ActiveRoot, TryActivate,
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
            if (ActiveRoot() == hwnd || PInvoke.SetForegroundWindow(new(hwnd)))
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
