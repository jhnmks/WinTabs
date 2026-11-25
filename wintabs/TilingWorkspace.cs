using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using wintabs.Layouts;
using wintabs.Layouts.Tiling;


using WinMan;

namespace wintabs
{

    internal enum TilingError
    {
        MissingTarget,
        MissingAdjacentWindow,
        NoGroupInDirection,
    }

    internal class TilingFailedException(TilingError reason) : InvalidOperationException
    {
        public TilingError FailReason { get; } = reason;
    }

    public class WindowAlreadyRegisteredException : Exception
    {
    }

    internal class DesktopState
    {
        public required DesktopTree DesktopTree { get; init; }
        public TilingNode? FocusedNode { get; set; }
    }

    internal class TilingWorkspaceState
    {
        private readonly Dictionary<IVirtualDesktop, DesktopState> m_states = [];

        public IEnumerable<IVirtualDesktop> Desktops => m_states.Keys;
        public IEnumerable<DesktopState> States => m_states.Values;

        public void AddState(IVirtualDesktop virtualDesktop, DesktopState state)
        {
            m_states.Add(virtualDesktop, state);
        }

        public DesktopState? GetState(IVirtualDesktop virtualDesktop)
        {
            return m_states.TryGetValue(virtualDesktop, out var state) ? state : null;
        }

        public DesktopState? GetState(DesktopTree tree)
        {
            return m_states.Where(x => x.Value.DesktopTree == tree).SingleOrDefault().Value;
        }

        public void RemoveState(IVirtualDesktop virtualDesktop)
        {
            if (!m_states.Remove(virtualDesktop))
            {
                throw new ArgumentException("The specified desktop does not exist!");
            }
        }

        public DesktopState? FindByVdm(IWindow window)
        {
            var desktop = m_states.Keys.FirstOrDefault(x => x.HasWindow(window));
            if (desktop == null)
            {
                return null;
            }
            return m_states[desktop];
        }

        public DesktopState? FindByTree(IWindow window)
        {
            var state = m_states.Values.FirstOrDefault(x => x.DesktopTree.FindNode(window) != null);
            return state;
        }
    }

    internal class TilingWorkspace
    {
        private readonly TilingWorkspaceState m_states = new();
        private readonly Dictionary<IWindow, Rectangle> m_originalPositions = [];

        public IEnumerable<DesktopTree> Trees => m_states.States.Select(x => x.DesktopTree);

        public void RegisterDesktop(IVirtualDesktop virtualDesktop, Rectangle workArea)
        {
            var tree = new DesktopTree
            {
                Root = new StackPanelNode(),
                WorkArea = workArea,
            };
            m_states.AddState(virtualDesktop, new DesktopState
            {
                DesktopTree = tree,
                FocusedNode = null,
            });
        }

        public IEnumerable<IVirtualDesktop> Desktops => m_states.Desktops;

        public void UnregisterDesktop(IVirtualDesktop virtualDesktop)
        {
            var tree = GetTree(virtualDesktop);
            m_states.RemoveState(virtualDesktop);
            foreach (var window in tree!.Root!.Windows)
                m_originalPositions.Remove(window.WindowReference);
            tree.Root = null;
        }

        public WindowNode RegisterWindow(IWindow window)
        {
            var state = GetValidatedState(window);
            var root = state.DesktopTree.Root!;
            // A new window opens beside the focused tab, including inside a named group.
            var group = state.FocusedNode?.Parent as StackPanelNode ?? root.Nodes.OfType<StackPanelNode>().FirstOrDefault(x => !x.IsContainer);
            if (group == null)
            {
                group = new StackPanelNode();
                state.DesktopTree.Root = group;
            }
            return RegisterWindow(window, group);
        }

        public WindowNode RegisterWindow(IWindow window, StackPanelNode parent)
        {
            if (m_states.FindByTree(window) != null)
                throw new WindowAlreadyRegisteredException();

            var newNode = new WindowNode(window);
            var position = window.Position;
            parent.Attach(newNode);
            m_originalPositions[window] = position;
            return newNode;
        }

        private DesktopState GetValidatedState(IWindow window)
        {
            var state = m_states.FindByVdm(window)
                ?? throw new InvalidWindowReferenceException(window.Handle);

            if (state.DesktopTree.FindNode(window) is WindowNode)
                throw new WindowAlreadyRegisteredException();

            return state;
        }

        public void UnregisterWindow(IWindow window)
        {
            var state = m_states.FindByTree(window) ?? throw new ArgumentException(null, nameof(window));
            if (state.FocusedNode is WindowNode node && node.WindowReference == window)
            {
                state.FocusedNode = null;
            }

            var windowNode = state.DesktopTree.FindNode(window)!;
            var group = GetGroup(windowNode)!;
            DetachTab(windowNode);
            if (group.Children.Count == 0)
                RemoveEmptyGroup(group);
            m_originalPositions.Remove(window);
        }

