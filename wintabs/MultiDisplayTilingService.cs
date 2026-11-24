using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Windows.Threading;

using wintabs.Models;
using wintabs.Utilities;

using WinMan;
using wintabs.Layouts.Tiling;
using Serilog;

namespace wintabs
{
    class MultiDisplayTilingService : ITilingService, IDisposable
    {
        public event EventHandler<TilingFailedEventArgs>? PlacementFailed;

        public Dispatcher Dispatcher { get; }
        public IWorkspace Workspace { get; }

        public bool Active => m_tilingServices.TryGetValue(m_activeDisplay, out var tiling) ? tiling.Active : m_enabled;
        private bool m_enabled;
        private bool m_workspacePaused;


        private readonly Dictionary<IDisplay, TilingService> m_tilingServices = [];
        private readonly CompositeDisposable m_subscriptions = [];
        private readonly Subject<Unit> m_focusedWindowLocationChanges = new();
        private readonly ILogger m_logger;
        private IDisplay m_activeDisplay;
        private readonly IObservable<ITilingServiceSettings> m_settings;
        private readonly FloatingWindows m_floating;
        private readonly object m_syncRoot = new();

        public MultiDisplayTilingService(IWorkspace workspace, IObservable<ITilingServiceSettings> settings, FloatingWindows floating)
        {
            Dispatcher = Dispatcher.CurrentDispatcher;
            Workspace = workspace;
            m_settings = settings;
            m_floating = floating;
            m_logger = App.Current.Logger;
            m_logger.Information($"Using the multi-monitor tiling backend");

            foreach (var display in Workspace.DisplayManager.Displays)
            {
                var tiling = new TilingService(Workspace, display, settings, m_floating, true);
                ConfigureDisplayService(tiling);
                m_tilingServices.Add(display, tiling);
            }

            m_activeDisplay = GetActiveDisplay() ?? Workspace.DisplayManager.PrimaryDisplay;
            m_tilingServices[m_activeDisplay].AutoRegisterWindows = true;
            m_tilingServices[m_activeDisplay].Refresh();

            Workspace.DisplayManager.Added += OnDisplayAdded;
            Workspace.DisplayManager.Removed += OnDisplayRemoved;

            Workspace.FocusedWindowChanged += OnFocusedWindowChanged;
            m_subscriptions.Add(Disposable.Create(() => Workspace.FocusedWindowChanged -= OnFocusedWindowChanged));

            var focusLocationObservable = m_focusedWindowLocationChanges
                .Throttle(TimeSpan.FromMilliseconds(100))
                .Do(async _ => await Dispatcher.InvokeAsync(() =>
                {
                    UpdateActiveDisplay(reason: "the focused window was moved");
                }));
            m_subscriptions.Add(focusLocationObservable.Subscribe());
        }

        private void ConfigureDisplayService(TilingService tiling)
        {
            tiling.PlacementFailed += OnTilingFailed;
            tiling.ExternalGroupDrag = OnExternalGroupDrag;
            tiling.ExternalTabDrag = OnExternalTabDrag;
            tiling.ClearExternalDropPreview = ClearExternalGroupPreview;
            tiling.IsTabElsewhere = window => m_tilingServices.Values.Any(x => x != tiling && x.HasWindow(window));
        }

        private TilingService? m_groupPreviewDisplay;

        private void ClearExternalGroupPreview()
        {
            m_groupPreviewDisplay?.ClearGroupPreview();
            m_groupPreviewDisplay = null;
        }

        // A group is dragged off its own display: the display under the cursor previews or takes the drop.
        private void OnExternalGroupDrag(TilingService source, StackPanelNode group, Point point, bool commit)
        {
            var target = m_tilingServices.Values.FirstOrDefault(tiling => tiling != source && tiling.GetBounds().Contains(point));
            if (target != m_groupPreviewDisplay)
                ClearExternalGroupPreview();
            m_groupPreviewDisplay = commit ? null : target;
            target?.PreviewOrDropGroupFrom(source, group, point, commit);
        }

