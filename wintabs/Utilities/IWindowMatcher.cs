using System;
using System.Text.RegularExpressions;

using WinMan;

namespace wintabs.Utilities
{
    internal interface IWindowMatcher
    {
        bool Matches(IWindow window);
    }

    /// <summary>
    /// "Always float" rule for an app, identified by its executable name without ".exe" (for example "Taskmgr").
    /// Matched exactly, ignoring case.
    /// </summary>
    internal class ByProcessNameMatcher(string processName) : IWindowMatcher
    {
        public string ProcessName { get; } = Normalize(processName);

        /// <summary>"Taskmgr.exe " and "Taskmgr" name the same app.</summary>
        public static string Normalize(string processName)
        {
            var name = processName.Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }

        public bool Matches(IWindow window) =>
            ProcessName.Length != 0 && string.Equals(window.GetCachedProcessName(), ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Advanced rule: floats windows by their internal window class. Exact match, or a regular expression.
    /// </summary>
    internal class ByClassNameMatcher(string className) : IWindowMatcher
    {
        public string ClassName { get; } = className;

        public bool Matches(IWindow window) =>
            window is WinMan.Windows.Win32Window w && IsMatch(w.ClassName, ClassName);

        private static bool IsMatch(string input, string pattern)
        {
            if (pattern.Length == 0)
                return false;
            if (string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
            }
            catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