        /// <summary>The group (top-level tab bar) a tab belongs to, also when the tab is inside a container.</summary>
        public static StackPanelNode? GetGroup(TilingNode tab) =>
            tab.Parent is StackPanelNode { IsContainer: true } container ? (StackPanelNode)container.Parent! : tab.Parent as StackPanelNode;

        /// <summary>
        /// Splits <paramref name="tab"/> off into a new group beside its own. A tab that is alone in its
        /// group has nothing to split from, so this refuses and leaves the layout untouched.
        /// </summary>
        public bool SplitTab(WindowNode tab, TilingDirection direction) =>
            GetGroup(tab) is StackPanelNode group && group.Windows.Count() > 1 && SplitTabBeside(tab, group, direction);

        private bool CanSplitTabBeside(WindowNode tab, StackPanelNode target, TilingDirection direction)
        {
            if (GetGroup(tab) is not StackPanelNode source || tab.Desktop == null || tab.Desktop != target.Desktop)
                return false;
            if (source.Windows.Count() < 2)
                return source != target && CanMoveTabGroup(source, target, direction);
            return CanAcceptTab(tab, target, direction);
        }

        private bool SplitTabBeside(WindowNode tab, StackPanelNode target, TilingDirection direction)
        {
            if (!CanSplitTabBeside(tab, target, direction))
                return false;

            var source = GetGroup(tab)!;
            if (source.Windows.Count() < 2)
                return MoveTabGroup(source, target, direction);

            var newGroup = new StackPanelNode { Padding = target.Padding, Spacing = target.Spacing };
            DetachTab(tab);
            InsertGroup(target, newGroup, direction);
            newGroup.Attach(tab);
            SetFocus(tab);
            return true;
        }

        /// <summary>
        /// Takes a tab out of its row. A container left empty disappears with it. The group itself is left even
        /// when empty; the caller decides what happens to it.
        /// </summary>
        private static void DetachTab(WindowNode tab)
        {
            var row = (StackPanelNode)tab.Parent!;
            row.Detach(tab);
            if (row.IsContainer && row.Children.Count == 0)
                row.Parent!.Detach(row);
        }

        /// <summary>
        /// Moves a tab to a new group in the outer row, even if it already belongs to another group.
        /// </summary>
        public StackPanelNode? WrapInContainer(WindowNode tab)
        {
            if (GetGroup(tab) is not { } group || group.Desktop == null)
                return null;
            var container = new StackPanelNode { Padding = group.Padding, Spacing = group.Spacing };
            bool selected = group.SelectedTab == tab;
            DetachTab(tab);
            AttachContainer(group, container);
            container.Attach(tab);
            if (selected)
                group.SelectTab(container);
            return container;
        }

        private static void AttachContainer(StackPanelNode group, StackPanelNode container) =>
            group.Attach(group.Children.TakeWhile(x => x is StackPanelNode).Count(), container);

        /// <summary>Puts a tab into an existing container, at the end of its row.</summary>
        public bool MoveTabToContainer(WindowNode tab, StackPanelNode container, bool keepSourceFocus = false)
        {
            if (!container.IsContainer || tab.Parent == container || tab.Desktop == null || tab.Desktop != container.Desktop)
                return false;
            if (!CanAcceptTab(tab, container, null))
                return false;
            return MoveTabToGroup(tab, container, keepSourceFocus);
        }

        /// <summary>Unpacks the tabs in order before the other regular window tabs.</summary>
        public bool Unpack(StackPanelNode container)
        {
            if (!container.IsContainer)
                return false;
            var group = (StackPanelNode)container.Parent!;
            var selected = group.SelectedChild == container ? container.SelectedTab : null;
            int index = group.Children.TakeWhile(x => x is StackPanelNode).Count();
            foreach (var tab in container.Children.ToArray())
            {
                container.Detach(tab);
                group.Attach(index++, tab);
            }
            group.Detach(container);
            if (selected != null)
                group.SelectTab(selected);
            return true;
        }

        public bool UnpackGroups(IVirtualDesktop desktop)
        {
            if (GetTree(desktop)?.Root is not { } root)
                return false;

            var containers = root.Nodes.OfType<StackPanelNode>().Where(row => row.IsContainer).ToArray();
            // Unpack inserts windows before the regular tabs, so work backwards to keep the groups in order.
            for (int i = containers.Length - 1; i >= 0; i--)
                Unpack(containers[i]);
            return containers.Length != 0;
        }

        public (StackPanelNode Group, TilingDirection? Edge)? GetTabDropTarget(WindowNode tab, Point point, bool dropOnUngrouped = false)
        {
            if (GetGroup(tab) is not StackPanelNode source || tab.Desktop?.Root is not PanelNode root)
                return null;

            var target = FindGroupAt(root, point);
            if (target == null)
                return null;

            if (dropOnUngrouped)
                return tab.Parent != target ? (target, null) : null;

            var edge = GetTabDropEdge(target, point);
            if (edge is TilingDirection direction)
                return CanSplitTabBeside(tab, target, direction) ? (target, edge) : null;

            if (target.SelectedChild is StackPanelNode { IsContainer: true } container)
                return container != tab.Parent && CanAcceptTab(tab, container, null) ? (container, null) : null;

            // Its own group already holds it; only a drop on the group's tab bar takes it out of a container.
            if (target == source)
                return tab.Parent != source && (IsOnTabBar(source, point) || GetUngroupedBarBounds(source).Contains(point)) ? (target, null) : null;
            return (target, null);
        }

