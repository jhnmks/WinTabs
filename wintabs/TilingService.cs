using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

using wintabs.Layouts.Tiling;
using wintabs.Utilities;
using WinMan;
using System;
using wintabs.Models;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using Serilog;
using System.Threading.Tasks;
using System.ComponentModel;

#if DEBUG
using Lock = wintabs.Utilities.DebugLock;
#else
using Lock = System.Threading.Lock;
#endif

namespace wintabs
{
    /// <summary>
    /// Manages the layout of window in the workspace
    /// </summary>
    internal partial class TilingService : ITilingService, IDisposable
    {
        private class NodeLocation(TilingNode node)
        {
            public PanelNode Parent = node.Parent ?? throw new ArgumentException(nameof(node));
            public int Index = node.Parent.IndexOf(node);

        }

        public event EventHandler<TilingFailedEventArgs>? PlacementFailed;

        /// <summary>
        /// Current active state.
        /// <see cref="Start"/>
        /// <see cref="Stop"/>
        /// </summary>
        public bool Active
        {
            get => m_active;
        }

        public bool AutoRegisterWindows { get; internal set; }




        private bool m_autoFloatNewWindows = false;

        public IWorkspace Workspace => m_workspace;

        private int m_groupNumberLane;
        private int m_groupNumberStride = 2;

        internal void SetGroupNumberSequence(int lane, int stride)
        {
            using (m_backendLock.EnterScope())
            {
                m_groupNumberLane = lane;
                m_groupNumberStride = stride;
                foreach (var tree in m_backend.Trees)
                {
                    tree.GroupNumberLane = lane;
                    tree.GroupNumberStride = stride;
                }
            }
            InvalidateLayout();
        }

        internal TilingWorkspace TabWorkspace => m_backend;
        internal Action<TilingService, StackPanelNode, Point, bool>? ExternalGroupDrag { get; set; }
        internal Func<TilingService, WindowNode, Point, bool, bool>? ExternalTabDrag { get; set; }
        internal Action? ClearExternalDropPreview { get; set; }
        /// <summary>Whether another display has the window as a tab.</summary>
        internal Func<IWindow, bool>? IsTabElsewhere { get; set; }


        private static readonly TimeSpan LockThreshold = TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// The dispatcher from the thread that created the <see cref="TilingService"/>
        /// </summary>
        private readonly Dispatcher m_dispatcher;
        private readonly IWorkspace m_workspace;
        private readonly ILogger m_logger = App.Current.Logger;
        private readonly FloatingWindows m_floating;

        private readonly TilingOverlayRenderer m_gui;
        private readonly IDisplay m_display;

        private readonly TilingWorkspace m_backend;
        private readonly Utilities.DebugLock m_backendLock = new(LockThreshold);


        private readonly HashSet<IWindow> m_windowSet = [];
        private readonly Utilities.DebugLock m_windowSetLock = new(LockThreshold);


        private readonly Dictionary<IWindow, Rectangle> m_ignoreRepositionSet = [];
        private readonly Utilities.DebugLock m_ignoreRepositionSetLock = new(LockThreshold);

        private readonly Dictionary<IWindow, NodeLocation> m_savedLocations = [];
        private readonly Utilities.DebugLock m_savedLocationsLock = new(LockThreshold);

        private readonly CompositeDisposable m_subscriptions = [];
        private int m_panelHeight = 20;
        private int m_windowPadding = 2;


        private bool m_active = false;
        private volatile bool m_workspacePaused;
        private int m_layoutQueued;
        private volatile bool m_dirty = true;

        private readonly Counter m_frozen = new();

