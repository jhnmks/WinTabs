using System.Collections.Generic;
using System.Linq;

namespace wintabs.Models
{
    public sealed record ShortcutInfo(string Section, IReadOnlyList<string> Keys, string Title, string Description)
    {
        /// <summary>True for the first shortcut of its section; used to draw dividers only between rows.</summary>
        public bool IsFirstInSection { get; init; }
    }

    /// <summary>
    /// The single list of keyboard shortcuts shown to the user (cheat sheet and Settings), in display order.
    /// Keep in step with <see cref="Utilities.TabShortcuts"/>, which does the actual key handling.
    /// </summary>
    public static class ShortcutCatalog
    {
        public static IReadOnlyList<ShortcutInfo> All { get; } = MarkSections(
        [
            new("Tabs", ["Ctrl", "← →"], "Previous or next tab",
                "Cycles through the tabs of the focused group. Wraps around at the ends."),
            new("Tabs", ["Ctrl", "Shift", "← →"], "Move the tab left or right",
                "Shifts the active tab one position within its group."),
            new("Tabs", ["Ctrl", "Shift", "1 – 9"], "Send the tab to a numbered group",
                "Sends the active tab to the group with that badge number. If a tab remains in the source row, focus stays there."),
            new("Tab groups", ["Ctrl", "Win", "← →"], "Previous or next group",
                "Switches groups in the outer row, including Ungrouped. Wraps around at the ends."),
            new("Tab groups", ["Ctrl", "Win", "G"], "Put the active tab in a new group",
                "Creates a new group in the outer row for the active window."),
            new("Tab groups", ["Ctrl", "Win", "U"], "Unpack all groups",
                "Returns windows in named groups to regular tabs on the current desktop across all managed displays."),
            new("Tab groups", ["Ctrl", "Win", "Shift", "← →"], "Move the group left or right",
                "Moves the active group one position in the outer row, including Ungrouped."),
            new("Tab groups", ["Ctrl", "Alt", "← ↑ → ↓"], "Tile the tab beside its group",
                "Splits the active tab off into a new group on that side."),
            new("WinTabs", ["Ctrl", "Alt", "/"], "Show this cheat sheet",
                "Opens or closes a quick reference of these shortcuts."),
        ]);

        private static IReadOnlyList<ShortcutInfo> MarkSections(IReadOnlyList<ShortcutInfo> shortcuts) =>
            shortcuts.Select((s, i) => s with { IsFirstInSection = i == 0 || shortcuts[i - 1].Section != s.Section }).ToList();
    }
}
