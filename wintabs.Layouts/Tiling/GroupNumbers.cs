using System.Collections.Generic;
using System.Linq;

namespace wintabs.Layouts.Tiling
{
    /// <summary>
    /// Hands out the numbers shown on tab group badges. Only groups that contain tabs and are part of a
    /// layout hold a number, and a new group always takes the lowest number not in use, so the visible
    /// groups stay 1, 2, 3, ... The registry is process-wide so numbers are unique across every display.
    /// </summary>
    public static class GroupNumbers
    {
        private static readonly object s_lock = new();
        private static readonly Dictionary<StackPanelNode, int> s_assigned = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The group's number, assigning the lowest free one on first use. Groups without tabs, or not in a
        /// layout, have no number (0).
        /// </summary>
        public static int Get(StackPanelNode group)
        {
            lock (s_lock)
            {
                PruneStale();
                if (!IsNumbered(group))
                    return 0;
                if (s_assigned.TryGetValue(group, out int number))
                    return number;

                var used = s_assigned.Values.ToHashSet();
                number = 1;
                while (used.Contains(number))
                    number++;
                s_assigned[group] = number;
                return number;
            }
        }

        /// <summary>For tests.</summary>
        public static void Reset()
        {
            lock (s_lock)
            {
                s_assigned.Clear();
            }
        }

        private static bool IsNumbered(StackPanelNode group) => group.Children.Count > 0 && group.Desktop != null;

        // Groups that emptied, or were dropped from their layout (closed, merged, display removed), give
        // their number back.
        private static void PruneStale()
        {
            List<StackPanelNode>? stale = null;
            foreach (var group in s_assigned.Keys)
            {
                if (!IsNumbered(group))
                    (stale ??= []).Add(group);
            }
            if (stale == null)
                return;
            foreach (var group in stale)
                s_assigned.Remove(group);
        }
    }
}