        public TilingService(IWorkspace workspace, IDisplay display, IObservable<ITilingServiceSettings> settings, FloatingWindows floating, bool autoRegisterWindows, TilingWorkspace? existingWorkspace = null, bool workspacePaused = false)
        {
            m_floating = floating;
            m_logger.Information("Managing display {Display} (Bounds: {Bounds}, Scale: {Scaling})", display, display.Bounds, display.Scaling);
            m_dispatcher = Dispatcher.CurrentDispatcher;
            m_workspace = workspace;
            m_display = display;
            m_workspacePaused = workspacePaused;
            m_backend = existingWorkspace ?? new TilingWorkspace(tabsOnly: true);
            m_gui = new TilingOverlayRenderer(display, GetOverlayAnchor)
            {
                PanelSpacing = GetPanelSpacing(),
                PanelPadding = ToThickness(GetPanelPaddingRect()),
            };
            m_gui.TilingNodeFocusRequested += OnTilingNodeFocusRequested;
            m_gui.TilingNodeCloseRequested += OnTilingNodeCloseRequested;
            m_gui.TabSplitRequested += OnTabSplitRequested;
            m_gui.WrapInContainerRequested += OnWrapInContainerRequested;
            m_gui.MoveToContainerRequested += OnMoveToContainerRequested;
            m_gui.RenameContainerRequested += OnRenameContainerRequested;
            m_gui.GroupColorRequested += OnGroupColorRequested;
            m_gui.UnpackContainerRequested += OnUnpackContainerRequested;
            m_gui.GroupMoved += OnGroupMoved;
            m_gui.DragCanceled += () => ClearExternalDropPreview?.Invoke();
            m_gui.DividerResized += OnDividerResized;
            m_gui.FloatRequested += OnWindowFloatRequested;
            m_gui.AlwaysFloatRequested += OnAlwaysFloatRequested;
            m_gui.IsAlwaysFloating = m_floating.IsAlwaysFloating;
            m_floating.Changed += OnFloatingChanged;

            AutoRegisterWindows = autoRegisterWindows;

            var desktops = m_workspace.VirtualDesktopManager.Desktops.ToArray();
            foreach (var desktop in m_backend.Desktops.Except(desktops).ToArray())
                m_backend.UnregisterDesktop(desktop);
            foreach (var desktop in desktops.Except(m_backend.Desktops))
                OnDesktopAdded(this, new DesktopChangedEventArgs(desktop));

            m_workspace.VirtualDesktopManager.DesktopAdded += OnDesktopAdded;
            m_workspace.VirtualDesktopManager.DesktopRemoved += OnDesktopRemoved;
            m_workspace.VirtualDesktopManager.CurrentDesktopChanged += OnCurrentDesktopChanged;

            m_display.ScalingChanged += OnDisplayScalingChanged;
            m_display.WorkAreaChanged += OnDisplayWorkAreaChanged;
            GroupNumbers.Renumbered += OnGroupsRenumbered;
            m_display.BoundsChanged += OnDisplayWorkAreaChanged;
            m_workspace.CursorLocationChanged += OnNativeDragCursorChanged;

            m_workspace.FocusedWindowChanged += OnWorkspaceFocusChanged;
            m_workspace.WindowAdded += OnWindowAdded;
            m_workspace.WindowRemoved += OnWindowRemoved;

            PlacementFailed += OnPlacementFailed;

            m_subscriptions.Add(m_gui);
            m_subscriptions.Add(settings.Subscribe(OnSettingsChanged));


            foreach (var window in m_workspace.GetSnapshot())
                OnWindowAdded(window, new WindowChangedEventArgs(window));
        }

        private void OnSettingsChanged(ITilingServiceSettings x)
        {
            _ = m_dispatcher.RunAsync(() =>
            {
                m_autoFloatNewWindows = x.AutoFloatNewWindows;
                m_windowPadding = x.WindowPadding;
                m_panelHeight = x.PanelHeight;
                UpdateLayoutSpacing();

            });
        }

        public void Start()
        {
            m_active = true;
            InvalidateLayout();
            if (!m_workspacePaused)
                m_gui.Show();
        }

        public void Stop()
        {
            m_active = false;
            if (!m_workspacePaused)
                RestoreOriginalLayout();
            m_gui.Hide();
        }

        public void SetWorkspacePaused(bool paused)
        {
            if (m_workspacePaused == paused)
                return;

            m_workspacePaused = paused;
            if (paused)
            {
                m_gui.PreviewRectangle = null;
                using (m_ignoreRepositionSetLock.EnterScope())
                {
                    m_ignoreRepositionSet.Clear();
                }
                m_gui.Hide();
                return;
            }

            // Hidden windows can temporarily disappear from WinMan while the session is locked.
            // Drop only those which are still missing once the session is usable again.
            var visible = m_workspace.GetSnapshot().ToHashSet();
            List<IWindow> tracked;
            using (m_windowSetLock.EnterScope())
            {
                tracked = [.. m_windowSet];
            }
            using (m_backendLock.EnterScope())
            {
                tracked.AddRange(m_backend.Trees.SelectMany(tree => tree.Root!.Windows)
                    .Select(node => node.WindowReference).Except(tracked));
            }
            foreach (var window in tracked)
            {
                if (!visible.Contains(window))
                    OnWindowRemoved(m_workspace, new WindowChangedEventArgs(window));
            }

            if (m_workspace.FocusedWindow is IWindow focused && visible.Contains(focused))
                OnWindowGotFocus(focused, new WindowFocusChangedEventArgs(focused, true));

            if (Active)
            {
                m_gui.Show();
                InvalidateLayout();
            }
        }

