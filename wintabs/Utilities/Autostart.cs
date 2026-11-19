using System;
using Microsoft.Win32;
using System.Threading.Tasks;

using Windows.ApplicationModel;

namespace wintabs.Utilities
{
    internal static class Autostart
    {
        private const string StartupTaskId = "wintabs";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "wintabs";
        private static readonly bool s_isRunningAsUwp = new DesktopBridge.Helpers().IsRunningAsUwp();

        public static async Task<bool> IsEnabledAsync()
        {
            if (s_isRunningAsUwp)
            {
                return await IsEnabledUwpAsync();
            }
            else
            {
                return await IsEnabledLegacyAsync();
            }
        }

        public static async Task<bool> EnableAsync()
        {
            if (s_isRunningAsUwp)
            {
                return await EnableUwpAsync();
            }
            else
            {
                return await EnableLegacyAsync();
            }
        }

        public static async Task<bool> DisableAsync()
        {
            if (s_isRunningAsUwp)
            {
                return await DisableUwpAsync();
            }
            else
            {
                return await DisableLegacyAsync();
            }
        }

        private static Task<bool> IsEnabledLegacyAsync()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return Task.FromResult(key?.GetValue(RunValue) is string);
        }

        private static async Task<bool> IsEnabledUwpAsync()
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return task.State == StartupTaskState.Enabled || task.State == StartupTaskState.EnabledByPolicy;
        }

        private static async Task<bool> EnableLegacyAsync()
        {
            if (Environment.ProcessPath is not string executable)
                return false;

            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(RunValue, $"\"{executable}\"", RegistryValueKind.String);
            return await IsEnabledLegacyAsync();
        }

        private static async Task<bool> EnableUwpAsync()
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (task.State != StartupTaskState.Enabled && task.State != StartupTaskState.EnabledByPolicy)
            {
                var newState = await task.RequestEnableAsync();
                if (newState != StartupTaskState.Enabled)
                {
                    return false;
                }
            }
            return true;
        }

        private static async Task<bool> DisableLegacyAsync()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
            return !await IsEnabledLegacyAsync();
        }

        private static async Task<bool> DisableUwpAsync()
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (task.State == StartupTaskState.Enabled || task.State == StartupTaskState.EnabledByPolicy)
            {
                task.Disable();
                if (task.State != StartupTaskState.Disabled)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
