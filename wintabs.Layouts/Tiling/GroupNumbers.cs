using System;
using System.Collections.Generic;
using System.Linq;

namespace wintabs.Layouts.Tiling
{
    /// <summary>Numbers tab rows in each display’s sequence, with nested groups first and Ungrouped last.</summary>
    public static class GroupNumbers
    {
        private static readonly object s_lock = new();
        private static readonly List<StackPanelNode> s_order = [];
        private static Dictionary<StackPanelNode, int> s_numbers = [];

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
                s_order.RemoveAll(x => !IsNumbered(x));
                if (IsNumbered(group) && !s_order.Contains(group))
                    s_order.Add(group);

                var next = new Dictionary<StackPanelNode, int>();
                foreach (var display in s_order.GroupBy(x => x.Desktop!.GroupNumberLane))
                {
                    int position = 0;
                    foreach (var row in display.OrderBy(x => x.IsContainer ? 0 : x.HasContainers ? 2 : 1))
                        next.Add(row, display.Key + 1 + position++ * row.Desktop!.GroupNumberStride);
                }
                renumbered = next.Any(x => s_numbers.TryGetValue(x.Key, out int previous) && previous != x.Value);
                s_numbers = next;
                number = next.GetValueOrDefault(group);
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
                s_numbers.Clear();
            }
        }

        internal static void Register(StackPanelNode group)
        {
            lock (s_lock)
                if (IsNumbered(group) && !s_order.Contains(group))
                    s_order.Add(group);
        }

        private static bool IsNumbered(StackPanelNode group) => group.Desktop != null &&
            (group.IsContainer ? group.Children.Count != 0 : group.UngroupedSelectedTab != null);
    }
}
