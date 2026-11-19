using System;
using System.Windows;

using Microsoft.Win32;

using wintabs.Models;

namespace wintabs.Utilities
{
    /// <summary>
    /// Applies the native WPF Fluent theme (System, Light or Dark) and reports the effective mode.
    /// </summary>
    internal static class AppTheme
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private static AppearanceMode s_mode = AppearanceMode.System;

        static AppTheme()
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && s_mode == AppearanceMode.System)
                    Changed?.Invoke(null, EventArgs.Empty);
            };
        }

        public static event EventHandler? Changed;

        public static bool IsDark => s_mode switch
        {
            AppearanceMode.Light => false,
            AppearanceMode.Dark => true,
            _ => !IsSystemLight(),
        };

        public static void Apply(AppearanceMode mode)
        {
            s_mode = mode;
#pragma warning disable WPF0001
            Application.Current.ThemeMode = mode switch
            {
                AppearanceMode.Light => ThemeMode.Light,
                AppearanceMode.Dark => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
#pragma warning restore WPF0001
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static bool IsSystemLight()
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
    }
}