        // Header rows are join targets; the window area has three horizontal drop zones.
        public static TilingDirection? GetTabDropEdge(StackPanelNode target, Point point)
        {
            var bounds = GetGroupBounds(target);
            int windowTop = target.SelectedChild is StackPanelNode { IsContainer: true } container
                ? GetContainerBarBounds(container).Bottom
                : Math.Max(GetTabBarBounds(target).Bottom, GetUngroupedBarBounds(target).Bottom);
            if (point.Y < windowTop || bounds.Width <= 0)
                return null;

            double fraction = (double)(point.X - bounds.Left) / bounds.Width;
            return fraction < 0.20 ? TilingDirection.Left : fraction >= 0.80 ? TilingDirection.Right : null;
        }

        /// <summary>The group whose area (tab bar included) contains the point.</summary>
        public static StackPanelNode? FindGroupAt(PanelNode root, Point point) =>
            root.Nodes.OfType<StackPanelNode>().FirstOrDefault(group => !group.IsContainer && group.Children.Count != 0 && GetGroupBounds(group).Contains(point));

        /// <summary>The container whose (inner) tab bar contains the point, if any.</summary>
        public static Rectangle GetUngroupedBarBounds(StackPanelNode group)
        {
            if (!group.HasContainers || group.SelectedChild is not WindowNode)
                return default;
            var outer = GetTabBarBounds(group);
            return new Rectangle(outer.Left, outer.Bottom, outer.Right, outer.Bottom + group.Padding.Top);
        }

        /// <summary>The strip of a container's own tab bar, just above its windows.</summary>
        public static Rectangle GetContainerBarBounds(StackPanelNode container)
        {
            var rect = container.ComputedRectangle;
            return new Rectangle(rect.Left, rect.Top - container.Padding.Top, rect.Right, rect.Top);
        }

        /// <summary>
        /// For dragging an entire tile: within the outer quarter of a group's
        /// window area the drop tiles beside it on that side. On its tab bar or in its middle the drop goes into the
        /// group (or swaps with it, for a group), shown as <c>null</c>.
        /// </summary>
        public static TilingDirection? GetDropEdge(StackPanelNode target, Point point)
        {
            var bounds = GetGroupBounds(target);
            int headerBottom = target.Parent == null ? bounds.Top + target.Padding.Top : target.ComputedRectangle.Top;
            if (point.Y < headerBottom)
                return null;
            double x = (double)(point.X - bounds.Left) / Math.Max(1, bounds.Width);
            double y = (double)(point.Y - headerBottom) / Math.Max(1, bounds.Bottom - headerBottom);
            var edges = new[] { x, 1 - x, y, 1 - y };
            int nearest = Array.IndexOf(edges, edges.Min());
            if (edges[nearest] >= 0.25)
                return null;
            return nearest switch
            {
                0 => TilingDirection.Left,
                1 => TilingDirection.Right,
                2 => TilingDirection.Up,
                _ => TilingDirection.Down,
            };
        }

        /// <summary>Whether the tab can become a new group beside <paramref name="target"/> (or join it, without an edge).</summary>
        public static bool CanAcceptTab(WindowNode tab, StackPanelNode target, TilingDirection? edge)
        {
            if (!target.IsContainer)
                return edge is not TilingDirection direction ||
                    CanSplitGroup(target, direction, new StackPanelNode { Padding = target.Padding, Spacing = target.Spacing }, tab);
            if (edge != null)
                return false;
            tab.Measure();
            var size = target.GetAvailableTabSize();
            return tab.MinSize.X <= size.X && tab.MinSize.Y <= size.Y;
        }

        /// <summary>The strip of the group's tab bar; dropping a group there merges it into this one.</summary>
        public static Rectangle GetTabBarBounds(StackPanelNode group)
        {
            var bounds = GetGroupBounds(group);
            int headerBottom = group.Parent == null ? bounds.Top + group.Padding.Top : group.ComputedRectangle.Top;
            return new Rectangle(bounds.Left, bounds.Top, bounds.Right, Math.Max(bounds.Top + 1, headerBottom));
        }

        public static bool IsOnTabBar(StackPanelNode group, Point point) => GetTabBarBounds(group).Contains(point);

        /// <summary>Whether every tab of <paramref name="group"/> fits in <paramref name="target"/>.</summary>
        public static bool CanMergeInto(StackPanelNode group, StackPanelNode target)
        {
            if (group.IsContainer || target.IsContainer || group == target || group.Children.Count == 0 || target.Children.Count == 0)
                return false;
            group.Measure();
            return FitsGroup(group, GetGroupBounds(target));
        }

