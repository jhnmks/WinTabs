using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace wintabs.Utilities
{
    internal sealed record RunningApp(string Name, string ProcessName);

    /// <summary>Readable app names ("Task Manager") for process names ("Taskmgr").</summary>
    internal static class AppNames
    {
        /// <summary>The app's name if one of its processes is running, otherwise the process name itself.</summary>
        public static string Describe(string processName)
        {
            var name = ByProcessNameMatcher.Normalize(processName);
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (TryGetDescription(process) is string description)
                        return description;
                }
            }
            return name;
        }

        /// <summary>Apps that currently show a window, one entry per executable, sorted by name.</summary>
        public static IReadOnlyList<RunningApp> GetRunningApps()
        {
            var current = Environment.ProcessId;
            Dictionary<string, RunningApp> apps = new(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == current || process.MainWindowHandle == IntPtr.Zero || apps.ContainsKey(process.ProcessName))
                            continue;
                        apps[process.ProcessName] = new(TryGetDescription(process) ?? process.ProcessName, process.ProcessName);
                    }
                    catch (InvalidOperationException)
                    {
                        // Exited while enumerating.
                    }
                }
            }
            return [.. apps.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)];
        }

        private static string? TryGetDescription(Process process)
        {
            try
            {
                var version = process.MainModule?.FileVersionInfo;
                var name = !string.IsNullOrWhiteSpace(version?.FileDescription) ? version.FileDescription : version?.ProductName;
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Elevated or protected processes do not expose their executable.
                return null;
            }
        }
    }
}
