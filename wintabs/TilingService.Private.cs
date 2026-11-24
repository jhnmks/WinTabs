using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using wintabs.Utilities;

using WinMan;
using wintabs.Layouts.Tiling;
using wintabs.Layouts;
using wintabs.ViewModels;

namespace wintabs
{
    internal partial class TilingService
    {
        private void RestoreOriginalLayout()
        {
            using (m_backendLock.EnterScope())
            {
                foreach (var desktop in m_workspace.VirtualDesktopManager.Desktops)
                {
                    try
                    {
                        var tree = m_backend.GetTree(desktop);
                        if (tree == null)
                            continue;

                        foreach (var window in tree.Root!.Windows)
                        {
                            var originalPosition = m_backend.GetOriginalPosition(window.WindowReference);
                            try
                            {
                                window.WindowReference.SetPosition(originalPosition);
                            }
                            catch (InvalidWindowReferenceException)
                            {
                                continue;
                            }
                            catch (InvalidOperationException) when (window.WindowReference.State != WindowState.Restored)
                            {
                                continue;
                            }
                        }
                    }
                    catch (KeyNotFoundException)
                    {
                        continue;
                    }
                    catch (InvalidOperationException e)
                    {
                        m_logger.Warning(e, "Exception thrown while restoring the original window layout!");
                    }
                }
            }
        }


        private async Task UpdateLayoutAsync()
        {
            if (!Active || m_workspacePaused)
                return;

            IVirtualDesktop desktop = m_workspace.VirtualDesktopManager.CurrentDesktop;

            List<TilingNode> snapshot;
            IReadOnlyCollection<TilingNode> focusedPath;
            TilingNode? focusedNode;

            using (m_backendLock.EnterScope())
            {
                var tree = m_backend.GetTree(desktop);
                if (tree?.Root == null)
                    return;

                tree.WorkArea = GetLayoutWorkArea();
                if (tree.WorkArea.Width <= 0 || tree.WorkArea.Height <= GetPanelPaddingRect().Top)
                    return;
                foreach (var container in tree.Root.Nodes.OfType<StackPanelNode>().Where(x => x.IsContainer))
                {
                    container.Padding = GetPanelPaddingRect();
                    container.Spacing = GetPanelSpacing();
                }
                if (tree.Root is SplitPanelNode)
                    tree.Measure();
                tree.Arrange();

                snapshot = tree.Root!.Children.Count == 0 ? [] : tree.Root.Nodes.ToList();
                // The tab shown is the one whose window is in front; in a container, the container is shown.
                var groups = snapshot.OfType<StackPanelNode>().Where(group => !group.IsContainer && group.Windows.Skip(1).Any()).ToArray();
                if (groups.Length != 0)
                {
                    var zOrder = m_workspace.CreateSnapshotZOrderComparer();
                    foreach (var group in groups)
                    {
                        var front = group.Windows.OrderBy(tab => tab.WindowReference, zOrder).FirstOrDefault();
                        for (TilingNode? child = front; child?.Parent is StackPanelNode row; child = row == group ? null : row)
                            row.SelectTab(child);
                    }
                }
                focusedNode = m_backend.GetFocus(desktop);
                focusedPath = (IReadOnlyCollection<TilingNode>?)focusedNode?.PathToRoot?.ToList() ?? [];
            }

            async ValueTask RepositionAsync()
            {
                try
                {
                    Freeze();
                    IList<WindowNode> snapshotWindows;
                    using (m_ignoreRepositionSetLock.EnterScope())
                    {
                        snapshotWindows = snapshot.OfType<WindowNode>().Where(x => !m_ignoreRepositionSet.ContainsKey(x.WindowReference)).ToList();
                    }

                    await TransitionTargetGroup.PerformTransitionAsync(CalculateRepositionTargets(snapshotWindows));
                }
                finally
                {
                    Unfreeze();
                }
            }

            var foreground = m_workspace.FocusedWindow;
            var activeTab = foreground == null ? null : snapshot.OfType<WindowNode>()
                .FirstOrDefault(tab => Equals(tab.WindowReference, foreground));

            var repositionTask = RepositionAsync();

            m_gui.UpdateOverlay(snapshot, focusedPath, activeTab);

            await repositionTask;
        }

        private List<TransitionTarget> CalculateRepositionTargets(IEnumerable<WindowNode> snapshot)
        {
            var targets = new List<TransitionTarget>();
            foreach (var window in snapshot)
            {
                try
                {
                    if (window.WindowReference.State != WindowState.Restored)
                        continue;

                    var currentPosition = window.WindowReference.Position;
                    if (!window.WindowReference.CanResize)
                    {
                        var targetRect = CenterFixedSizeWindow(window.ComputedRectangle, currentPosition.Size);
                        if (targetRect == currentPosition)
                        {
                            continue;
                        }
                        targets.Add(new TransitionTarget(window.WindowReference, currentPosition, targetRect));
                    }
                    else
                    {
                        m_logger.Debug("Updating position of window {Window}", window.WindowReference.DebugString());
                        var rect = window.ComputedRectangle;
                        var frame = window.WindowReference.FrameMargins;
                        var adjustedRect = new Rectangle(
                            left: rect.Left - frame.Left,
                            top: rect.Top - frame.Top,
                            right: rect.Right + frame.Right,
                            bottom: rect.Bottom + frame.Bottom);

                        if (adjustedRect == currentPosition)
                        {
                            continue;
                        }

                        targets.Add(new TransitionTarget(window.WindowReference, currentPosition, adjustedRect));

                        var minSize = window.WindowReference.MinSize;
                        if (minSize.HasValue)
                        {
                            if (minSize.Value.X > adjustedRect.Width)
                            {
                                m_logger.Warning("New width for {Window} is smaller than the value reported by WM_GETMINMAXINFO ({ComputedWidth} < {MinimumWidth})",
                                    window.WindowReference.DebugString(), adjustedRect.Width, minSize.Value.X);
                            }
                            if (minSize.Value.Y > adjustedRect.Height)
                            {
                                m_logger.Warning("New height for {Window} is smaller than the value reported by WM_GETMINMAXINFO ({ComputedHeight} < {MinimumHeight})",
                                    window.WindowReference.DebugString(), adjustedRect.Height, minSize.Value.Y);
                            }
                        }
                    }
                }
                catch (InvalidWindowReferenceException)
                {
                    // Ignore
                }
                catch (Win32Exception e)
                {
                    m_logger.Error(e, "Failed to calculate reposition targets");
                }
            }
            return targets;
        }

