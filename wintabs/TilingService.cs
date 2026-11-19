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

        private void SetWindowPadding(int value)
        {
            m_windowPadding = value;
            PropagatePaddingChange();
        }

        private void SetPanelHeight(int value)
        {
            m_panelHeight = value;
            PropagatePanelHeightChange();
        }


        public IWorkspace Workspace => m_workspace;

        internal TilingWorkspace TabWorkspace => m_backend;
        internal Action<TilingService, StackPanelNode, Point, bool>? ExternalGroupDrag { get; set; }
        internal Action? ClearExternalGroupPreview { get; set; }

        public IReadOnlyCollection<IWindowMatcher> ExclusionMatchers
        {
            get => m_exclusionMatchers;
            set
            {
                m_exclusionMatchers = [.. value];

                using (m_windowSetLock.EnterScope())
                {
                    foreach (var window in m_windowSet)
                    {
                        if (m_exclusionMatchers.Any(x => x.Matches(window)))
                        {
                            using (m_floatingSetLock.EnterScope())
                            {
                                m_floatingSet.Add(window);
                            }
                        }
                    }
                }
                Refresh();
            }
        }

        private static readonly TimeSpan LockThreshold = TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// The dispatcher from the thread that created the <see cref="TilingService"/>
        /// </summary>
        private readonly Dispatcher m_dispatcher;
        private readonly IWorkspace m_workspace;
        private readonly ILogger m_logger = App.Current.Logger;
        private IReadOnlyCollection<IWindowMatcher> m_exclusionMatchers = [];

        private readonly TilingOverlayRenderer m_gui;
        private readonly IDisplay m_display;

        private readonly TilingWorkspace m_backend;
        private readonly Utilities.DebugLock m_backendLock = new(LockThreshold);


        private readonly HashSet<IWindow> m_windowSet = [];
        private readonly Utilities.DebugLock m_windowSetLock = new(LockThreshold);

        private readonly HashSet<IWindow> m_floatingSet = [];
        private readonly Utilities.DebugLock m_floatingSetLock = new(LockThreshold);

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

        public TilingService(IWorkspace workspace, IDisplay display, IObservable<ITilingServiceSettings> settings, bool autoRegisterWindows, TilingWorkspace? existingWorkspace = null, bool workspacePaused = false)
        {
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
            m_gui.GroupMoved += OnGroupMoved;
            m_gui.DragCanceled += () => ClearExternalGroupPreview?.Invoke();
            m_gui.DividerResized += OnDividerResized;
            m_gui.FloatRequested += OnWindowFloatRequested;
            m_gui.IgnoreProcessRequested += OnWindowIgnoreProcessRequested;
            m_gui.IgnoreClassRequested += OnWindowIgnoreClassRequested;

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
                SetWindowPadding(x.WindowPadding);
                SetPanelHeight(x.PanelHeight);

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

        public bool CanMoveFocus(TilingDirection direction)
        {
            return HasFocusAndAdjacentWindow(direction);
        }

        public void MoveFocus(TilingDirection direction)
        {
            using (m_backendLock.EnterScope())
            {
                var adjacentWindow = m_backend.GetFocusAdjacentWindow(m_workspace.VirtualDesktopManager.CurrentDesktop, direction);
                if (FocusHelper.ForceActivate(adjacentWindow.WindowReference.Handle))
                {
                    m_backend.SetFocus(adjacentWindow);
                }
            }

            InvalidateLayout();
        }

        public bool CanMoveWindow(TilingDirection direction)
        {
            return HasFocusAndAdjacentWindow(direction);
        }

        public void MoveWindow(TilingDirection direction)
        {
            using (m_backendLock.EnterScope())
            {
                var focusedNode = m_backend.GetFocus(m_workspace.VirtualDesktopManager.CurrentDesktop) ?? throw new TilingFailedException(TilingError.MissingTarget);
                WindowNode? adjacentWindow = focusedNode.GetAdjacentWindow(direction) ?? throw new TilingFailedException(TilingError.MissingAdjacentWindow);
                var adjancentWindowIndex = adjacentWindow.Parent!.IndexOf(adjacentWindow);

                if (adjacentWindow.Parent == focusedNode.Parent)
                {
                    var focusedNodeIndex = focusedNode.Parent.IndexOf(focusedNode);
                    var adjacentNodeIndex = focusedNode.Parent.IndexOf(adjacentWindow);
                    focusedNode.Parent.Move(focusedNodeIndex, adjacentNodeIndex);
                }
                else
                {

                    if (direction == TilingDirection.Left || direction == TilingDirection.Up)
                    {
                        m_backend.MoveAfter(focusedNode, adjacentWindow);
                    }
                    else
                    {
                        m_backend.MoveBefore(focusedNode, adjacentWindow);
                    }
                }
            }

            InvalidateLayout();
        }

        public bool CanSwapFocus(TilingDirection direction)
        {
            return HasFocusAndAdjacentWindow(direction);
        }

        public void SwapFocus(TilingDirection direction)
        {
            using (m_backendLock.EnterScope())
            {
                (var currentWindow, var adjacentWindow) = m_backend.GetFocusAndAdjacentWindow(m_workspace.VirtualDesktopManager.CurrentDesktop, direction);
                currentWindow!.Swap(adjacentWindow);
            }
            InvalidateLayout();
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
                        if (!m_backend.HasWindow(window) && window.State == WindowState.Restored && CanManage(window))
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

        public bool CanFloat()
        {
            var window = m_workspace.FocusedWindow;
            return window != null && CanManage(window, ignoreFloating: true);
        }

        public void Float()
        {
            var window = m_workspace.FocusedWindow ?? throw new TilingFailedException(TilingError.MissingTarget);
            ToggleFloat(window);
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

        public void Dispose()
        {
            m_logger.Information("No longer managing display {Display}", m_display);

            m_active = false;
            m_subscriptions.Dispose();

            PlacementFailed = null;

            m_workspace.VirtualDesktopManager.DesktopAdded -= OnDesktopAdded;
            m_workspace.VirtualDesktopManager.DesktopRemoved -= OnDesktopRemoved;
            m_workspace.VirtualDesktopManager.CurrentDesktopChanged -= OnCurrentDesktopChanged;

            m_workspace.FocusedWindowChanged -= OnWorkspaceFocusChanged;
            m_workspace.WindowAdded -= OnWindowAdded;
            m_workspace.WindowRemoved -= OnWindowRemoved;

            m_display.ScalingChanged -= OnDisplayScalingChanged;
            m_display.WorkAreaChanged -= OnDisplayWorkAreaChanged;
            m_display.BoundsChanged -= OnDisplayWorkAreaChanged;
            m_workspace.CursorLocationChanged -= OnNativeDragCursorChanged;

            // There is still the possibility that OnWindowAdded gets called, but hopefully that does not happen too often.
            using (m_windowSetLock.EnterScope())
            {
                foreach (var window in m_windowSet)
                {
                    UnbindEventHandlers(window);
                }
            }
        }

        public IWindow? GetFocus()
        {
            using (m_backendLock.EnterScope())
            {
                var focusedNode = m_backend.GetFocus(m_workspace.VirtualDesktopManager.CurrentDesktop);
                if (focusedNode is not WindowNode focusedWindow)
                    return null;

                return focusedWindow.WindowReference;
            }
        }

        public Rectangle GetBounds()
        {
            return m_display.Bounds;
        }

        public IWindow? FindClosest(Point center)
        {
            static double Distance(Point point1, Point point2)
            {
                return Math.Pow(point1.X - point2.X, 2) + Math.Pow(point1.Y - point2.Y, 2);
            }

            using (m_backendLock.EnterScope())
            {
                var tree = m_backend.GetTree(m_workspace.VirtualDesktopManager.CurrentDesktop);
                var closestNode = tree!.Root!.Windows
                    .OrderBy(x => Distance(center, x.ComputedRectangle.Center))
                    .FirstOrDefault();

                if (closestNode != null)
                {
                    return closestNode.WindowReference;
                }
            }

            return null;
        }
    }
}
