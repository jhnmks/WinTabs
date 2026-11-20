using System;
using System.Threading.Tasks;

using Microsoft.Win32;

namespace wintabs.Utilities
{
    /// <summary>
    /// Starts WinTabs at sign-in through the current user's Run registry key.
    /// </summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "wintabs";

        public static Task<bool> IsEnabledAsync()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return Task.FromResult(key?.GetValue(RunValue) is string);
        }

        public static Task<bool> EnableAsync()
        {
            if (Environment.ProcessPath is not string executable)
                return Task.FromResult(false);

            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                key.SetValue(RunValue, $"\"{executable}\"", RegistryValueKind.String);
            return IsEnabledAsync();
        }

        public static async Task<bool> DisableAsync()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                key?.DeleteValue(RunValue, throwOnMissingValue: false);
            return !await IsEnabledAsync();
        }
    }
}
