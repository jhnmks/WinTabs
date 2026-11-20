using System.Collections.Generic;

namespace wintabs.Models
{
    public sealed record ShortcutInfo(string Section, IReadOnlyList<string> Keys, string Title, string Description);

    /// <summary>
    /// The single list of keyboard shortcuts shown to the user (cheat sheet and Settings).
    /// Keep in step with <see cref="Utilities.TabShortcuts"/>, which does the actual key handling.
    /// </summary>
    public static class ShortcutCatalog
    {
        public static IReadOnlyList<ShortcutInfo> All { get; } =
        [
            new("Tab groups", ["Ctrl", "Win", "← ↑ → ↓"], "Focus a neighbouring group",
                "Jumps to the closest tab group on screen in the direction of the arrow."),
            new("Tab groups", ["Win", "Shift", "← ↑ → ↓"], "Tile the tab beside its group",
                "Splits the active tab off into a new group on that side."),
            new("Tabs", ["Ctrl", "← →"], "Previous or next tab",
                "Cycles through the tabs of the focused group. Wraps around at the ends."),
            new("Tabs", ["Ctrl", "Shift", "← →"], "Move the tab left or right",
                "Shifts the active tab one position within its group."),
            new("Tabs", ["Ctrl", "Shift", "1 – 9"], "Send the tab to a numbered group",
                "Moves the active tab to the end of the group with that number, shown in the red badge on each tab bar."),
            new("WinTabs", ["Ctrl", "Win", "/"], "Show this cheat sheet",
                "Opens or closes a quick reference of these shortcuts."),
        ];
    }
}