        private IntPtr GetOverlayAnchor()
        {
            var desktop = m_workspace.VirtualDesktopManager.CurrentDesktop;
            using (m_backendLock.EnterScope())
            {
                var comparer = m_workspace.CreateSnapshotZOrderComparer();
                var tree = m_backend.GetTree(desktop);
                if (tree == null)
                    return new IntPtr(0);
                var topWindow = tree.Root!.Windows
                    .OrderBy(x => x.WindowReference, comparer)
                    .FirstOrDefault();

                if (topWindow != null)
                    return topWindow.WindowReference.Handle;

                return new IntPtr(0);
            }
        }

        /// <summary>
        /// A window started or stopped floating (float button, a rule added or removed, ...). Takes it out of this
        /// display's tabs, or puts it back if it belongs on this display.
        /// </summary>
        private void OnFloatingChanged(object? sender, IWindow window)
        {
            // Always deferred: the change can be raised while this service holds its backend lock.
            m_dispatcher.BeginInvoke(() => ApplyFloatingChange(window));
        }

        private void ApplyFloatingChange(IWindow window)
        {
            if (m_workspacePaused)
                return;

            try
            {
                if (m_floating.IsFloating(window))
                {
                    bool wasTab;
                    using (m_backendLock.EnterScope())
                        wasTab = m_backend.HasWindow(window);
                    if (!wasTab)
                        return;
                    DetectChanges(window);
                    OnWindowFloated(window);
                }
                else
                {
                    DetectChanges(window);
                }
            }
            catch (InvalidWindowReferenceException)
            {
                return;
            }
            InvalidateLayout();
        }

        private void OnDisplayWorkAreaChanged(object? sender, DisplayRectangleChangedEventArgs e)
        {
            InvalidateLayout();
        }

        private void OnDisplayScalingChanged(object? sender, DisplayScalingChangedEventArgs e)
        {
            UpdateLayoutSpacing();
        }

        private void OnWindowFloated(IWindow window)
        {
            Rectangle? originalPosition;
            try
            {
                using (m_backendLock.EnterScope())
                {
                    originalPosition = m_backend.GetOriginalPosition(window);
                }
            }
            catch
            {
                originalPosition = null;
            }
            try
            {
                originalPosition ??= GetOptimalRestoredSize(window);

                var originalDisplay = m_workspace.DisplayManager.Displays.FirstOrDefault(x => x.Bounds.Contains(originalPosition.Value.Center));
                originalDisplay ??= m_workspace.DisplayManager.PrimaryDisplay;

                var displayBounds = originalDisplay.Bounds;

                var centeredPosition = Rectangle.OffsetAndSize(
                    displayBounds.Left + displayBounds.Width / 2 - originalPosition.Value.Width / 2,
                    displayBounds.Top + displayBounds.Height / 2 - originalPosition.Value.Height / 2,
                    originalPosition.Value.Width,
                    originalPosition.Value.Height);

                window.SetPosition(centeredPosition);
                FocusHelper.ForceActivate(window.Handle);
            }
            catch (Exception e) when (e is InvalidWindowReferenceException || e is InvalidOperationException && window.State != WindowState.Restored)
            {
                // ignore
            }
        }

        private Rectangle GetOptimalRestoredSize(IWindow window)
        {
            var screenSize = m_display.WorkArea.Size;
            var minSize = window.MinSize ?? new Point(0, 0);
            var maxSize = window.MaxSize ?? new Point(screenSize.X, screenSize.Y);
            var pos = window.Position;

            return Rectangle.OffsetAndSize(
                pos.Left,
                pos.Top,
                Math.Max(minSize.X, Math.Min(maxSize.X, Math.Min(screenSize.X, (screenSize.X + minSize.X) / 2))),
                Math.Max(minSize.Y, Math.Min(maxSize.Y, Math.Min(screenSize.Y, (screenSize.Y + minSize.Y) / 2))));
        }


        private void OnWindowFloatRequested(object? sender, WindowNode e)
        {
            m_floating.Add(e.WindowReference, FloatReason.Manual);
        }

        private void OnAlwaysFloatRequested(object? sender, WindowNode e)
        {
            var window = e.WindowReference;
            _ = FloatingWindows.SetAlwaysFloatingAsync(window.GetCachedProcessName(), !m_floating.IsAlwaysFloating(window));
        }

        private void OnDesktopAdded(object? sender, DesktopChangedEventArgs e)
        {
            m_logger.Information("Desktop {Desktop} added to workspace", e.Source);
            using (m_backendLock.EnterScope())
            {
                m_backend.RegisterDesktop(e.Source, GetLayoutWorkArea());
                var tree = m_backend.GetTree(e.Source)!;
                tree.GroupNumberLane = m_groupNumberLane;
                tree.GroupNumberStride = m_groupNumberStride;
            }
        }

        private void OnDesktopRemoved(object? sender, DesktopChangedEventArgs e)
        {
            m_logger.Information("Desktop {Desktop} removed from workspace", e.Source);
            using (m_backendLock.EnterScope())
            {
                m_backend.UnregisterDesktop(e.Source);
            }
        }

        private void OnCurrentDesktopChanged(object? sender, CurrentDesktopChangedEventArgs e)
        {
            Refresh();
            InvalidateLayout();
        }

        private void OnWorkspaceFocusChanged(object? sender, FocusedWindowChangedEventArgs e) => InvalidateLayout();