        /// <summary>
        /// Moves every tab of <paramref name="source"/> to the end of <paramref name="target"/> (same display), in
        /// order, and removes the emptied group. The tab that was selected in it stays selected and gets focus.
        /// </summary>
        public bool MergeTabGroup(StackPanelNode source, StackPanelNode target)
        {
            if (source.Desktop == null || source.Desktop != target.Desktop || !CanMergeInto(source, target))
                return false;

            var selected = source.SelectedTab;
            foreach (var tab in source.Children.ToArray())
            {
                source.Detach(tab);
                if (tab is StackPanelNode container)
                    AttachContainer(target, container);
                else
                    target.Attach(tab);
            }
            RemoveEmptyGroup(source);
            if (selected != null)
                SetFocus(selected);
            return true;
        }

        /// <summary>
        /// Moves every tab of <paramref name="group"/> to the end of <paramref name="target"/>, a group owned by
        /// another workspace (another display), and removes the emptied group.
        /// </summary>
        public bool MergeTabGroupInto(StackPanelNode group, TilingWorkspace destination, StackPanelNode target, IVirtualDesktop desktop)
        {
            var source = m_states.GetState(desktop);
            var other = destination.m_states.GetState(desktop);
            if (source == null || other == null || source == other || group.Desktop != source.DesktopTree ||
                target.Desktop != other.DesktopTree || !CanMergeInto(group, target))
                return false;

            var selected = group.SelectedTab;
            foreach (var tab in group.Children.ToArray())
            {
                group.Detach(tab);
                if (tab is StackPanelNode container)
                    AttachContainer(target, container);
                else
                    target.Attach(tab);
                foreach (var window in tab.Windows)
                {
                    if (m_originalPositions.Remove(window.WindowReference, out var original))
                        destination.m_originalPositions[window.WindowReference] = original;
                }
            }
            if (group.Parent != null)
                RemoveEmptyGroup(group);
            else
                source.DesktopTree.Root = new StackPanelNode();

            if (source.FocusedNode is { } stale && !source.DesktopTree.Root!.Nodes.Contains(stale))
                source.FocusedNode = null;
            if (selected != null)
                destination.SetFocus(selected);
            return true;
        }

        /// <summary>Whether a group from another display can swap with <paramref name="target"/>, or tile beside it.</summary>
        public static bool CanAcceptGroup(StackPanelNode group, StackPanelNode target, TilingDirection? edge)
        {
            if (edge is TilingDirection direction)
                return CanSplitGroup(target, direction, group);
            group.Measure();
            target.Measure();
            return FitsGroup(group, GetGroupBounds(target)) && FitsGroup(target, GetGroupBounds(group));
        }

        public bool DropTab(WindowNode tab, Point point, bool dropOnUngrouped = false) => GetTabDropTarget(tab, point, dropOnUngrouped) is { } target &&
            (target.Edge is TilingDirection edge ? SplitTabBeside(tab, target.Group, edge)
                : target.Group.IsContainer ? MoveTabToContainer(tab, target.Group) : MoveTabToGroup(tab, target.Group));

        public bool MoveTabToGroup(WindowNode tab, StackPanelNode target, bool keepSourceFocus = false)
        {
            if (GetGroup(tab) is not StackPanelNode source || tab.Parent == target || tab.Desktop == null || tab.Desktop != target.Desktop)
                return false;

            var focused = m_states.GetState(tab.Desktop)?.FocusedNode;
            var replacement = keepSourceFocus && focused == tab ? GetSourceNeighbor(tab) : null;
            DetachTab(tab);
            target.Attach(tab);
            if (source.Children.Count == 0)
                RemoveEmptyGroup(source);
            if (!keepSourceFocus || focused == tab)
                SetFocus(replacement ?? tab);
            return true;
        }

        /// <summary>
        /// Moves the focused tab to the end of the group labelled <paramref name="groupNumber"/> on the same desktop.
        /// </summary>
        public bool MoveFocusedTabToGroup(IVirtualDesktop currentDesktop, int groupNumber)
        {
            if (GetFocus(currentDesktop) is not WindowNode { Parent: StackPanelNode source } tab)
                return false;

            var target = FindGroup(currentDesktop, groupNumber);
            if (target == null)
                return false;

            if (target == source)
            {
                int index = source.IndexOf(tab);
                if (index < source.Children.Count - 1)
                    source.Move(index, source.Children.Count - 1);
                return true;
            }

            return target.IsContainer ? MoveTabToContainer(tab, target, keepSourceFocus: true)
                : MoveTabToGroup(tab, target, keepSourceFocus: true);
        }

        public StackPanelNode? FindGroup(IVirtualDesktop desktop, int groupNumber) =>
            groupNumber > 0 ? GetTree(desktop)?.Root?.Nodes.OfType<StackPanelNode>().FirstOrDefault(group => group.GroupNumber == groupNumber) : null;

