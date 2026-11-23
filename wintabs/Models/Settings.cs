using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using wintabs.Utilities;

namespace wintabs.Models
{
    public enum AppearanceMode
    {
        System,
        Light,
        Dark,
    }

    public interface ITilingServiceSettings
    {
        int WindowPadding { get; }
        int PanelHeight { get; }

        bool AutoFloatNewWindows { get; }
    }

    public record class Settings : IEquatable<Settings>, ITilingServiceSettings
    {
        public Settings()
        {

        }


        public bool NotifyVirtualDesktopServiceIncompatibility { get; init; } = true;




        public bool AutoFloatNewWindows { get; init; } = false;

        public int WindowPadding { get; init; } = 4;

        public int PanelHeight { get; init; } = 18;

        public int PanelFontSize { get; init; } = 12;

        public string? FocusBorderColor { get; init; }



        public AppearanceMode Appearance { get; init; } = AppearanceMode.System;

        public List<string> ProcessIgnoreList { get; init; } =
        [
            "Taskmgr"
        ];

        // Built-in class rules live in FloatingWindows; this list is only the user's own.
        public List<string> ClassIgnoreList { get; init; } = [];

        public bool MultiMonitorSupport { get; init; } = true;

        public bool SoundOnFailure { get; init; } = true;

    }
}