        public void CycleTab(int offset)
        {
            WindowNode tab;
            using (m_backendLock.EnterScope())
            {
                SyncFocusToForeground();
                tab = m_backend.GetCycledTab(m_workspace.VirtualDesktopManager.CurrentDesktop, offset)
                    ?? throw new TilingFailedException(TilingError.MissingAdjacentWindow);
                SetShortcutFocus(tab);
            }

            FocusHelper.ForceActivate(tab.WindowReference.Handle);
            InvalidateLayout();
        }

        public void CycleGroup(int offset)
        {
            if (!Active || m_workspacePaused)
                return;
            WindowNode? tab;
            using (m_backendLock.EnterScope())
            {
                SyncFocusToForeground();
                tab = m_backend.GetCycledGroupTab(m_workspace.VirtualDesktopManager.CurrentDesktop, offset);
                if (tab == null)
                    return;
                SetShortcutFocus(tab);
            }
            FocusHelper.ForceActivate(tab.WindowReference.Handle);
            InvalidateLayout();
        }

        public void CreateGroup()
        {
            if (!Active || m_workspacePaused)
                return;
            using (m_backendLock.EnterScope())
            {
                SyncFocusToForeground();
                if (m_backend.GetFocus(m_workspace.VirtualDesktopManager.CurrentDesktop) is not WindowNode tab ||
                    m_backend.WrapInContainer(tab) == null)
                    return;
                SetShortcutFocus(tab);
            }
            InvalidateLayout();
        }

        public void UnpackGroups()
        {
            if (!Active || m_workspacePaused)
                return;
            using (m_backendLock.EnterScope())
            {
                if (!m_backend.UnpackGroups(m_workspace.VirtualDesktopManager.CurrentDesktop))
                    return;
            }
            InvalidateLayout();
        }

        public void MoveGroup(int offset)
        {
            if (!Active || m_workspacePaused)
                return;
            using (m_backendLock.EnterScope())
            {
                SyncFocusToForeground();
                var desktop = m_workspace.VirtualDesktopManager.CurrentDesktop;
                if (!m_backend.MoveFocusedGroup(desktop, offset))
                    throw new TilingFailedException(TilingError.NoGroupInDirection);
            }
            InvalidateLayout();
        }

        public void TileTab(TilingDirection direction)
        {
            WindowNode? tab;
            using (m_backendLock.EnterScope())
            {
                SyncFocusToForeground();
                tab = m_backend.GetFocus(m_workspace.VirtualDesktopManager.CurrentDesktop) as WindowNode
                    ?? throw new TilingFailedException(TilingError.MissingTarget);
            }
            OnTabSplitRequested(tab, direction);
        }

        public void MoveTab(int offset)
        {
            using (m_backendLock.EnterScope())
            {
                SyncFocusToForeground();
                if (!m_backend.MoveFocusedTab(m_workspace.VirtualDesktopManager.CurrentDesktop, offset))
                    return;
            }

            InvalidateLayout();
        }

        public bool MoveTabToGroup(int groupNumber)
        {
            using (m_backendLock.EnterScope())
            {
                SyncFocusToForeground();
                if (!m_backend.MoveFocusedTabToGroup(m_workspace.VirtualDesktopManager.CurrentDesktop, groupNumber))
                    return false;
            }

            InvalidateLayout();
            return true;
        }

        public bool DiscoverWindows()
        {
            if (m_workspacePaused || !AutoRegisterWindows)
            {
                return false;
            }

            List<IWindow> windows;
            using (m_windowSetLock.EnterScope())
            {
                windows = [.. m_windowSet];
            }

            bool anyChanges = false;
            foreach (var window in windows)
            {
                using (m_backendLock.EnterScope())
                {
                    try
                    {
                        if (!m_backend.HasWindow(window) && !IsInWindowGesture(window) && window.State == WindowState.Restored && CanManage(window))
                        {
                            m_logger.Debug("Discovered window {Window}", window.DebugString());
                            RegisterTab(window);
                            InvalidateLayout();
                            anyChanges = true;
                        }
                    }
                    catch (NoValidPlacementExistsException)
                    {
                        PlacementFailed?.Invoke(this, new TilingFailedEventArgs(
                            TilingError.NoValidPlacementExists, window));
                    }
                    catch (InvalidWindowReferenceException)
                    {
                        if (m_backend.HasWindow(window))
                            m_backend.UnregisterWindow(window);
                    }
                }
            }

            return anyChanges;
        }