        /// <summary>
        /// Moves the focused tab to the end of a group owned by another workspace (another display).
        /// </summary>
        public WindowNode? TransferFocusedTab(IVirtualDesktop desktop, TilingWorkspace destination, StackPanelNode target) =>
            GetFocus(desktop) is WindowNode tab ? TransferTab(tab, destination, target, null, desktop, keepSourceFocus: true) : null;

        private static WindowNode? GetSourceNeighbor(WindowNode tab)
        {
            var row = (StackPanelNode)tab.Parent!;
            int index = row.IndexOf(tab);
            // Only windows in this row count; a named group in the outer row is not a window tab.
            return row.Children.Take(index).OfType<WindowNode>().LastOrDefault()
                ?? row.Children.Skip(index + 1).OfType<WindowNode>().FirstOrDefault();
        }

        /// <summary>
        /// Moves a tab to a workspace of another display: into <paramref name="target"/>, into a new group beside it
        /// (with an edge), or, without a target, into the empty display. Never loses the tab when that fails.
        /// </summary>
        public WindowNode? TransferTab(WindowNode tab, TilingWorkspace destination, StackPanelNode? target, TilingDirection? edge, IVirtualDesktop desktop, bool keepSourceFocus = false)
        {
            var tree = destination.GetTree(desktop);
            if (tab.Parent is not StackPanelNode || tab.Desktop == null || tab.Desktop != GetTree(desktop) || tree?.Root is not PanelNode root || tree == tab.Desktop)
                return null;
            if (target == null ? root is not StackPanelNode { Children.Count: 0 } : target.Desktop != tree || !CanAcceptTab(tab, target, edge))
                return null;

            var window = tab.WindowReference;
            if (destination.HasWindow(window))
                return null;

            var original = m_originalPositions[window];
            var group = target ?? (StackPanelNode)root;
            if (target != null && edge is TilingDirection direction)
            {
                group = new StackPanelNode { Padding = target.Padding, Spacing = target.Spacing };
                InsertGroup(target, group, direction);
            }
            var replacement = keepSourceFocus ? GetSourceNeighbor(tab) : null;
            UnregisterWindow(window);
            group.Attach(tab);
            destination.m_originalPositions[window] = original;
            if (replacement != null)
                SetFocus(replacement);
            else
                destination.SetFocus(tab);
            return tab;
        }

        public bool TransferTabGroup(StackPanelNode group, TilingWorkspace destination, IVirtualDesktop desktop)
        {
            var source = m_states.GetState(desktop);
            var target = destination.m_states.GetState(desktop);
            if (source == null || target?.DesktopTree.Root is not PanelNode targetRoot || source == target ||
                group.Desktop != source.DesktopTree || group.Children.Count == 0 || targetRoot.Children.Count != 0)
                return false;

            var windows = group.Windows.ToArray();
            if (windows.Any(node => destination.HasWindow(node.WindowReference)))
                return false;

            var focus = source.FocusedNode;
            bool movesFocus = focus != null && group.Nodes.Contains(focus);
            if (group.Parent is PanelNode parent)
            {
                parent.Detach(group);
                CollapseSplit(parent);
            }
            else
            {
                source.DesktopTree.Root = new StackPanelNode();
            }
            target.DesktopTree.Root = group;
            foreach (var node in windows)
            {
                if (m_originalPositions.Remove(node.WindowReference, out var original))
                    destination.m_originalPositions[node.WindowReference] = original;
            }
            if (movesFocus)
                source.FocusedNode = null;
            target.FocusedNode = movesFocus ? focus : windows[0];
            return true;
        }

        /// <summary>
        /// Drops a tab group onto a group owned by another workspace (another display): swaps the two groups, or
        /// with an edge, tiles it beside the target. Refuses when the windows would not fit.
        /// </summary>
        public bool MoveTabGroupTo(StackPanelNode group, TilingWorkspace destination, StackPanelNode target, TilingDirection? edge, IVirtualDesktop desktop)
        {
            var source = m_states.GetState(desktop);
            var other = destination.m_states.GetState(desktop);
            if (source == null || other == null || source == other || group.Desktop != source.DesktopTree ||
                target.Desktop != other.DesktopTree || group.Children.Count == 0 || target.Children.Count == 0)
                return false;

            group.Measure();
            target.Measure();
            var focus = source.FocusedNode;
            bool movesFocus = focus != null && group.Nodes.Contains(focus);
            var moving = group.Windows.ToArray();
            var returning = edge == null ? target.Windows.ToArray() : [];

            if (!CanAcceptGroup(group, target, edge))
                return false;
            if (edge is TilingDirection direction)
            {
                if (group.Parent is PanelNode parent)
                {
                    parent.Detach(group);
                    CollapseSplit(parent);
                }
                else
                {
                    source.DesktopTree.Root = new StackPanelNode();
                }
                InsertGroup(target, group, direction);
            }
            else
            {
                // Empty placeholders hold both places while the groups change layouts.
                var groupSlot = new StackPanelNode();
                var targetSlot = new StackPanelNode();
                ReplaceGroup(source.DesktopTree, group, groupSlot);
                ReplaceGroup(other.DesktopTree, target, targetSlot);
                ReplaceGroup(source.DesktopTree, groupSlot, target);
                ReplaceGroup(other.DesktopTree, targetSlot, group);
            }

            foreach (var node in moving)
            {
                if (m_originalPositions.Remove(node.WindowReference, out var original))
                    destination.m_originalPositions[node.WindowReference] = original;
            }
            foreach (var node in returning)
            {
                if (destination.m_originalPositions.Remove(node.WindowReference, out var original))
                    m_originalPositions[node.WindowReference] = original;
            }
            other.FocusedNode = movesFocus ? focus : group.SelectedTab;
            if (source.FocusedNode is { } stale && !source.DesktopTree.Root!.Nodes.Contains(stale))
                source.FocusedNode = edge == null ? target.SelectedTab : null;
            return true;
        }