        // A tab's window is dragged off its own display: the display under the cursor previews or takes the drop.
        private bool OnExternalTabDrag(TilingService source, WindowNode tab, Point point, bool commit)
        {
            var target = m_tilingServices.Values.FirstOrDefault(tiling => tiling != source && tiling.GetBounds().Contains(point));
            if (target != m_groupPreviewDisplay)
                ClearExternalGroupPreview();
            m_groupPreviewDisplay = commit ? null : target;
            return target?.PreviewOrDropTabFrom(source, tab, point, commit) == true;
        }

        private void OnDisplayRemoved(object? sender, DisplayChangedEventArgs e) => QueueDisplayUpdate();

        private void OnDisplayAdded(object? sender, DisplayChangedEventArgs e) => QueueDisplayUpdate();

        private void QueueDisplayUpdate() => Dispatcher.InvokeAsync(() =>
        {
            if (!m_workspacePaused && !m_subscriptions.IsDisposed)
                ReconcileDisplays();
        });

        private void ReconcileDisplays(bool resuming = false)
        {
            lock (m_syncRoot)
            {
                var displays = Workspace.DisplayManager.Displays;
                var returningWorkspaces = new Dictionary<IDisplay, TilingWorkspace>();
                foreach (var (display, tiling) in m_tilingServices.ToArray())
                {
                    if (displays.Any(current => ReferenceEquals(current, display)))
                        continue;

                    bool preserveWorkspace = displays.Contains(display);
                    if (preserveWorkspace)
                        returningWorkspaces.Add(display, tiling.TabWorkspace);
                    if (m_groupPreviewDisplay == tiling)
                        ClearExternalGroupPreview();
                    m_tilingServices.Remove(display);
                    tiling.Dispose(preserveWorkspace);
                }

                foreach (var display in displays)
                {
                    if (m_tilingServices.ContainsKey(display))
                        continue;

                    returningWorkspaces.TryGetValue(display, out var previous);
                    var tiling = new TilingService(Workspace, display, m_settings, m_floating, true, previous, workspacePaused: resuming);
                    ConfigureDisplayService(tiling);
                    m_tilingServices.Add(display, tiling);
                    if (m_enabled)
                        tiling.Start();
                }

                UpdateActiveDisplay();
            }
        }

        private void OnWindowPositionChanged(object? sender, WindowPositionChangedEventArgs e)
        {
            m_focusedWindowLocationChanges.OnNext(Unit.Default);
        }

        private void OnFocusedWindowChanged(object? sender, FocusedWindowChangedEventArgs e)
        {
            if (e.OldFocusedWindow != null)
            {
                e.OldFocusedWindow.PositionChanged -= OnWindowPositionChanged;
            }
            if (e.NewFocusedWindow == null)
            {
                return;
            }
            e.NewFocusedWindow.PositionChanged += OnWindowPositionChanged;

            _ = Dispatcher.InvokeAsync(() =>
            {
                UpdateActiveDisplay($"the focused window has changed to {e.NewFocusedWindow.DebugString()}");
            });
        }

        private void UpdateActiveDisplay(string? reason = null)
        {
            if (m_workspacePaused || m_subscriptions.IsDisposed)
                return;

            lock (m_syncRoot)
            {
                var newActiveDisplay = GetActiveDisplay() ?? Workspace.DisplayManager.PrimaryDisplay;
                if (!newActiveDisplay.Equals(m_activeDisplay))
                {
                    m_logger.Information($"Active display changed from {m_activeDisplay} to {newActiveDisplay}");
                    if (m_tilingServices.TryGetValue(m_activeDisplay, out var oldTiling))
                    {
                        oldTiling.AutoRegisterWindows = true;
                    }
                    else
                    {
                        m_logger.Warning($"Previous active display {m_activeDisplay} is not associated with a tiling service!");
                    }
                    m_activeDisplay = newActiveDisplay;
                    if (m_tilingServices.TryGetValue(m_activeDisplay, out var newTiling))
                    {
                        newTiling.AutoRegisterWindows = true;
                        // Changing focus does not change tab membership.
                    }
                    else
                    {
                        m_logger.Warning($"New active display {m_activeDisplay} is not associated with a tiling service!");
                    }
                    m_logger.Verbose($"Check triggered because {reason}.");
                }
            }
        }

