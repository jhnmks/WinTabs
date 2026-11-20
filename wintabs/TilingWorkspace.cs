using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Xml.Linq;

using wintabs.Layouts;
using wintabs.Layouts.Tiling;
using wintabs.Utilities;

using Windows.Devices.Enumeration;

using WinMan;

namespace wintabs
{

    internal enum TilingError
    {
        Failed,
        MissingTarget,
        InvalidTarget,
        MissingAdjacentWindow,
        CausesRecursiveNesting,
        ModifiesTopLevelPanel,
        NoValidPlacementExists,
        TargetCannotFit,
        PullsBeyondTopLevelPanel,
        NestingInStackPanel,
    }

    internal class TilingFailedException : InvalidOperationException
    {
        public TilingError FailReason { get; } = TilingError.Failed;

        public TilingFailedException(TilingError reason = TilingError.Failed)
        {
            FailReason = reason;
        }

        public TilingFailedException(string? message, TilingError reason = TilingError.Failed) : base(message)
        {
            FailReason = reason;
        }

        public TilingFailedException(string? message, Exception? innerException, TilingError reason = TilingError.Failed) : base(message, innerException)
        {
            FailReason = reason;
        }
    }

    public class NoValidPlacementExistsException : Exception
    {
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

    internal class TilingWorkspace(bool tabsOnly = false)
    {
        private readonly TilingWorkspaceState m_states = new();
        private readonly Dictionary<IWindow, Rectangle> m_originalPositions = [];

        public IEnumerable<DesktopTree> Trees => m_states.States.Select(x => x.DesktopTree);

        public bool AutoCollapse { get; set; } = false;


        public PanelNode CreateRoot(PanelOrientation orientation)
        {
            return tabsOnly ? new StackPanelNode() : new SplitPanelNode { Orientation = orientation };
        }

