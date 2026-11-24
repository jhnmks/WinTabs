using System;
using System.Collections.Generic;
using System.Linq;

namespace wintabs.Layouts.Tiling
{
    /// <summary>Numbers nonempty tab rows by screen, then nested groups, standalone groups, and Ungrouped.</summary>
    public static class GroupNumbers
    {
        private static readonly object s_lock = new();
        private static readonly List<StackPanelNode> s_order = [];

        /// <summary>
        /// Raised when existing groups were renumbered (a group closed), so every display can refresh its badges.
        /// </summary>
        public static event EventHandler? Renumbered;

        /// <summary>
        /// The group's number. Groups without tabs, or not in a layout, have no number (0).
        /// </summary>
        public static int Get(StackPanelNode group)
        {
            bool renumbered;
            int number;
            lock (s_lock)
            {
                renumbered = PruneStale();
                if (IsNumbered(group) && !s_order.Contains(group))
                    s_order.Add(group);
                var ordered = s_order.OrderBy(x => ScreenBounds(x).Left)
                    .ThenBy(x => ScreenBounds(x).Top)
                    .ThenBy(x => x.IsContainer ? 0 : x.HasContainers ? 2 : 1)
                    .ToArray();
                if (!s_order.SequenceEqual(ordered))
                {
                    s_order.Clear();
                    s_order.AddRange(ordered);
                    renumbered = true;
                }
                number = s_order.IndexOf(group) + 1;
            }

            // Raised outside the lock so handlers may read numbers freely.
            if (renumbered)
                Renumbered?.Invoke(null, EventArgs.Empty);
            return number;
        }

        /// <summary>For tests.</summary>
        public static void Reset()
        {
            lock (s_lock)
            {
                s_order.Clear();
            }
        }

        internal static void Register(StackPanelNode group)
        {
            lock (s_lock)
                if (IsNumbered(group) && !s_order.Contains(group))
                    s_order.Add(group);
        }

        private static WinMan.Rectangle ScreenBounds(StackPanelNode group) =>
            group.Desktop is { } tree ? tree.DisplayBounds ?? tree.WorkArea : default;

        private static bool IsNumbered(StackPanelNode group) => group.Desktop != null &&
            (group.IsContainer ? group.Children.Count != 0 : group.UngroupedSelectedTab != null);

        // Drops groups that emptied or left their layout (closed, merged, display removed). Returns true if
        // any group that stays numbered moved to a lower number as a result.
        private static bool PruneStale()
        {
            int firstRemoved = s_order.FindIndex(group => !IsNumbered(group));
            if (firstRemoved < 0)
                return false;

            s_order.RemoveAll(group => !IsNumbered(group));
            return firstRemoved < s_order.Count;
        }
    }
}