        private IDisplay? GetActiveDisplay()
        {
            lock (m_syncRoot)
            {
                var window = Workspace.FocusedWindow;
                if (window == null)
                    return null;
                var display = Workspace.DisplayManager.Displays
                    .FirstOrDefault(x => x.Bounds.Contains(window.Position.Center));
                return display;
            }
        }

        private TilingService GetActiveTilingService()
        {
            lock (m_syncRoot)
            {
                return m_tilingServices[m_activeDisplay];
            }
        }


        private void OnTilingFailed(object? sender, TilingFailedEventArgs e)
        {
            PlacementFailed?.Invoke(this, e);
        }

        public void Dispose()
        {
            lock (m_syncRoot)
            {
                Workspace.DisplayManager.Added -= OnDisplayAdded;
                Workspace.DisplayManager.Removed -= OnDisplayRemoved;
                m_subscriptions.Dispose();
                foreach (var tiling in m_tilingServices.Values)
                    tiling.Dispose();
                m_tilingServices.Clear();
            }
        }

        public bool DiscoverWindows()
        {
            bool anyChanges = false;
            lock (m_syncRoot)
            {
                foreach (var tiling in m_tilingServices.Values)
                {
                    anyChanges |= tiling.DiscoverWindows();
                }
            }
            return anyChanges;
        }

        public void Refresh()
        {
            lock (m_syncRoot)
            {
                foreach (var tiling in m_tilingServices.Values)
                {
                    tiling.Refresh();
                }
            }
        }


        public void SetWorkspacePaused(bool paused)
        {
            if (m_workspacePaused == paused)
                return;

            m_workspacePaused = paused;
            if (!paused)
                ReconcileDisplays(resuming: true);

            foreach (var tiling in m_tilingServices.Values)
                tiling.SetWorkspacePaused(paused);
        }

        public void Stop()
        {
            m_enabled = false;
            lock (m_syncRoot)
            {
                foreach (var tiling in m_tilingServices.Values)
                {
                    tiling.Stop();
                }
            }
        }

        public void Start()
        {
            m_enabled = true;
            lock (m_syncRoot)
            {
                foreach (var tiling in m_tilingServices.Values)
                {
                    tiling.Start();
                }
            }
        }

        public void CycleTab(int offset)
        {
            GetFocusedTabService().CycleTab(offset);
        }

        public void CycleGroup(int offset) => GetFocusedTabService().CycleGroup(offset);

        public void MoveGroup(int offset) => GetFocusedTabService().MoveGroup(offset);

        public bool MoveTabToGroup(int groupNumber)
        {
            var source = GetFocusedTabService();
            TilingService? destination;
            lock (m_syncRoot)
            {
                destination = m_tilingServices.Values.FirstOrDefault(x => x.HasGroup(groupNumber));
            }

            if (destination == null)
                return false;
            return destination == source
                ? source.MoveTabToGroup(groupNumber)
                : source.TransferFocusedTabTo(destination, groupNumber);
        }

        public void TileTab(TilingDirection direction)
        {
            GetFocusedTabService().TileTab(direction);
        }

        /// <summary>
        /// The display service holding the focused tab; the "active" display can lag behind focus changes.
        /// </summary>
        private TilingService GetFocusedTabService()
        {
            lock (m_syncRoot)
            {
                // A display that just took focus through a shortcut wins: Windows may not report it as foreground yet.
                var justFocused = m_tilingServices.Values
                    .Where(x => x.RecentShortcutFocus != null)
                    .OrderByDescending(x => x.RecentShortcutFocus)
                    .FirstOrDefault();
                if (justFocused != null)
                    return justFocused;

                var foreground = Workspace.FocusedWindow;
                return (foreground == null ? null : m_tilingServices.Values.FirstOrDefault(x => x.HasWindow(foreground)))
                    ?? GetActiveTilingService();
            }
        }

        public void MoveTab(int offset)
        {
            GetFocusedTabService().MoveTab(offset);
        }

        public Task ToggleDesktopAsync() => GetActiveTilingService().ToggleDesktopAsync();

        public Rectangle GetBounds()
        {
            throw new NotSupportedException();
        }
    }
}