        public void Refresh()
        {
            if (m_workspacePaused)
                return;

            List<IWindow> windows;
            using (m_windowSetLock.EnterScope())
            {
                windows = [.. m_windowSet];
            }

            bool anyChanges = false;
            foreach (var window in windows)
            {
                if (DetectChanges(window))
                {
                    anyChanges = true;
                }
            }

            if (anyChanges)
            {
                InvalidateLayout();
            }

            List<IWindow> movedWindows = [];

            using (m_backendLock.EnterScope())
            {
                foreach (var desktop in m_workspace.VirtualDesktopManager.Desktops)
                {
                    var tree = m_backend.GetTree(desktop);
                    if (tree == null)
                        continue;
                    foreach (var window in windows)
                    {
                        if (tree.FindNode(window) != null && !desktop.HasWindow(window))
                        {
                            movedWindows.Add(window);
                        }
                    }
                }
            }

            foreach (var movedWindow in movedWindows)
            {
                OnWindowRemoved(movedWindow, new WindowChangedEventArgs(movedWindow));
                OnWindowAdded(movedWindow, new WindowChangedEventArgs(movedWindow));
            }
        }


        public async Task ToggleDesktopAsync()
        {
            if (m_active)
            {
                Stop();
                await Task.Delay(50);
                foreach (var window in m_workspace.GetCurrentDesktopSnapshot())
                {
                    try
                    {
                        if (window.CanMinimize)
                            window.SetState(WindowState.Minimized);
                    }
                    catch (Exception e) when (e is Win32Exception || e is InvalidWindowReferenceException)
                    {
                        // Ignore
                    }
                }
            }
            else
            {
                foreach (var window in m_workspace.GetCurrentDesktopSnapshot())
                {
                    try
                    {
                        if (window.CanMinimize)
                            window.SetState(WindowState.Restored);
                    }
                    catch (Exception e) when (e is Win32Exception || e is InvalidWindowReferenceException)
                    {
                        // Ignore
                    }
                }
                await Task.Delay(50);
                Start();
                Refresh();
            }
        }

        public void Dispose() => Dispose(preserveWorkspace: false);

        internal void Dispose(bool preserveWorkspace)
        {
            m_logger.Information("No longer managing display {Display}", m_display);

            m_active = false;
            m_subscriptions.Dispose();

            PlacementFailed = null;
            m_floating.Changed -= OnFloatingChanged;

            m_workspace.VirtualDesktopManager.DesktopAdded -= OnDesktopAdded;
            m_workspace.VirtualDesktopManager.DesktopRemoved -= OnDesktopRemoved;
            m_workspace.VirtualDesktopManager.CurrentDesktopChanged -= OnCurrentDesktopChanged;

            m_workspace.FocusedWindowChanged -= OnWorkspaceFocusChanged;
            m_workspace.WindowAdded -= OnWindowAdded;
            m_workspace.WindowRemoved -= OnWindowRemoved;

            m_display.ScalingChanged -= OnDisplayScalingChanged;
            m_display.WorkAreaChanged -= OnDisplayWorkAreaChanged;
            GroupNumbers.Renumbered -= OnGroupsRenumbered;
            m_display.BoundsChanged -= OnDisplayWorkAreaChanged;
            m_workspace.CursorLocationChanged -= OnNativeDragCursorChanged;

            if (!preserveWorkspace)
            {
                using (m_backendLock.EnterScope())
                {
                    foreach (var desktop in m_backend.Desktops.ToArray())
                        m_backend.UnregisterDesktop(desktop);
                }
            }

            // There is still the possibility that OnWindowAdded gets called, but hopefully that does not happen too often.
            using (m_windowSetLock.EnterScope())
            {
                foreach (var window in m_windowSet)
                {
                    UnbindEventHandlers(window);
                }
            }
        }

        public Rectangle GetBounds()
        {
            return m_display.Bounds;
        }
    }
}
