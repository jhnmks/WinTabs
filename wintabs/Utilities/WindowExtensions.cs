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

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        /// <summary>
        /// False once the window has been re-parented into another window (for example a
        /// viewer embedded by a connection manager). Such windows must not get their own tab.
        /// </summary>
        public static bool IsTopLevelWindow(this IWindow window)
        {
            var root = GetAncestor(window.Handle, GA_ROOT);
            return root == window.Handle;
        }

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
