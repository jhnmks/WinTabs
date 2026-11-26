using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using WinMan;
using wintabs.DllImports;
using System.Diagnostics;
using WinMan.Windows;

namespace wintabs.Utilities
{
    internal static class WindowExtensions
    {
        private class ProcessInfo(int id, string name)
        {
            public int ID = id;
            public string Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        private static readonly ConditionalWeakTable<IWindow, ProcessInfo> m_processCache = [];
        private static readonly ConditionalWeakTable<IWindow, string> m_appNames = [];

        private const uint GA_ROOT = 2;
        private const uint GW_OWNER = 4;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_APPWINDOW = 0x00040000;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>
        /// False once the window has been re-parented into another window (for example a
        /// viewer embedded by a connection manager). Such windows must not get their own tab.
        /// </summary>
        public static bool IsTopLevelWindow(this IWindow window) => GetRootWindow(window.Handle) == window.Handle;

        internal static IntPtr GetRootWindow(IntPtr hwnd) => hwnd == IntPtr.Zero ? hwnd : GetAncestor(hwnd, GA_ROOT);

        /// <summary>
        /// True for dialogs and other secondary windows that belong to a visible window, such as a viewer's
        /// options dialog. Windows keeps them above their owner, so as tabs they would hide their owner's tab.
        /// Like the taskbar, an explicit WS_EX_APPWINDOW still counts as a main window, and an owner that is
        /// never shown (used by some frameworks for every main window) does not count. An owner embedded in
        /// another app counts through that app's window, even while the embedded owner itself is hidden.
        /// </summary>
        public static bool IsOwnedByVisibleWindow(this IWindow window)
        {
            var owner = GetWindow(window.Handle, GW_OWNER);
            return owner != IntPtr.Zero && IsWindowVisible(GetRootWindow(owner)) &&
                (GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_APPWINDOW) == 0;
        }

        internal static bool IsVisible(IntPtr hwnd) => IsWindowVisible(hwnd);

        public static string GetCachedProcessName(this IWindow window)
        {
            return GetCachedProcessInfo(window).name;
        }

        public static string GetCachedAppName(this IWindow window) => m_appNames.GetValue(window, ResolveAppName);

        private static string ResolveAppName(IWindow window)
        {
            var (id, name) = GetCachedProcessInfo(window);
            if (id == 0)
                return "App";


            try
            {
                using var process = Process.GetProcessById(id);
                var version = process.MainModule?.FileVersionInfo;
                if (version != null)
                {
                    var displayName = !string.IsNullOrWhiteSpace(version.FileDescription)
                        ? version.FileDescription : version.ProductName;
                    if (!string.IsNullOrWhiteSpace(displayName))
                        return displayName.Trim();
                }
            }
            catch (Exception e) when (e is ExternalException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // Protected or exited processes may not expose executable metadata.
            }

            return name;
        }

        public static string DebugString(this IWindow window)
        {
            var (id, name) = GetCachedProcessInfo(window);
            return $"{window.Handle:X8}={name}({id})";
        }

        private static (int id, string name) GetCachedProcessInfo(IWindow window)
        {
            var info = GetCachedProcessInfoInternal(window);
            return (info.ID, info.Name);
        }

        private static ProcessInfo GetCachedProcessInfoInternal(IWindow window)
        {
            ProcessInfo? info;
            if (m_processCache.TryGetValue(window, out info!))
            {
                return info;
            }

            try
            {
                var process = window.GetProcess();
                info = new(process.Id, process.ProcessName);
                if (info.Name == "ApplicationFrameHost" && window is Win32Window win32Window && win32Window.ClassName == "ApplicationFrameWindow")
                {
                    HWND hwndChild = PInvoke.FindWindowEx(new(window.Handle), new HWND(), "Windows.UI.Core.CoreWindow", null);
                    if (hwndChild.Value != IntPtr.Zero)
                    {
                        info = GetCachedProcessInfoInternal(window.Workspace.UnsafeCreateFromHandle(hwndChild.Value));
                    }
                }
            }
            catch (InvalidWindowReferenceException)
            {
                info = new(0, "Invalid handle");
            }
            catch (ExternalException)
            {
                info = new(0, "Inaccessible process");
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                info = new(0, "Dead process");
            }
            m_processCache.AddOrUpdate(window, info);
            return info;
        }

        public static object GetMetadata(this IWindow window)
        {
            return new
            {
                window.Handle,
                window.IsAlive,
                window.IsFocused,
                window.IsTopmost,
                window.MaxSize,
                window.MinSize,
                window.Position,
                window.State,
                Permissions = new
                {
                    window.CanClose,
                    window.CanMaximize,
                    window.CanMinimize,
                    window.CanMove,
                    window.CanReorder,
                    window.CanResize,
                },
            };
        }
    }
}
