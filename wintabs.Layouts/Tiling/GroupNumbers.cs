using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace wintabs.Layouts.Tiling
{
    /// <summary>Numbers tab rows in each display’s sequence, with nested groups first and Ungrouped last.</summary>
    public static class GroupNumbers
    {
        private sealed record Order(long Value);
        private sealed record Snapshot(int Lane, int Stride, (StackPanelNode Group, int Priority)[] Rows);
        private static readonly object s_lock = new();
        private static readonly Dictionary<DesktopTree, Snapshot> s_layouts = [];
        private static readonly ConditionalWeakTable<StackPanelNode, Order> s_order = new();
        private static Dictionary<StackPanelNode, int> s_numbers = [];
        private static long s_nextOrder;
        private static bool s_dirty;

        public static event EventHandler? Renumbered;

        // Called by the layout writer after a mutation, while it still owns the layout lock.
        internal static void Publish(DesktopTree tree)
        {
            var rows = tree.Root?.Nodes.OfType<StackPanelNode>()
                .Where(group => group.Children.Any(child => child is WindowNode))
                .Select(group => (group, group.IsContainer ? 0 : group.HasContainers ? 2 : 1)).ToArray() ?? [];
            lock (s_lock)
            {
                foreach (var (row, _) in rows)
                    s_order.GetValue(row, _ => new Order(s_nextOrder++));
                if (rows.Length == 0)
                    s_layouts.Remove(tree);
                else
                    s_layouts[tree] = new Snapshot(tree.GroupNumberLane, tree.GroupNumberStride, rows);
                s_dirty = true;
            }
        }

        public static int Get(StackPanelNode group)
        {
            bool renumbered = false;
            int number;
            lock (s_lock)
            {
                if (s_dirty)
                {
                    var next = new Dictionary<StackPanelNode, int>();
                    foreach (var display in s_layouts.Values.GroupBy(layout => layout.Lane))
                    {
                        int position = 0;
                        foreach (var row in display.SelectMany(layout => layout.Rows.Select(row => (row.Group, row.Priority, layout.Stride)))
                            .OrderBy(row => row.Priority).ThenBy(row => s_order.GetValue(row.Group, _ => new Order(s_nextOrder++)).Value))
                        {
                            next[row.Group] = display.Key + 1 + position++ * row.Stride;
                        }
                    }
                    renumbered = s_numbers.Any(entry => next.GetValueOrDefault(entry.Key) != entry.Value);
                    s_numbers = next;
                    s_dirty = false;
                }
                number = s_numbers.GetValueOrDefault(group);
            }

            if (renumbered)
                Renumbered?.Invoke(null, EventArgs.Empty);
            return number;
        }

        /// <summary>For tests.</summary>
        public static void Reset()
        {
            lock (s_lock)
            {
                s_layouts.Clear();
                s_order.Clear();
                s_numbers.Clear();
                s_nextOrder = 0;
                s_dirty = false;
            }
        }
    }
}