        private static void ReplaceGroup(DesktopTree tree, StackPanelNode group, StackPanelNode replacement)
        {
            if (group.Parent is PanelNode parent)
                parent.Replace(group, replacement);
            else
                tree.Root = replacement;
        }

        /// <summary>Whether <see cref="MoveTabGroup"/> would change anything; used to show only drops that work.</summary>
        public bool CanMoveTabGroup(StackPanelNode source, StackPanelNode target, TilingDirection? edge)
        {
            if (source == target || source.Parent == null || source.Desktop == null || source.Desktop != target.Desktop)
                return false;
            if (edge is not TilingDirection direction)
            {
                source.Measure();
                target.Measure();
                return FitsGroup(source, GetGroupBounds(target)) && FitsGroup(target, GetGroupBounds(source));
            }
            if (GetSiblingMoveIndex(source, target, direction) is int to)
                return to != source.Parent.IndexOf(source);
            return CanSplitGroup(target, direction, source);
        }

        // Tiling beside a sibling in the same row or column only reorders the row; returns the new index then.
        private static int? GetSiblingMoveIndex(StackPanelNode source, StackPanelNode target, TilingDirection direction)
        {
            if (source.Parent != target.Parent || source.Parent is not SplitPanelNode siblings ||
                siblings.Orientation != GetOrientation(direction))
                return null;
            int from = siblings.IndexOf(source);
            int to = siblings.IndexOf(target) + (IsBefore(direction) ? 0 : 1);
            return from < to ? to - 1 : to;
        }

        public bool MoveTabGroup(StackPanelNode source, StackPanelNode target, TilingDirection? edge)
        {
            if (!CanMoveTabGroup(source, target, edge))
                return false;

            if (edge is not TilingDirection direction)
            {
                source.Swap(target);
                return true;
            }

            if (GetSiblingMoveIndex(source, target, direction) is int to)
            {
                source.Parent!.Move(source.Parent.IndexOf(source), to);
                return true;
            }

            var oldParent = source.Parent!;
            oldParent.Detach(source);
            InsertGroup(target, source, direction);
            CollapseSplit(oldParent);
            return true;
        }

        public static Rectangle GetGroupBounds(StackPanelNode group)
        {
            var rect = group.ComputedRectangle;
            return group.Parent == null ? rect : new Rectangle(rect.Left - group.Padding.Left,
                rect.Top - group.Padding.Top, rect.Right + group.Padding.Right, rect.Bottom + group.Padding.Bottom);
        }

        private static bool CanSplitGroup(StackPanelNode target, TilingDirection direction, StackPanelNode incoming, WindowNode? tab = null)
        {
            target.Measure();
            incoming.Measure();
            var bounds = GetGroupBounds(target);
            var existing = target.MinSize;
            var added = incoming.MinSize;
            if (tab != null)
            {
                tab.Measure();
                if (GetGroup(tab) == target)
                    existing = MeasureWithoutTab(target, tab);
                added = new Point(tab.MinSize.X + incoming.Spacing + added.X, tab.MinSize.Y + incoming.Spacing + added.Y);
            }
            return GetOrientation(direction) == PanelOrientation.Horizontal
                ? existing.X + added.X <= bounds.Width && Math.Max(existing.Y, added.Y) <= bounds.Height
                : existing.Y + added.Y <= bounds.Height && Math.Max(existing.X, added.X) <= bounds.Width;
        }

        private static Point MeasureWithoutTab(StackPanelNode row, WindowNode excluded)
        {
            int width = 0, height = 0;
            bool hasContainers = row.Children.OfType<StackPanelNode>()
                .Any(container => container.Children.Any(child => child != excluded));
            foreach (var child in row.Children)
            {
                if (child == excluded || child is StackPanelNode empty && !empty.Children.Any(x => x != excluded))
                    continue;
                var size = child is StackPanelNode container ? MeasureWithoutTab(container, excluded) : child.MinSize;
                if (child is WindowNode)
                    size = new Point(size.X + row.Spacing, size.Y + row.Spacing + (hasContainers ? row.Padding.Top : 0));
                width = Math.Max(width, size.X);
                height = Math.Max(height, size.Y);
            }
            return new Point(width + row.Padding.Left + row.Padding.Right, height + row.Padding.Top + row.Padding.Bottom);
        }

