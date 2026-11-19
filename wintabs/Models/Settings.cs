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

    [AttributeUsage(AttributeTargets.Field)]
    public class DefaultKeybindingAttribute(params KeyCode[] keys) : Attribute
    {
        public readonly KeyCode[] Keys = keys;
    }

    public record class Settings : IEquatable<Settings>, ITilingServiceSettings
    {
        public Settings()
        {

        }

        [JsonConverter(typeof(Converters.ActivationHotkeyConverter))]
        public ActivationHotkey ActivationHotkey { get; init; } = ActivationHotkey.Default;

        public bool ActivateOnCapsLock { get; init; } = false;


        public bool NotifyVirtualDesktopServiceIncompatibility { get; init; } = true;




        public bool AutoFloatNewWindows { get; init; } = false;

        public int WindowPadding { get; init; } = 4;

        public int PanelHeight { get; init; } = 18;

        public int PanelFontSize { get; init; } = 12;

        public string? FocusBorderColor { get; init; }



        public AppearanceMode Appearance { get; init; } = AppearanceMode.System;

        [JsonConverter(typeof(Converters.KeybindingConverter))]
        public KeybindingDictionary Keybindings { get; init; } = [];

        public List<string> ProcessIgnoreList { get; init; } =
        [
            "Taskmgr"
        ];

        public List<string> ClassIgnoreList { get; init; } =
        [
            "OperationStatusWindow",
            "RAIL_WINDOW",
        ];

        public bool ShowContextHints { get; init; } = true;

        public bool MultiMonitorSupport { get; init; } = true;

        public bool SoundOnFailure { get; init; } = true;

    }
}
