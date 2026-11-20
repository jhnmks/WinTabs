using System;
using System.Collections.Generic;

namespace wintabs.Layouts.Tiling
{
    /// <summary>
    /// Hands out the numbers shown on tab group badges. Only groups that contain tabs and are part of a
    /// layout are numbered, and the numbers always run 1, 2, 3, ... without gaps: groups keep their
    /// order of creation, and when one closes, the ones after it move up. The registry is process-wide
    /// so numbers are unique across every display.
    /// </summary>
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
                if (!IsNumbered(group))
                {
                    number = 0;
                }
                else
                {
                    int index = s_order.IndexOf(group);
                    if (index < 0)
                    {
                        s_order.Add(group);
                        index = s_order.Count - 1;
                    }
                    number = index + 1;
                }
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

        private static bool IsNumbered(StackPanelNode group) => group.Children.Count > 0 && group.Desktop != null;

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