        private static bool FitsGroup(StackPanelNode group, Rectangle bounds) =>
            group.MinSize.X <= bounds.Width && group.MinSize.Y <= bounds.Height;

        private static PanelOrientation GetOrientation(TilingDirection direction) =>
            direction is TilingDirection.Left or TilingDirection.Right ? PanelOrientation.Horizontal : PanelOrientation.Vertical;

        private static bool IsBefore(TilingDirection direction) => direction is TilingDirection.Left or TilingDirection.Up;

        private static void InsertGroup(StackPanelNode target, StackPanelNode group, TilingDirection direction)
        {
            var split = new SplitPanelNode { Orientation = GetOrientation(direction) };
            if (target.Parent == null)
            {
                var tree = target.Desktop!;
                tree.Root = split;
                split.Attach(target);
            }
            else
            {
                target.Embed(split);
            }
            split.Attach(IsBefore(direction) ? 0 : 1, group);
        }

        private static void RemoveEmptyGroup(PanelNode group)
        {
            if (group.Parent is PanelNode parent)
            {
                parent.Detach(group);
                CollapseSplit(parent);
            }
        }

        private static void CollapseSplit(PanelNode panel)
        {
            if (panel is not SplitPanelNode || panel.Children.Count != 1)
                return;

            if (panel.Parent != null)
            {
                panel.CollapseIfSingle();
                return;
            }

            var tree = panel.Desktop!;
            var remaining = (PanelNode)panel.Children[0];
            panel.Detach(remaining);
            tree.Root = remaining;
        }

        public Rectangle GetOriginalPosition(IWindow window)
        {
            return m_originalPositions[window];
        }

        public DesktopTree? GetTree(IVirtualDesktop desktop)
        {
            return m_states.GetState(desktop)?.DesktopTree;
        }

        public bool HasWindow(IWindow window)
        {
            var state = m_states.FindByTree(window);
            if (state == null)
            {
                return false;
            }
            return state.DesktopTree.FindNode(window) != null;
        }

        public WindowNode? FindWindow(IWindow window)
        {
            var state = m_states.FindByTree(window);
            if (state == null)
            {
                return null;
            }
            return state.DesktopTree.FindNode(window);
        }

        public void ResizeNode(TilingNode node, Rectangle newPosition, Rectangle oldPosition)
        {
            if (newPosition.Width != oldPosition.Width)
            {
                GridLikeNode? p = node.Ancestors
                    .Select(x => x as GridLikeNode)
                    .Where(x => x != null)
                    .FirstOrDefault(x => x!.CanResizeInOrientation(PanelOrientation.Horizontal));

                if (p != null)
                {
                    var leftResizeAmount = Math.Abs(newPosition.Left - oldPosition.Left);
                    var rightResizeAmount = Math.Abs(newPosition.Right - oldPosition.Right);
                    GrowDirection direction = leftResizeAmount < rightResizeAmount
                        ? GrowDirection.TowardsEnd
                        : leftResizeAmount > rightResizeAmount
                            ? GrowDirection.TowardsStart
                            : GrowDirection.Both;
                    var child = p.Children.First(x => x.Nodes.Contains(node));
                    var childIndex = p.IndexOf(child);
                    var sizeDelta = newPosition.Width - oldPosition.Width;

                    if (direction == GrowDirection.TowardsStart && childIndex == 0 && child.GetAdjacentNode(TilingDirection.Left) is TilingNode leftNode)
                    {
                        var leftNodePosition = leftNode.ComputedRectangle;
                        ResizeNode(leftNode, new Rectangle(leftNodePosition.Left, leftNodePosition.Top, leftNodePosition.Right - sizeDelta, leftNodePosition.Bottom), leftNodePosition);
                    }
                    else if (direction == GrowDirection.TowardsEnd && childIndex == p.Children.Count - 1 && child.GetAdjacentNode(TilingDirection.Right) is TilingNode rightNode)
                    {
                        var rightNodePosition = rightNode.ComputedRectangle;
                        ResizeNode(rightNode, new Rectangle(rightNodePosition.Left + sizeDelta, rightNodePosition.Top, rightNodePosition.Right, rightNodePosition.Bottom), rightNodePosition);
                    }

                    p.ResizeBy(child, sizeDelta, direction);
                }
            }

            if (newPosition.Height != oldPosition.Height)
            {
                GridLikeNode? p = node.Ancestors
                    .Select(x => x as GridLikeNode)
                    .Where(x => x != null)
                    .FirstOrDefault(x => x!.CanResizeInOrientation(PanelOrientation.Vertical));

                if (p != null)
                {
                    var topResizeAmount = Math.Abs(newPosition.Top - oldPosition.Top);
                    var bottomResizeAmount = Math.Abs(newPosition.Bottom - oldPosition.Bottom);
                    GrowDirection direction = topResizeAmount < bottomResizeAmount
                        ? GrowDirection.TowardsEnd
                        : topResizeAmount > bottomResizeAmount
                            ? GrowDirection.TowardsStart
                            : GrowDirection.Both;
                    var child = p.Children.First(x => x.Nodes.Contains(node));
                    var childIndex = p.IndexOf(child);
                    var sizeDelta = newPosition.Height - oldPosition.Height;

                    if (direction == GrowDirection.TowardsStart && childIndex == 0 && child.GetAdjacentNode(TilingDirection.Up) is TilingNode topNode)
                    {
                        var topNodePosition = topNode.ComputedRectangle;
                        ResizeNode(topNode, new Rectangle(topNodePosition.Left, topNodePosition.Top, topNodePosition.Right, topNodePosition.Bottom - sizeDelta), topNodePosition);
                    }
                    else if (direction == GrowDirection.TowardsEnd && childIndex == p.Children.Count - 1 && child.GetAdjacentNode(TilingDirection.Down) is TilingNode bottomNode)
                    {
                        var bottomNodePosition = bottomNode.ComputedRectangle;
                        ResizeNode(bottomNode, new Rectangle(bottomNodePosition.Left, bottomNodePosition.Top + sizeDelta, bottomNodePosition.Right, bottomNodePosition.Bottom), bottomNodePosition);
                    }

                    p.ResizeBy(child, sizeDelta, direction);
                }
            }
        }