        private void OnWindowGotFocus(object? sender, WindowFocusChangedEventArgs e)
        {
            m_dispatcher.BeginInvoke(() =>
            {
                if (!Active || m_workspacePaused || !Equals(m_workspace.FocusedWindow, e.Source))
                    return;

                m_logger.Debug("Got focus on {Window}", e.Source.DebugString());
                try
                {
                    bool hideMaximised = false;
                    using (m_backendLock.EnterScope())
                    {
                        if (m_backend.HasWindow(e.Source))
                        {
                            m_logger.Debug("Window {Window} is managed by backend, need to hide all obstructing windows", e.Source.DebugString());
                            // Focused restored windows that are in the tree cause all maximised windows
                            // to be send to the back
                            hideMaximised = true;
                            m_backend.SetFocus(e.Source);
                        }
                        else
                        {
                            m_logger.Debug("Window {Window} is not managed by backend", e.Source.DebugString());
                            return;
                        }
                    }

                    if (hideMaximised)
                    {
                        m_logger.Debug("Moving all obstructing maximised windows to back");
                        var comparer = m_workspace.CreateSnapshotZOrderComparer();
                        foreach (var maximisedWindow in m_workspace.GetCurrentDesktopSnapshot()
                            .Where(x => x.State == WindowState.Maximized && m_display.Bounds.Contains(x.Position.Center))
                            .OrderBy(x => x, comparer))
                        {
                            m_logger.Information("Moving maximised window {Window} to back", maximisedWindow.DebugString());
                            try
                            {
                                if (maximisedWindow.CanReorder)
                                {
                                    maximisedWindow.SendToBack();
                                }
                            }
                            catch (InvalidWindowReferenceException)
                            {
                                continue;
                            }
                            catch (Win32Exception ex)
                            {
                                m_logger.Error(ex, "Moving window {Window} to back failed ({@Metadata})", maximisedWindow.DebugString(), maximisedWindow.GetMetadata());
                                continue;
                            }
                        }
                    }
                    InvalidateLayout();
                }
                catch (InvalidWindowReferenceException)
                {
                    return;
                }
            }, System.Windows.Threading.DispatcherPriority.DataBind);
        }