        public void RegisterDesktop(IVirtualDesktop virtualDesktop, Rectangle workArea, PanelOrientation orientation)
        {
            var tree = new DesktopTree
            {
                Root = CreateRoot(orientation),
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
        }

        public WindowNode RegisterWindow(IWindow window, int maxTreeWidth = 100)
        {
            var state = GetValidatedState(window);
            var focusedNode = state.FocusedNode;
            if (tabsOnly)
            {
                var root = state.DesktopTree.Root!;
                var group = focusedNode?.Parent as StackPanelNode ?? root.Nodes.OfType<StackPanelNode>().FirstOrDefault();
                if (group == null)
                {
                    group = new StackPanelNode();
                    state.DesktopTree.Root = group;
                }
                return RegisterWindow(window, group);
            }

            var parent = ResolveParent(state, focusedNode);
            parent = ResolveParentWithWidthConstraint(parent, focusedNode, window, maxTreeWidth);
            return RegisterWindow(window, parent, focusedNode as WindowNode);
        }

        public WindowNode RegisterWindow(IWindow window, PanelNode parent, WindowNode? anchor = null)
        {
            if (m_states.FindByTree(window) != null)
                throw new WindowAlreadyRegisteredException();

            var newNode = new WindowNode(window);
            var position = window.Position;
            AttachToParent(newNode, parent);
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

        private static PanelNode ResolveParent(DesktopState state, TilingNode? focusedNode)
            => focusedNode is WindowNode focusedWindow
                ? focusedWindow.Parent ?? state.DesktopTree.Root!
                : state.DesktopTree.Root!;

        private PanelNode ResolveParentWithWidthConstraint(
            PanelNode parent, TilingNode? focusedNode, IWindow window, int maxTreeWidth)
        {
            if (!IsAtMaxWidth(parent, maxTreeWidth) || parent is not SplitPanelNode parentSplit)
                return parent;

            var nodeToSplit = SelectNodeToSplit(parent, focusedNode);

            if (nodeToSplit is WindowNode)
                TrySplitNode(nodeToSplit, parentSplit, window);

            return nodeToSplit.Parent!;
        }

        private static bool IsAtMaxWidth(PanelNode parent, int maxTreeWidth)
            => parent.Children.Count >= maxTreeWidth;

        private static TilingNode SelectNodeToSplit(PanelNode parent, TilingNode? focusedNode)
            => parent.Children.Contains(focusedNode) ? focusedNode! : parent.Children.Last();

        private void TrySplitNode(TilingNode nodeToSplit, SplitPanelNode parentSplit, IWindow window)
        {
            WrapInSplitPanel(nodeToSplit, vertical: parentSplit.Orientation == PanelOrientation.Horizontal);
            ArrangeWithFallback(nodeToSplit);

            if (!CanFitLossy(nodeToSplit.Parent!, window))
                nodeToSplit.Parent!.CollapseIfSingle();
        }

        private static void ArrangeWithFallback(TilingNode node)
        {
            try
            {
                node.Desktop!.Arrange();
            }
            catch (UnsatisfiableFlexConstraintsException)
            {
                node.Parent!.CollapseIfSingle();
            }
        }

        private void AttachToParent(WindowNode newNode, PanelNode parent)
        {
            if (parent is not StackPanelNode && !CanFitLossy(parent, newNode))
                throw new NoValidPlacementExistsException();

            parent.Attach(newNode);
        }

        private static bool CanFitLossy(PanelNode parent, IWindow window)
        {
            if (parent.ComputedRectangle == default)
            {
                return true;
            }

            var node = new WindowNode(window);
            node.Measure();
            return CanFitLossy(parent, node);
        }

        private static bool CanFitLossy(PanelNode parent, TilingNode node)
        {
            if (parent.ComputedRectangle == default)
            {
                return true;
            }

            var minSize = node.MinSize;
            var maxSize = parent.GetMaxSizeForInsert(node);

            return minSize.X <= maxSize.X && minSize.Y <= maxSize.Y;
        }

        public void UnregisterWindow(IWindow window)
        {
            var state = m_states.FindByTree(window) ?? throw new ArgumentException(null, nameof(window));
            if (state.FocusedNode is WindowNode node && node.WindowReference == window)
            {
                state.FocusedNode = null;
            }

            var windowNode = state.DesktopTree.FindNode(window)!;
            if (tabsOnly)
            {
                var group = windowNode.Parent!;
                group.Detach(windowNode);
                if (group.Children.Count == 0)
                    RemoveEmptyGroup(group);
            }
            else
            {
                windowNode.Remove(cleanup: true, collapse: AutoCollapse);
            }
            m_originalPositions.Remove(window);
        }

        /// <summary>
        /// Splits <paramref name="tab"/> off into a new group beside its own. A tab that is alone in its
        /// group has nothing to split from, so this refuses and leaves the layout untouched.
        /// </summary>
        public bool SplitTab(WindowNode tab, TilingDirection direction) =>
            tab.Parent is StackPanelNode group && group.Children.Count > 1 && SplitTabBeside(tab, group, direction);

        private bool SplitTabBeside(WindowNode tab, StackPanelNode target, TilingDirection direction)
        {
            if (tab.Parent is not StackPanelNode source || tab.Desktop == null || tab.Desktop != target.Desktop)
                return false;

            if (source.Children.Count < 2)
                return source != target && MoveTabGroup(source, target, direction);

            var newGroup = new StackPanelNode { Padding = target.Padding, Spacing = target.Spacing };
            if (!CanSplitGroup(target, direction, newGroup, tab))
                return false;

            source.Detach(tab);
            InsertGroup(target, newGroup, direction);
            newGroup.Attach(tab);
            SetFocus(tab);
            return true;
        }

        public (StackPanelNode Group, TilingDirection? Edge)? GetTabDropTarget(WindowNode tab, Point point)
        {
            if (tab.Parent is not StackPanelNode source || tab.Desktop?.Root is not PanelNode root)
                return null;

            var target = root.Nodes.OfType<StackPanelNode>()
                .FirstOrDefault(group => GetGroupBounds(group).Contains(point));
            if (target == null)
                return null;

            var bounds = GetGroupBounds(target);
            int headerBottom = target.Parent == null ? bounds.Top + target.Padding.Top : target.ComputedRectangle.Top;
            // Other tab bars are always merge targets, even near a split boundary.
            if (target != source && point.Y < headerBottom)
                return (target, null);

            int edgeWidth = Math.Min(64, bounds.Width / 4);
            TilingDirection? edge = point.X - bounds.Left < edgeWidth ? TilingDirection.Left
                : bounds.Right - point.X <= edgeWidth ? TilingDirection.Right : null;
            return target == source && (edge == null || source.Children.Count < 2) ? null : (target, edge);
        }

        public bool DropTab(WindowNode tab, Point point) => GetTabDropTarget(tab, point) is { } target &&
            (target.Edge is TilingDirection edge ? SplitTabBeside(tab, target.Group, edge) : MoveTabToGroup(tab, target.Group));

        public bool MoveTabToGroup(WindowNode tab, StackPanelNode target)
        {
            if (tab.Parent is not StackPanelNode source || source == target || tab.Desktop == null || tab.Desktop != target.Desktop)
                return false;

            source.Detach(tab);
            target.Attach(tab);
            if (source.Children.Count == 0)
                RemoveEmptyGroup(source);
            SetFocus(tab);
            return true;
        }

        /// <summary>
        /// Moves the focused tab to the end of the group labelled <paramref name="groupNumber"/> on the same desktop.
        /// </summary>
        public bool MoveFocusedTabToGroup(IVirtualDesktop currentDesktop, int groupNumber)
        {
            if (GetFocus(currentDesktop) is not WindowNode { Parent: StackPanelNode source } tab || source.Desktop?.Root is not PanelNode root)
                return false;

            var target = root.Nodes.OfType<StackPanelNode>().FirstOrDefault(group => group.GroupNumber == groupNumber);
            if (target == null)
                return false;

            if (target == source)
            {
                int index = source.IndexOf(tab);
                if (index == source.Children.Count - 1)
                    return false;
                source.Move(index, source.Children.Count - 1);
                return true;
            }

            return MoveTabToGroup(tab, target);
        }

        public StackPanelNode? FindGroup(IVirtualDesktop desktop, int groupNumber) =>
            GetTree(desktop)?.Root?.Nodes.OfType<StackPanelNode>().FirstOrDefault(group => group.GroupNumber == groupNumber);

        /// <summary>
        /// Moves the focused tab to the end of a group owned by another workspace (another display).
        /// </summary>
        public WindowNode? TransferFocusedTab(IVirtualDesktop desktop, TilingWorkspace destination, StackPanelNode target)
        {
            if (GetFocus(desktop) is not WindowNode { Parent: StackPanelNode source } tab || target.Desktop != destination.GetTree(desktop))
                return null;

            var window = tab.WindowReference;
            if (destination.HasWindow(window))
                return null;

            var original = m_originalPositions.GetValueOrDefault(window, window.Position);
            int index = source.IndexOf(tab);
            UnregisterWindow(window);
            try
            {
                var moved = destination.RegisterWindow(window, target);
                destination.m_originalPositions[window] = original;
                destination.SetFocus(moved);
                return moved;
            }
            catch (Exception)
            {
                // Never lose the tab: put it back where it was.
                if (source.Desktop != null)
                {
                    var restored = new WindowNode(window);
                    source.Attach(Math.Min(index, source.Children.Count), restored);
                    m_originalPositions[window] = original;
                }
                throw;
            }
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

        public bool MoveTabGroup(StackPanelNode source, StackPanelNode target, TilingDirection? edge)
        {
            if (source == target || source.Parent == null || source.Desktop == null || source.Desktop != target.Desktop)
                return false;

            if (edge is not TilingDirection direction)
            {
                source.Measure();
                target.Measure();
                if (!FitsGroup(source, GetGroupBounds(target)) || !FitsGroup(target, GetGroupBounds(source)))
                    return false;

                source.Swap(target);
                return true;
            }

            if (source.Parent == target.Parent && source.Parent is SplitPanelNode siblings &&
                siblings.Orientation == GetOrientation(direction))
            {
                int from = siblings.IndexOf(source);
                int to = siblings.IndexOf(target) + (IsBefore(direction) ? 0 : 1);
                if (from < to)
                    to--;
                if (from == to)
                    return false;
                siblings.Move(from, to);
                return true;
            }

            if (!CanSplitGroup(target, direction, source))
                return false;

            var oldParent = source.Parent;
            source.Remove();
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
                if (tab.Parent == target)
                {
                    var remaining = target.Children.Where(child => child != tab).ToArray();
                    existing = new Point(remaining.Max(child => child.MinSize.X) + target.Spacing + target.Padding.Left + target.Padding.Right,
                        remaining.Max(child => child.MinSize.Y) + target.Spacing + target.Padding.Top + target.Padding.Bottom);
                }
                added = new Point(tab.MinSize.X + incoming.Spacing + added.X, tab.MinSize.Y + incoming.Spacing + added.Y);
            }
            return GetOrientation(direction) == PanelOrientation.Horizontal
                ? existing.X + added.X <= bounds.Width && Math.Max(existing.Y, added.Y) <= bounds.Height
                : existing.Y + added.Y <= bounds.Height && Math.Max(existing.X, added.X) <= bounds.Width;
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

        public void MoveNode(TilingNode node, Point pt, bool allowNesting = true)
        {
            if (node.Parent == null)
                throw new ArgumentException($"Node cannot be a top-level node!", nameof(node));

            if (node.Desktop == null)
                throw new ArgumentException($"Node must be registered with the backend!", nameof(node));

            var nodeAtPoint = node.Desktop.Root!.Windows
                .Where(x => x != node)
                .FirstOrDefault<TilingNode>(x => x.ComputedRectangle.Contains(pt)) ?? node.Desktop.Root!.Nodes
                    .OfType<PanelNode>()
                    .Where(x => x != node)
                    .FirstOrDefault(x => Rectangle.OffsetAndSize(
                        x.ComputedRectangle.Left - x.Padding.Left,
                        x.ComputedRectangle.Top - x.Padding.Top,
                        x.ComputedRectangle.Width + x.Padding.Left + x.Padding.Right,
                        x.Padding.Top).Contains(pt));
            if (nodeAtPoint == null || nodeAtPoint.Parent == null)
                return;

            if (nodeAtPoint.PathToRoot.Contains(node))
                throw new TilingFailedException(TilingError.CausesRecursiveNesting);

            if (nodeAtPoint.PathToRoot.OfType<StackPanelNode>().Any() && node is not WindowNode)
                throw new TilingFailedException(TilingError.NestingInStackPanel);

            if (allowNesting)
            {
                if (node.Parent != nodeAtPoint.Parent)
                {
                    // Node moved over another node that NOT a sibling
                    var oldParent = node.Parent;
                    try
                    {
                        if (!MoveNodeTest(node, nodeAtPoint.Parent, pt))
                        {
                            return;
                        }
                        node.Parent.Detach(node);
                        var insertionIndex = FindInsertionIndex(nodeAtPoint, pt);
                        nodeAtPoint.Parent.Attach(insertionIndex, node);
                        oldParent.Cleanup(collapse: AutoCollapse);
                    }
                    catch (UnsatisfiableFlexConstraintsException)
                    {
                        throw new TilingFailedException(TilingError.TargetCannotFit);
                    }
                }
                else if (node.Parent is not StackPanelNode) // Do not rearrange items in stack panel
                {
                    try
                    {
                        var newPosition = TransferSize(node.ComputedRectangle, nodeAtPoint.ComputedRectangle);
                        if (newPosition.Contains(pt))
                        {
                            // Node moved over another node that IS a sibling
                            var nodeIndex = node.Parent.Children.IndexOf(node);
                            var targetIndex = node.Parent.Children.IndexOf(nodeAtPoint);

                            node.Parent.Move(nodeIndex, targetIndex);
                        }
                    }
                    catch (UnsatisfiableFlexConstraintsException)
                    {
                        throw new TilingFailedException(TilingError.Failed);
                    }
                }
            }
            else if (node.Parent != nodeAtPoint)
            {
                try
                {
                    node.Swap(nodeAtPoint);
                }
                catch (UnsatisfiableFlexConstraintsException)
                {
                    throw new TilingFailedException(TilingError.Failed);
                }
            }
        }

        private static int FindInsertionIndex(TilingNode nodeAtPoint, Point pt)
        {
            Debug.Assert(nodeAtPoint.Parent != null);
            var insertionIndex = nodeAtPoint.Parent!.IndexOf(nodeAtPoint);
            if (nodeAtPoint.Parent is GridLikeNode grid)
            {
                if (grid.CanResizeInOrientation(PanelOrientation.Horizontal))
                {
                    if (nodeAtPoint.ComputedRectangle.Left + nodeAtPoint.ComputedRectangle.Width / 2 < pt.X)
                    {
                        // Right half of the window in a horizontal panel
                        return insertionIndex + 1;
                    }
                }
                else if (nodeAtPoint.ComputedRectangle.Top + nodeAtPoint.ComputedRectangle.Height / 2 < pt.Y)
                {
                    // Lower half of the window in a vertical panel
                    return insertionIndex + 1;
                }
            }
            else if (nodeAtPoint.Parent is StackPanelNode stack)
            {
                return stack.Children.Count;
            }
            return insertionIndex;
        }

        internal void WrapInSplitPanel(TilingNode node, bool vertical)
        {
            var isOnlyChild = node.Parent?.Parent != null && node.Parent.Children.Count == 1;
            if (!isOnlyChild && node.Ancestors.OfType<StackPanelNode>().Any())
            {
                throw new TilingFailedException(TilingError.NestingInStackPanel);
            }

            if (node.Parent == null)
                throw new TilingFailedException(TilingError.ModifiesTopLevelPanel);
            // Parent is not the top-level panel and this is the only child
            if (isOnlyChild)
            {
                SwapPanels(node.Parent, new SplitPanelNode
                {
                    Orientation = vertical ? PanelOrientation.Vertical : PanelOrientation.Horizontal,
                });
            }
            else
            {
                node.Embed(new SplitPanelNode
                {
                    Orientation = vertical ? PanelOrientation.Vertical : PanelOrientation.Horizontal,
                });
            }
        }

        internal void WrapInStackPanel(TilingNode node)
        {
            var isOnlyChild = node.Parent?.Parent != null && node.Parent.Children.Count == 1;

            if (!isOnlyChild && node.Ancestors.OfType<StackPanelNode>().Any())
            {
                throw new TilingFailedException(TilingError.NestingInStackPanel);
            }

            if (!isOnlyChild && node.Nodes.Where(x => x is not WindowNode).Any())
            {
                throw new TilingFailedException(TilingError.NestingInStackPanel);
            }

            if (node.Parent == null)
                throw new TilingFailedException(TilingError.ModifiesTopLevelPanel);

            if (isOnlyChild)
            {
                SwapPanels(node.Parent, new StackPanelNode());
            }
            else
            {
                node.Embed(new StackPanelNode());
            }
        }

        public void MoveBefore(TilingNode node, TilingNode nodeBefore)
        {
            MoveTo(node, nodeBefore, beforeAnchor: true);
        }

        public void MoveAfter(TilingNode node, TilingNode nodeAfter)
        {
            MoveTo(node, nodeAfter, beforeAnchor: false);
        }

        private void MoveTo(TilingNode node, TilingNode nodeAnchor, bool beforeAnchor)
        {
            if (node.Parent == null)
                throw new ArgumentException($"Node cannot be a top-level node!", nameof(node));

            if (node.Desktop == null)
                throw new ArgumentException($"Node must be registered with the backend!", nameof(node));

            if (nodeAnchor.Parent == null)
                throw new ArgumentException($"Node cannot be a top-level node!", nameof(nodeAnchor));

            if (nodeAnchor.Desktop == null)
                throw new ArgumentException($"Node must be registered with the backend!", nameof(nodeAnchor));

            if (node.Parent == nodeAnchor.Parent)
                throw new ArgumentException($"Nodes must have different parents!", nameof(nodeAnchor));

            var destination = nodeAnchor.Parent;
            var index = destination.IndexOf(nodeAnchor);
            var oldParent = node.Parent;
            oldParent.Detach(node);
            destination.Attach(beforeAnchor ? index : index + 1, node);
            if (tabsOnly && oldParent is StackPanelNode)
            {
                if (oldParent.Children.Count == 0)
                    RemoveEmptyGroup(oldParent);
            }
            else
            {
                oldParent.Cleanup(collapse: AutoCollapse);
            }
        }

        private bool MoveNodeTest(TilingNode node, PanelNode newParentNode, Point pt)
        {
            Debug.Assert(node.Desktop != null);
            Debug.Assert(node.Parent != null);
            Debug.Assert(newParentNode.Desktop != null);
            Debug.Assert(newParentNode.Desktop != null);
            Debug.Assert(node.Desktop == newParentNode.Desktop);

            var rootClone = (PanelNode)node.Desktop.Root!.Clone();

            var nodeClone = rootClone.Nodes.First(x => x.GenerationID == node.GenerationID);
            var newParentClone = (PanelNode)rootClone.Nodes.First(x => x.GenerationID == newParentNode.GenerationID);
            var testTree = new DesktopTree
            {
                Root = rootClone,
                WorkArea = node.Desktop.WorkArea,
            };

            var nodeCloneParent = nodeClone.Parent!;
            var newParentIsAncestor = nodeCloneParent.Ancestors.Contains(newParentClone);

            nodeCloneParent.Detach(nodeClone);
            nodeCloneParent.Cleanup(collapse: AutoCollapse);

            if (newParentClone.Desktop == null && newParentIsAncestor)
            {
                // The new parent node got completely detached because it was an ancestor
                // of the existing node and apparently it was a change of 1-child panels.
                return true;
            }

            newParentClone.Attach(nodeClone);
            testTree.Measure();
            testTree.Arrange();

            return newParentClone.ComputedRectangle.Contains(pt);
        }

        private static Rectangle TransferSize(Rectangle a, Rectangle b)
        {
            var newCenter = b.Center;
            var width = a.Width;
            var height = a.Height;

            return new Rectangle(newCenter.X - width / 2, newCenter.Y - height / 2, newCenter.X + width / 2, newCenter.Y + height / 2);
        }

        public void MoveWindow(IWindow window, Point pt, bool allowNesting)
        {
            var state = m_states.FindByTree(window) ?? throw new ArgumentException($"Window must be registered with the backend!", nameof(window));
            var sourceNode = state.DesktopTree.FindNode(window) ?? throw new ArgumentException($"Window must be registered with the backend!", nameof(window));
            MoveNode(sourceNode, pt, allowNesting);
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
            if (to < 0 || to >= group.Children.Count)
                return false;

            group.Move(from, to);
            return true;
        }


        public void SetFocus(TilingNode node)
        {
            Debug.Assert(node.Parent != null);
            if (m_states.GetState(node.Desktop!) is not DesktopState state)
                throw new ArgumentException("Desktop not registered with backend!");

            state.FocusedNode = node;
            if (node is WindowNode tab && tab.Parent is StackPanelNode group)
                group.SelectTab(tab);
        }

        public void SetFocus(IWindow window)
        {
            var state = m_states.FindByTree(window) ?? throw new ArgumentException("Window not registered with backend!");
            var node = state.DesktopTree.FindNode(window);
            Debug.Assert(node != null);

            SetFocus(node);
        }

        public void SwapPanels(PanelNode panel, PanelNode newPanel)
        {
            if (panel.Parent == null)
                throw new TilingFailedException(TilingError.ModifiesTopLevelPanel);

            var grandparent = panel.Parent;

            grandparent.Attach(newPanel);
            panel.Swap(newPanel);

            var children = new List<TilingNode>(panel.Children);
            foreach (var node in children)
            {
                panel.Detach(node);
                newPanel.Attach(node);
            }

            panel.Cleanup(collapse: AutoCollapse);
        }

        bool CanFit(PanelNode parent, TilingNode child)
        {
            if (child.PathToRoot.Contains(parent))
            {
                try
                {
                    _ = MoveNodeTest(child, parent, new Point());
                    return true;
                }
                catch (UnsatisfiableFlexConstraintsException)
                {
                    return false;
                }
#if !DEBUG
                catch (Exception)
                {
                    return false;
                }
#endif
            }
            else
            {
                return CanFitLossy(parent, child);
            }
        }

        public void PullUp(TilingNode node)
        {
            if (node.Parent == null)
                throw new TilingFailedException(TilingError.InvalidTarget);

            if (node.Parent.Parent == null)
                throw new TilingFailedException(TilingError.PullsBeyondTopLevelPanel);

            // First grandparent that we can fit in
            var grandparent = node.Parent.PathToRoot.Skip(1).OfType<PanelNode>().FirstOrDefault(x => CanFit(x, node)) ?? throw new TilingFailedException(TilingError.TargetCannotFit);
            var oldParent = node.Parent;
            var index = grandparent.IndexOf(node.Parent);

            node.Parent.Detach(node);
            grandparent.Attach(index, node);

            oldParent.Cleanup(collapse: AutoCollapse);
        }
    }
}