        public TilingNode? GetFocus(IVirtualDesktop currentDesktop)
        {
            if (m_states.GetState(currentDesktop) is not DesktopState state)
                throw new ArgumentException("Desktop not registered with backend!");

            return state.FocusedNode;
        }

        /// <summary>
        /// The tab <paramref name="offset"/> positions away from the focused tab, wrapping around its group.
        /// </summary>
        public WindowNode? GetCycledTab(IVirtualDesktop currentDesktop, int offset)
        {
            if (GetFocus(currentDesktop) is not WindowNode { Parent: StackPanelNode group } focused)
                return null;

            var tabs = group.Children.OfType<WindowNode>().ToList();
            if (tabs.Count < 2)
                return null;

            int index = tabs.IndexOf(focused);
            return tabs[((index + offset) % tabs.Count + tabs.Count) % tabs.Count];
        }

        /// <summary>
        /// Moves the focused tab one position within its group. Stops at the first and last positions.
        /// </summary>
        public bool MoveFocusedTab(IVirtualDesktop currentDesktop, int offset)
        {
            if (GetFocus(currentDesktop) is not WindowNode { Parent: StackPanelNode group } focused)
                return false;

            int from = group.IndexOf(focused);
            int to = from + offset;
            if (to < 0 || to >= group.Children.Count || group.Children[to] is not WindowNode)
                return false;

            group.Move(from, to);
            return true;
        }


        public WindowNode? GetCycledGroupTab(IVirtualDesktop desktop, int offset)
        {
            if (GetFocus(desktop) is not WindowNode focused || GetGroup(focused) is not { HasContainers: true } group)
                return null;

            var rows = GetOuterRows(group);
            if (rows.Count < 2)
                return null;

            int from = rows.IndexOf((StackPanelNode)focused.Parent!);
            int to = ((from + offset) % rows.Count + rows.Count) % rows.Count;
            return rows[to] == group ? group.UngroupedSelectedTab : rows[to].SelectedTab;
        }

        private static List<StackPanelNode> GetOuterRows(StackPanelNode group)
        {
            var rows = group.Children.OfType<StackPanelNode>().ToList();
            if (group.UngroupedSelectedTab != null)
                rows.Insert(Math.Min(group.UngroupedIndex, rows.Count), group);
            return rows;
        }

        public bool MoveFocusedGroup(IVirtualDesktop desktop, int offset)
        {
            if (GetFocus(desktop) is not WindowNode focused || GetGroup(focused) is not { HasContainers: true } group)
                return false;

            var rows = GetOuterRows(group);
            var source = (StackPanelNode)focused.Parent!;
            int from = rows.IndexOf(source);
            int to = from + offset;
            if (to < 0 || to >= rows.Count || to == from)
                return false;

            var target = rows[to];
            if (source == group)
                group.UngroupedIndex = to;
            else if (target == group)
                group.UngroupedIndex = from;
            else
                group.Move(group.IndexOf(source), group.IndexOf(target));
            return true;
        }

        public void SetFocus(TilingNode node)
        {
            Debug.Assert(node.Parent != null);
            if (m_states.GetState(node.Desktop!) is not DesktopState state)
                throw new ArgumentException("Desktop not registered with backend!");

            state.FocusedNode = node;
            // Select it in its row, and its container in the group's row.
            for (TilingNode child = node; child.Parent is StackPanelNode row; child = row)
                row.SelectTab(child);
        }

        public void SetFocus(IWindow window)
        {
            var state = m_states.FindByTree(window) ?? throw new ArgumentException("Window not registered with backend!");
            var node = state.DesktopTree.FindNode(window);
            Debug.Assert(node != null);

            SetFocus(node);
        }
    }
}