        private void OnWindowAdded(object? sender, WindowChangedEventArgs e)
        {
            m_logger.Debug("Window {Window} added to workspace", e.Source.DebugString());
            try
            {
                using (m_windowSetLock.EnterScope())
                {
                    if (!m_windowSet.Add(e.Source))
                        return;
                    BindEventHandlers(e.Source);
                }
                m_floating.OnWindowAdded(e.Source, AutoRegisterWindows && m_autoFloatNewWindows);

                if (!AutoRegisterWindows)
                {
                    return;
                }

                if (!m_workspacePaused && CanManage(e.Source) && e.Source.State == WindowState.Restored)
                {
                    m_logger.Information("Window {Window} can be managed, registering with backend ({Display})", e.Source.DebugString(), m_display);
                    m_dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            using (m_backendLock.EnterScope())
                            {
                                if (m_workspacePaused || m_backend.HasWindow(e.Source) ||
                                    e.Source.State != WindowState.Restored || !CanManage(e.Source))
                                {
                                    return;
                                }

                                RegisterTab(e.Source);
                            }
                        }
                        catch
                        {
                            return;
                        }

                        InvalidateLayout();
                    }, System.Windows.Threading.DispatcherPriority.DataBind);
                }
            }
            catch (InvalidWindowReferenceException)
            {
                return;
            }
        }

        private void OnWindowRemoved(object? sender, WindowChangedEventArgs e)
        {
            if (m_workspacePaused)
                return;

            m_logger.Information("Window {Window} removed from workspace", e.Source.DebugString());

            using (m_windowSetLock.EnterScope())
            {
                if (m_windowSet.Remove(e.Source))
                    UnbindEventHandlers(e.Source);
            }
            using (m_savedLocationsLock.EnterScope())
            {
                m_savedLocations.Remove(e.Source);
            }
            using (m_ignoreRepositionSetLock.EnterScope())
            {
                m_ignoreRepositionSet.Remove(e.Source);
            }
            using (m_backendLock.EnterScope())
            {
                if (m_backend.HasWindow(e.Source))
                {
                    m_logger.Debug("Unregistering window {Window} from backend", e.Source.DebugString());
                    m_backend.UnregisterWindow(e.Source);
                    InvalidateLayout();
                }
            }
        }

        private void OnTabSplitRequested(WindowNode tab, TilingDirection direction)
        {
            if (!Active || m_workspacePaused)
                return;

            using (m_backendLock.EnterScope())
            {
                if (!m_backend.SplitTab(tab, direction))
                    return;
            }
            OnTilingNodeFocusRequested(this, tab);
            InvalidateLayout();
        }

        private void OnWrapInContainerRequested(WindowNode tab)
        {
            if (!Active || m_workspacePaused)
                return;
            using (m_backendLock.EnterScope())
            {
                if (!m_backend.HasWindow(tab.WindowReference) || m_backend.WrapInContainer(tab) == null)
                    return;
            }
            OnTilingNodeFocusRequested(this, tab);
        }

        private void OnMoveToContainerRequested(WindowNode tab, StackPanelNode container)
        {
            if (!Active || m_workspacePaused)
                return;
            using (m_backendLock.EnterScope())
            {
                if (!m_backend.MoveTabToContainer(tab, container))
                    return;
            }
            OnTilingNodeFocusRequested(this, tab);
        }

        private void OnRenameContainerRequested(StackPanelNode container) =>
            m_dispatcher.BeginInvoke(() => RenameContainer(container));

        private void RenameContainer(StackPanelNode container)
        {
            if (!Active || m_workspacePaused || !container.IsContainer)
                return;
            var dialog = new Windows.ContainerNameWindow(container.Name, container.GroupNumber);
            if (dialog.ShowDialog() != true)
                return;
            using (m_backendLock.EnterScope())
            {
                if (container.IsContainer && container.Desktop != null)
                    container.Name = dialog.ContainerName;
            }
            InvalidateLayout();
        }

        private void OnUnpackContainerRequested(StackPanelNode container)
        {
            if (!Active || m_workspacePaused)
                return;
            using (m_backendLock.EnterScope())
            {
                if (!m_backend.Unpack(container))
                    return;
            }
            InvalidateLayout();
        }

        /// <summary>
        /// A tab is dragged (by its window's title bar) or dropped. Shows where it will land, or on commit puts it
        /// there: on this display, or on the display under the cursor. Returns whether the drop changed anything.
        /// </summary>
        private bool OnTabMoved(WindowNode tab, Point point, bool commit)
        {
            if (!Active || m_workspacePaused)
                return false;

            if (!m_display.Bounds.Contains(point))
            {
                m_gui.PreviewRectangle = null;
                return ExternalTabDrag?.Invoke(this, tab, point, commit) == true;
            }

            ClearExternalDropPreview?.Invoke();
            using (m_backendLock.EnterScope())
            {
                var desktop = m_backend.GetTree(m_workspace.VirtualDesktopManager.CurrentDesktop);
                if (desktop == null || tab.Desktop != desktop)
                {
                    m_gui.PreviewRectangle = null;
                    return false;
                }
                var groupTab = m_gui.FindGroupTabAt(point);
                if (groupTab is { } named)
                {
                    bool valid = named.Group.Desktop == desktop && named.Group != tab.Parent &&
                        TilingWorkspace.CanAcceptTab(tab, named.Group, null);
                    m_gui.PreviewRectangle = !commit && valid ? named.Bounds : null;
                    if (!commit || !valid || !m_backend.MoveTabToContainer(tab, named.Group))
                        return false;
                }
                else
                {
                    bool dropOnUngrouped = m_gui.IsOnUngroupedTab(point);
                    if (!commit)
                    {
                        var target = m_backend.GetTabDropTarget(tab, point, dropOnUngrouped);
                        m_gui.PreviewRectangle = target is { } drop ? GetTabMovePreview(drop.Group, drop.Edge) : null;
                        return false;
                    }
                    m_gui.PreviewRectangle = null;
                    if (!m_backend.DropTab(tab, point, dropOnUngrouped))
                        return false;
                }
            }
            OnTilingNodeFocusRequested(this, tab);
            InvalidateLayout();
            return true;
        }

        private void OnGroupMoved(StackPanelNode source, Point point, bool commit)
        {
            if (!Active || m_workspacePaused)
                return;

            if (!m_display.Bounds.Contains(point))
            {
                m_gui.PreviewRectangle = null;
                ExternalGroupDrag?.Invoke(this, source, point, commit);
                return;
            }

            ClearExternalDropPreview?.Invoke();
            WindowNode? focus = null;
            using (m_backendLock.EnterScope())
            {
                var target = source.Desktop?.Root is PanelNode root && source.Desktop == m_backend.GetTree(m_workspace.VirtualDesktopManager.CurrentDesktop)
                    ? TilingWorkspace.FindGroupAt(root, point) : null;
                if (target == null || target == source)
                {
                    m_gui.PreviewRectangle = null;
                    return;
                }

                // On its tab bar: merge into it. Near an edge: tile beside it. In the middle: swap places.
                if (TilingWorkspace.IsOnTabBar(target, point))
                {
                    bool canMerge = TilingWorkspace.CanMergeInto(source, target);
                    if (!commit)
                    {
                        m_gui.PreviewRectangle = canMerge ? TilingWorkspace.GetGroupBounds(target) : null;
                        return;
                    }
                    m_gui.PreviewRectangle = null;
                    var selected = source.SelectedTab;
                    if (!canMerge || !m_backend.MergeTabGroup(source, target))
                        return;
                    focus = selected;
                }
                else
                {
                    var edge = TilingWorkspace.GetDropEdge(target, point);
                    bool valid = m_backend.CanMoveTabGroup(source, target, edge);
                    if (!commit)
                    {
                        m_gui.PreviewRectangle = valid ? GetGroupMovePreview(source, target, edge) : null;
                        return;
                    }
                    m_gui.PreviewRectangle = null;
                    if (!valid || !m_backend.MoveTabGroup(source, target, edge))
                        return;
                }
            }
            InvalidateLayout();
            if (focus != null)
                OnTilingNodeFocusRequested(this, focus);
        }

        /// <summary>
        /// Swapping two groups highlights the spot the dragged group will take (the target's place), slightly
        /// inset, so it reads differently from a merge, which highlights the target group with its tab bar.
        /// </summary>
        private static Rectangle GetGroupMovePreview(StackPanelNode source, StackPanelNode target, TilingDirection? edge)
        {
            if (edge != null)
                return GetDropPreview(target, edge);
            var bounds = TilingWorkspace.GetGroupBounds(target);
            var bar = TilingWorkspace.GetTabBarBounds(target);
            return new Rectangle(bounds.Left, bar.Bottom, bounds.Right, bounds.Bottom);
        }

        /// <summary>
        /// Where something dragged in from another display would land here: an empty display takes it whole
        /// (no target), otherwise the group under the cursor and the side to tile on. Call with the backend lock held.
        /// </summary>
        private bool TryGetForeignDrop(Point point, out StackPanelNode? target, out TilingDirection? edge)
        {
            target = null;
            edge = null;
            if (m_backend.GetTree(m_workspace.VirtualDesktopManager.CurrentDesktop)?.Root is not PanelNode root)
                return false;
            if (root.Children.Count == 0)
                return true;
            target = TilingWorkspace.FindGroupAt(root, point);
            if (target == null)
                return false;
            edge = TilingWorkspace.GetUngroupedBarBounds(target).Contains(point) ? null : TilingWorkspace.GetDropEdge(target, point);
            return true;
        }

        /// <summary>
        /// A tab from another display is dragged over this one. Previews the drop, or on commit takes the tab: into
        /// the empty display, into the group under the cursor, or as a new group beside it.
        /// </summary>
        internal bool PreviewOrDropTabFrom(TilingService source, WindowNode tab, Point point, bool commit)
        {
            if (!Active || m_workspacePaused || !source.Active || source.m_workspacePaused)
            {
                ClearGroupPreview();
                return false;
            }

            var desktop = m_workspace.VirtualDesktopManager.CurrentDesktop;
            WindowNode? moved;
            using (source.m_backendLock.EnterScope())
            using (m_backendLock.EnterScope())
            {
                var tree = m_backend.GetTree(desktop);
                var groupTab = m_gui.FindGroupTabAt(point);
                StackPanelNode? target = groupTab?.Group;
                TilingDirection? edge = null;
                if (groupTab == null)
                {
                    if (!TryGetForeignDrop(point, out target, out edge))
                    {
                        ClearGroupPreview();
                        return false;
                    }
                    if (target != null)
                    {
                        var dropOnUngrouped = m_gui.IsOnUngroupedTab(point);
                        edge = dropOnUngrouped ? null : TilingWorkspace.GetTabDropEdge(target, point);
                        if (!dropOnUngrouped && edge == null && target.SelectedChild is StackPanelNode { IsContainer: true } container)
                            target = container;
                    }
                }
                if (target != null && (target.Desktop != tree || !TilingWorkspace.CanAcceptTab(tab, target, edge)))
                {
                    ClearGroupPreview();
                    return false;
                }
                if (!commit)
                {
                    m_gui.PreviewRectangle = groupTab?.Bounds ?? (target == null ? GetLayoutWorkArea() : GetTabMovePreview(target, edge));
                    return false;
                }
                ClearGroupPreview();
                moved = source.m_backend.TransferTab(tab, m_backend, target, edge, desktop);
                if (moved == null)
                    return false;
                moved.Parent!.Padding = GetPanelPaddingRect();
                ((StackPanelNode)moved.Parent).Spacing = GetPanelSpacing();
            }
            source.InvalidateLayout();
            InvalidateLayout();
            OnTilingNodeFocusRequested(this, moved);
            return true;
        }

        /// <summary>
        /// A group from another display is dragged over this one. Previews the drop, or on commit takes the group:
        /// onto an empty display, swapped with the group under the cursor, or tiled beside it.
        /// </summary>
        internal void PreviewOrDropGroupFrom(TilingService source, StackPanelNode group, Point point, bool commit)
        {
            m_gui.PreviewRectangle = null;
            if (!Active || m_workspacePaused || !source.Active || source.m_workspacePaused)
                return;

            var desktop = m_workspace.VirtualDesktopManager.CurrentDesktop;
            StackPanelNode? target;
            TilingDirection? direction;
            bool merge;
            using (source.m_backendLock.EnterScope())
            using (m_backendLock.EnterScope())
            {
                if (!TryGetForeignDrop(point, out target, out direction))
                    return;
                merge = target != null && TilingWorkspace.IsOnTabBar(target, point);
                if (target != null && !(merge ? TilingWorkspace.CanMergeInto(group, target) : TilingWorkspace.CanAcceptGroup(group, target, direction)))
                    return;
                if (!commit)
                {
                    m_gui.PreviewRectangle = target == null ? GetLayoutWorkArea()
                        : merge ? TilingWorkspace.GetGroupBounds(target)
                        : GetGroupMovePreview(group, target, direction);
                    return;
                }
            }

            if (target == null)
            {
                source.TransferGroupTo(group, this);
                return;
            }

            WindowNode? focus;
            if (merge)
            {
                using (source.m_backendLock.EnterScope())
                using (m_backendLock.EnterScope())
                {
                    focus = group.SelectedTab;
                    if (!source.m_backend.MergeTabGroupInto(group, m_backend, target, desktop))
                        return;
                }
                source.InvalidateLayout();
                InvalidateLayout();
                if (focus != null)
                    OnTilingNodeFocusRequested(this, focus);
                return;
            }

            using (source.m_backendLock.EnterScope())
            using (m_backendLock.EnterScope())
            {
                if (!source.m_backend.MoveTabGroupTo(group, m_backend, target, direction, desktop))
                    return;
                group.Padding = GetPanelPaddingRect();
                group.Spacing = GetPanelSpacing();
                if (direction == null)
                {
                    target.Padding = source.GetPanelPaddingRect();
                    target.Spacing = source.GetPanelSpacing();
                }
                focus = group.SelectedTab;
            }
            source.InvalidateLayout();
            InvalidateLayout();
            if (focus != null)
                OnTilingNodeFocusRequested(this, focus);
        }

        private Point GetCursor() =>
            wintabs.DllImports.PInvoke.GetCursorPos(out var cursor) ? new Point(cursor.x, cursor.y) : m_workspace.CursorLocation;

        /// <summary>
        /// A title-bar drag moves the window; a border drag resizes it, keeping the opposite edges in place. Moving
        /// onto a monitor with other scaling also resizes the window, so the size alone does not tell them apart.
        /// </summary>
        internal static bool IsMoveGesture(Rectangle start, Rectangle position)
        {
            if (position == start)
                return false;
            if (position.Size == start.Size)
                return true;
            bool xAnchored = position.Left == start.Left || position.Right == start.Right;
            bool yAnchored = position.Top == start.Top || position.Bottom == start.Bottom;
            return !(xAnchored && yAnchored);
        }

        private static Rectangle GetTabMovePreview(StackPanelNode target, TilingDirection? edge) =>
            target.IsContainer ? TilingWorkspace.GetGroupBounds((StackPanelNode)target.Parent!) : GetDropPreview(target, edge);

        private static Rectangle GetDropPreview(StackPanelNode group, TilingDirection? direction)
        {
            var bounds = TilingWorkspace.GetGroupBounds(group);
            return direction switch
            {
                TilingDirection.Left => new Rectangle(bounds.Left, bounds.Top, bounds.Center.X, bounds.Bottom),
                TilingDirection.Right => new Rectangle(bounds.Center.X, bounds.Top, bounds.Right, bounds.Bottom),
                TilingDirection.Up => new Rectangle(bounds.Left, bounds.Top, bounds.Right, bounds.Center.Y),
                TilingDirection.Down => new Rectangle(bounds.Left, bounds.Center.Y, bounds.Right, bounds.Bottom),
                _ => bounds,
            };
        }

        internal void ClearGroupPreview()
        {
            m_gui.PreviewRectangle = null;
        }

        /// <summary>
        /// Points the backend focus at the real foreground window, so keyboard actions target the tab the user sees.
        /// Call with the backend lock held.
        /// </summary>
        private void SyncFocusToForeground()
        {
            // Right after a shortcut activates a tab, Windows may still report the previous foreground window.
            // Trust our own choice for a moment so back-to-back shortcuts build on each other.
            if (Environment.TickCount64 - m_lastShortcutFocus < ShortcutFocusGraceMs)
                return;
            if (m_workspace.FocusedWindow is IWindow foreground && m_backend.HasWindow(foreground))
                m_backend.SetFocus(foreground);
        }

        private const long ShortcutFocusGraceMs = 400;
        private long m_lastShortcutFocus;

        /// <summary>
        /// When this display last took focus through a shortcut, if that is recent enough to still be in effect.
        /// </summary>
        internal long? RecentShortcutFocus =>
            Environment.TickCount64 - m_lastShortcutFocus < ShortcutFocusGraceMs ? m_lastShortcutFocus : null;

        /// <summary>
        /// Records that WinTabs itself just moved focus to <paramref name="tab"/>. Call with the backend lock held.
        /// </summary>
        private void SetShortcutFocus(WindowNode tab)
        {
            m_backend.SetFocus(tab);
            m_lastShortcutFocus = Environment.TickCount64;
        }

        // Another group closed and the numbers after it moved up; redraw this display's badges.
        private void OnGroupsRenumbered(object? sender, EventArgs e) => m_dispatcher.BeginInvoke(InvalidateLayout);


        internal bool HasWindow(IWindow window)
        {
            using (m_backendLock.EnterScope())
                return m_backend.HasWindow(window);
        }

        internal bool HasGroup(int groupNumber)
        {
            using (m_backendLock.EnterScope())
                return m_backend.FindGroup(m_workspace.VirtualDesktopManager.CurrentDesktop, groupNumber) != null;
        }

        internal bool TransferFocusedTabTo(TilingService destination, int groupNumber)
        {
            if (!Active || m_workspacePaused || !destination.Active || destination.m_workspacePaused)
                return false;

            WindowNode? moved;
            var desktop = m_workspace.VirtualDesktopManager.CurrentDesktop;
            using (m_backendLock.EnterScope())
            using (destination.m_backendLock.EnterScope())
            {
                SyncFocusToForeground();
                if (destination.m_backend.FindGroup(desktop, groupNumber) is not StackPanelNode target)
                    return false;
                moved = m_backend.TransferFocusedTab(desktop, destination.m_backend, target);
            }
            if (moved == null)
                return false;

            InvalidateLayout();
            destination.InvalidateLayout();
            destination.OnTilingNodeFocusRequested(this, moved);
            return true;
        }

        internal bool TransferGroupTo(StackPanelNode group, TilingService destination)
        {
            if (!Active || m_workspacePaused || !destination.Active || destination.m_workspacePaused)
                return false;

            WindowNode? focus;
            var desktop = m_workspace.VirtualDesktopManager.CurrentDesktop;
            using (m_backendLock.EnterScope())
            using (destination.m_backendLock.EnterScope())
            {
                if (!m_backend.TransferTabGroup(group, destination.m_backend, desktop))
                    return false;

                group.Padding = destination.GetPanelPaddingRect();
                group.Spacing = destination.GetPanelSpacing();
                focus = destination.m_backend.GetFocus(desktop) as WindowNode;
            }
            InvalidateLayout();
            destination.InvalidateLayout();
            if (focus != null)
                destination.OnTilingNodeFocusRequested(this, focus);
            return true;
        }

        private void OnDividerResized(SplitDividerViewModel divider, double delta)
        {
            if (!Active || m_workspacePaused)
                return;

            using (m_backendLock.EnterScope())
            {
                if (divider.Split.Desktop == m_backend.GetTree(m_workspace.VirtualDesktopManager.CurrentDesktop) &&
                    divider.Split.ResizeDivider(divider.Index, delta))
                    InvalidateLayout();
            }
        }

        private void OnWindowPositionChangeEnd(object? sender, WindowPositionChangedEventArgs e)
        {
            if (!Active || m_workspacePaused)
                return;

            Rectangle startPosition;
            using (m_ignoreRepositionSetLock.EnterScope())
            {
                if (!m_ignoreRepositionSet.Remove(e.Source, out startPosition))
                    return;
            }
            var dropPoint = GetCursor();
            m_dispatcher.InvokeAsync(() =>
            {
                if (!Active || m_workspacePaused)
                    return;

                m_gui.PreviewRectangle = null;
                try
                {
                    WindowNode? tab;
                    using (m_backendLock.EnterScope())
                        tab = m_backend.FindWindow(e.Source);

                    // A tab of another display: that display handles its drop.
                    if (tab == null && IsTabElsewhere?.Invoke(e.Source) == true)
                        return;

                    if (tab != null && e.Source.State == WindowState.Restored)
                    {
                        if (IsMoveGesture(startPosition, e.NewPosition))
                        {
                            if (OnTabMoved(tab, dropPoint, commit: true))
                                return;
                            // Released where nothing accepts it: back into its group, unless it went to another monitor.
                            if (!m_display.Bounds.Contains(dropPoint))
                                DetectChanges(e.Source);
                        }
                        else if (e.NewPosition != startPosition)
                        {
                            using (m_backendLock.EnterScope())
                            {
                                if (TilingWorkspace.GetGroup(tab) is StackPanelNode group)
                                    m_backend.ResizeNode(group, e.NewPosition, startPosition);
                            }
                        }
                        InvalidateLayout();
                        return;
                    }
                    DetectChanges(e.Source);
                }
                catch (InvalidWindowReferenceException)
                {
                    // The window closed before the dispatcher handled the gesture.
                }
                InvalidateLayout();
            });
        }

        private int m_nativeDragPreviewQueued;

        private void OnNativeDragCursorChanged(object? sender, CursorLocationChangedEventArgs e)
        {
            if (!Active || m_workspacePaused)
                return;
            using (m_ignoreRepositionSetLock.EnterScope())
            {
                if (m_ignoreRepositionSet.Count == 0)
                    return;
            }
            if (System.Threading.Interlocked.Exchange(ref m_nativeDragPreviewQueued, 1) != 0)
                return;
            _ = m_dispatcher.InvokeAsync(() =>
            {
                System.Threading.Interlocked.Exchange(ref m_nativeDragPreviewQueued, 0);
                if (!Active || m_workspacePaused)
                    return;
                KeyValuePair<IWindow, Rectangle>[] dragging;
                using (m_ignoreRepositionSetLock.EnterScope())
                    dragging = m_ignoreRepositionSet.ToArray();
                // Every display sees the gesture; only the display that owns the tab draws the preview (on itself or
                // on the display under the cursor), so displays never clear each other's preview.
                foreach (var (window, start) in dragging)
                {
                    try
                    {
                        WindowNode? tab;
                        using (m_backendLock.EnterScope())
                            tab = m_backend.FindWindow(window);
                        if (tab == null)
                            continue;
                        if (IsMoveGesture(start, window.Position))
                        {
                            OnTabMoved(tab, GetCursor(), commit: false);
                        }
                        else
                        {
                            m_gui.PreviewRectangle = null;
                            ClearExternalDropPreview?.Invoke();
                        }
                        return;
                    }
                    catch (InvalidWindowReferenceException)
                    {
                        // A window may close during the gesture.
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void OnWindowPositionChanged(object? sender, WindowPositionChangedEventArgs e)
        {
            if (!Active || m_workspacePaused)
                return;

            using (m_ignoreRepositionSetLock.EnterScope())
            {
                if (m_ignoreRepositionSet.ContainsKey(e.Source))
                    return;
            }

            // Keep resizable tabs on their assigned monitor during programmatic moves.
            // Fixed-size apps can move themselves back to a different monitor after placement.
            m_dispatcher.InvokeAsync(() =>
            {
                if (!Active || m_workspacePaused || IsInWindowGesture(e.Source))
                    return;

                try
                {
                    using (m_backendLock.EnterScope())
                    {
                        if (!m_backend.HasWindow(e.Source) ||
                            (!e.Source.CanResize && !m_display.Bounds.Contains(e.Source.Position.Center)))
                            DetectChanges(e.Source);
                    }
                }
                catch (InvalidWindowReferenceException)
                {
                    // The window closed before the queued event was processed.
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool IsInWindowGesture(IWindow window)
        {
            using (m_ignoreRepositionSetLock.EnterScope())
                return m_ignoreRepositionSet.ContainsKey(window);
        }

        private void OnWindowTopmostChanged(object? sender, WindowTopmostChangedEventArgs e)
        {
            if (!m_active)
                return;

            try
            {
                m_logger.Verbose("Changed topmost of window {Window}", e.Source.DebugString());
                DetectChanges(e.Source);
            }
            catch (InvalidWindowReferenceException)
            {
                return;
            }
        }

        private void OnWindowStateChanged(object? sender, WindowStateChangedEventArgs e)
        {
            if (!m_active || m_workspacePaused)
                return;

            void UnregisterAndSaveLocation()
            {
                using (m_backendLock.EnterScope())
                {
                    var window = m_backend.FindWindow(e.Source);
                    if (window != null)
                    {
                        using (m_savedLocationsLock.EnterScope())
                        {
                            // Be resilient to multiple OnWindowStateChanged events happening one after the other
                            m_savedLocations[e.Source] = new NodeLocation(window);
                        }
                        InvalidateLayout();
                        m_backend.UnregisterWindow(e.Source);
                    }
                }
                DetectChanges(e.Source);
            }

            void RegisterAndRestoreLocation()
            {
                NodeLocation? saved;
                using (m_savedLocationsLock.EnterScope())
                    m_savedLocations.Remove(e.Source, out saved);

                using (m_backendLock.EnterScope())
                {
                    var node = m_backend.FindWindow(e.Source);
                    if (node == null)
                    {
                        var tree = m_backend.GetTree(m_workspace.VirtualDesktopManager.CurrentDesktop);
                        node = saved?.Parent is StackPanelNode group && group.Desktop != null && group.Desktop == tree
                            ? m_backend.RegisterWindow(e.Source, group)
                            : RegisterTab(e.Source);
                        if (Equals(m_workspace.FocusedWindow, e.Source))
                            m_backend.SetFocus(node);
                    }
                    var parent = node.Parent!;
                    if (saved?.Parent == parent)
                        parent.Move(parent.IndexOf(node), Math.Clamp(saved.Index,
                            parent.Children.TakeWhile(child => child is StackPanelNode).Count(), parent.Children.Count - 1));
                }

                InvalidateLayout();
            }

            try
            {
                m_logger.Information("Changed state of window {Window} to {NewState}", e.Source.DebugString(), e.NewState);

                try
                {
                    // Window is now minimized or maximized but was restored
                    if ((e.NewState == WindowState.Maximized || e.NewState == WindowState.Minimized)
                        && e.OldState == WindowState.Restored)
                    {
                        UnregisterAndSaveLocation();
                    }
                    // Window is now restored
                    else if (e.NewState == WindowState.Restored
                        && (e.OldState == WindowState.Maximized || e.OldState == WindowState.Minimized))
                    {
                        if (!CanManage(e.Source))
                        {
                            return;
                        }
                        RegisterAndRestoreLocation();
                    }
                    else
                    {
                        DetectChanges(e.Source);
                    }
                }
                catch (InvalidWindowReferenceException)
                {
                    return;
                }
                catch (WindowAlreadyRegisteredException)
                {
                    return;
                }

                if (Equals(m_workspace.FocusedWindow, sender))
                {
                    m_logger.Debug("Window {Window} is also focused, calling OnWindowGotFocus", e.Source.DebugString());
                    // This is to update focus when a maximised window is restored.
                    OnWindowGotFocus(e.Source, new WindowFocusChangedEventArgs(e.Source, true));
                }
            }
            catch (InvalidWindowReferenceException)
            {
                return;
            }
        }

        private void OnWindowPositionChangeStart(object? sender, WindowPositionChangedEventArgs e)
        {
            if (!m_active || m_workspacePaused)
                return;

            using (m_ignoreRepositionSetLock.EnterScope())
            {
                m_ignoreRepositionSet.TryAdd(e.Source, e.NewPosition);
            }
            InvalidateLayout();
        }

        private void OnTilingNodeFocusRequested(object? sender, TilingNode e)
        {
            WindowNode? windowNode;
            using (m_backendLock.EnterScope())
            {
                windowNode = e is StackPanelNode row ? row.SelectedTab : e.Windows.FirstOrDefault();
                if (windowNode == null || !m_backend.HasWindow(windowNode.WindowReference))
                    return;
                // Record the target first, so a shortcut pressed straight after builds on this one.
                SetShortcutFocus(windowNode);
            }

            try
            {
                FocusHelper.ForceActivate(windowNode.WindowReference.Handle);
            }
            catch (InvalidWindowReferenceException)
            {
                return;
            }
            InvalidateLayout();
        }

        private void OnTilingNodeCloseRequested(object? sender, TilingNode e)
        {
            foreach (var window in e.Windows.ToList())
            {
                try
                {
                    if (window.WindowReference.CanClose)
                    {
                        window.WindowReference.Close();
                    }
                }
                catch (InvalidWindowReferenceException)
                {
                    // Ignore
                }
                catch (Win32Exception)
                {
                    // Ignore
                    // TODO: Show toast
                }
            }
        }

        private void BindEventHandlers(IWindow window)
        {
            window.StateChanged += OnWindowStateChanged;
            window.PositionChangeStart += OnWindowPositionChangeStart;
            window.PositionChangeEnd += OnWindowPositionChangeEnd;
            window.PositionChanged += OnWindowPositionChanged;
            window.GotFocus += OnWindowGotFocus;
            window.TopmostChanged += OnWindowTopmostChanged;
        }

        private void UnbindEventHandlers(IWindow window)
        {
            window.StateChanged -= OnWindowStateChanged;
            window.PositionChangeStart -= OnWindowPositionChangeStart;
            window.PositionChangeEnd -= OnWindowPositionChangeEnd;
            window.PositionChanged -= OnWindowPositionChanged;
            window.GotFocus -= OnWindowGotFocus;
            window.TopmostChanged -= OnWindowTopmostChanged;
        }

        private WindowNode RegisterTab(IWindow window)
        {
            var node = m_backend.RegisterWindow(window);
            node.Parent!.Padding = GetPanelPaddingRect();
            node.Parent.Spacing = GetPanelSpacing();
            if (Equals(m_workspace.FocusedWindow, window))
                m_backend.SetFocus(node);
            return node;
        }

        private bool DetectChanges(IWindow window)
        {
            // While the user drags a window, only the drop decides where it goes.
            if (m_workspacePaused || IsInWindowGesture(window))
                return false;

            m_logger.Verbose("Dirty checking for changes with window {Window}", window.DebugString());
            try
            {
                if (window.State == WindowState.Restored && CanManage(window))
                {
                    if (!AutoRegisterWindows)
                    {
                        return false;
                    }

                    using (m_backendLock.EnterScope())
                    {
                        try
                        {
                            if (!m_backend.HasWindow(window))
                            {
                                m_logger.Debug("Window {Window} can be managed, but is not registered with backend, registering now", window.DebugString());
                                RegisterTab(window);
                                InvalidateLayout();
                                return true;
                            }
                        }
                        catch (InvalidWindowReferenceException)
                        {
                            if (m_backend.HasWindow(window))
                                m_backend.UnregisterWindow(window);
                        }
                    }
                }
                else
                {
                    using (m_backendLock.EnterScope())
                    {
                        if (m_backend.HasWindow(window))
                        {
                            m_logger.Verbose("Window {Window} can no longer be managed, but is registered with backend, unregistering now", window.DebugString());
                            m_backend.UnregisterWindow(window);

                            InvalidateLayout();
                            return true;
                        }
                    }
                }
            }
            catch (WindowAlreadyRegisteredException)
            {
                return false;
            }
            // TODO: Is the following catch block necessary?
            catch (InvalidOperationException)
            {
                return false;
            }
            return false;
        }

        private bool CanManage(IWindow x)
        {
            bool IsOnCurrentDisplay()
            {
                var pos = x.Position.Center;
                if (m_display.Bounds.Contains(pos))
                    return true;

                // Assign windows outside every display (e.g. after unplugging a monitor) only once.
                return m_display.Equals(m_workspace.DisplayManager.PrimaryDisplay)
                    && !m_workspace.DisplayManager.Displays.Any(d => d.Bounds.Contains(pos));
            }
            bool IsFloating() => m_floating.IsFloating(x);

            // Cheap boolean read
            if (x.IsTopmost)
            {
                return false;
            }

            // The window may have been removed while a registration was still queued.
            using (m_windowSetLock.EnterScope())
            {
                if (!m_windowSet.Contains(x))
                    return false;
            }

            // Live check: hosts such as mRemoteNG adopt another process's window into their own
            // right after it is created. Once embedded it is no longer a top-level window.
            if (!x.IsTopLevelWindow())
            {
                return false;
            }

            // Set lookup
            if (IsFloating())
            {
                return false;
            }

            // GetWindowPos + Lookup
            if (!IsOnCurrentDisplay())
            {
                return false;
            }

            // Fixed-size windows can be moved into a tab group without being resized.
            // OpenProcess (expensive)
            if (!x.CanMove)
            {
                return false;
            }

            // Virtual Desktop stuff is very expensive
            if (m_workspace.VirtualDesktopManager.IsWindowPinned(x))
            {
                return false;
            }

            return true;
        }

        private void InvalidateLayout()
        {
            if (!m_active || m_workspacePaused)
                return;

            m_dirty = true;
            if (m_frozen.IsPositive() || System.Threading.Interlocked.Exchange(ref m_layoutQueued, 1) != 0)
                return;

            _ = m_dispatcher.InvokeAsync(async () =>
            {
                System.Threading.Interlocked.Exchange(ref m_layoutQueued, 0);
                if (!m_dirty || m_frozen.IsPositive() || !m_active || m_workspacePaused)
                    return;

                m_dirty = false;
                try
                {
                    await UpdateLayoutAsync();
                }
                catch (Exception e) when (e is InvalidReferenceException or Win32Exception or UnsatisfiableFlexConstraintsException)
                {
                    m_logger.Warning(e, "Could not apply the tab layout");
                }
            }, System.Windows.Threading.DispatcherPriority.DataBind);
        }

        private void Freeze()
        {
            m_frozen.Increment();
        }

        private void Unfreeze()
        {
            if (m_frozen.DecrementIfPositive())
            {
                if (m_dirty)
                {
                    InvalidateLayout();
                }
            }
        }

        internal static Rectangle CenterFixedSizeWindow(Rectangle container, Point size) => Rectangle.OffsetAndSize(
            container.Left + Math.Max(0, container.Width - size.X) / 2,
            container.Top + Math.Max(0, container.Height - size.Y) / 2,
            size.X, size.Y);

        // Each tile supplies half the gap. Add the other half at the work-area edge, including the taskbar.
        internal static Rectangle InsetWorkArea(Rectangle area, int gap)
        {
            int leading = gap / 2;
            int trailing = gap - leading;
            return new Rectangle(area.Left + trailing, area.Top + trailing, area.Right - leading, area.Bottom - leading);
        }

        private Rectangle GetLayoutWorkArea() => InsetWorkArea(m_display.WorkArea, GetPanelSpacing());

        private int GetPanelSpacing() => (int)Math.Round(Math.Max(0, m_windowPadding) * m_display.Scaling);

        private Rectangle GetPanelPaddingRect() =>
            new(0, (int)Math.Round(m_panelHeight * m_display.Scaling) + GetPanelSpacing(), 0, 0);

        private static System.Windows.Thickness ToThickness(Rectangle rc)
        {
            return new System.Windows.Thickness(rc.Left, rc.Top, rc.Right, rc.Bottom);
        }

        private void UpdateGuiNodeOptions()
        {
            m_dispatcher.Invoke(() =>
            {
                m_gui.PanelSpacing = GetPanelSpacing();
                m_gui.PanelPadding = ToThickness(GetPanelPaddingRect());
                m_gui.InvalidateView();
                InvalidateLayout();
            });
        }

        private void UpdateLayoutSpacing()
        {
            using (m_backendLock.EnterScope())
            {
                foreach (var panel in m_backend.Trees.SelectMany(x => x.Root!.Nodes).OfType<PanelNode>())
                {
                    if (panel is StackPanelNode)
                    {
                        panel.Padding = GetPanelPaddingRect();
                        panel.Spacing = GetPanelSpacing();
                    }
                }
            }
            UpdateGuiNodeOptions();
        }
    }
}
