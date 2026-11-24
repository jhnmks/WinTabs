using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using wintabs.Layouts.Tiling;
using wintabs.Utilities;
using wintabs.ViewModels;
using wintabs.Controls;
using WinMan;
using wintabs.Windows;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Threading;

namespace wintabs
{
    public class TilingOverlayRenderer : IDisposable
    {
        public event EventHandler<TilingNode>? TilingNodeFocusRequested;
        public event EventHandler<TilingNode>? TilingNodeCloseRequested;


        public event EventHandler<WindowNode>? FloatRequested;
        public event EventHandler<WindowNode>? AlwaysFloatRequested;
        public Func<IWindow, bool> IsAlwaysFloating { get; set; } = _ => false;

        public int PanelSpacing { get; set; }
        public Thickness PanelPadding { get; set; }

        public Rectangle? PreviewRectangle
        {
            get => m_previewRectangle;
            set
            {
                if (m_previewRectangle != value)
                {
                    OnSetPreviewRectangle(oldValue: m_previewRectangle, newValue: value);
                    m_previewRectangle = value;
                    m_overlay.SetDropPreviewVisible(value.HasValue);
                }
            }
        }

        public IWindow? IntentSourceWindow
        {
            get => m_intentSourceWindow;
            set
            {
                if (m_intentSourceWindow != value)
                {
                    m_intentSourceWindow = value;
                }
            }
        }

        internal event Action<WindowNode>? WrapInContainerRequested;
        internal event Action<WindowNode, StackPanelNode>? MoveToContainerRequested;
        internal event Action<StackPanelNode>? RenameContainerRequested;
        internal event Action<StackPanelNode>? UnpackContainerRequested;
        internal event Action? DragCanceled;
        internal event Action<WindowNode, TilingDirection>? TabSplitRequested;
        internal event Action<StackPanelNode, WinMan.Point, bool>? GroupMoved;
        internal event Action<SplitDividerViewModel, double>? DividerResized;

        private readonly OverlayHost m_overlay;
        private readonly DelegateCommand<TilingNodeViewModel> m_panelItemPrimaryActionCommand;
        private readonly DelegateCommand<TilingNodeViewModel> m_panelItemCloseActionCommand;
        private readonly IDisplay m_display;
        private readonly CompositeDisposable m_disposables = new();
        private bool m_isOverlayInit = false;
        private readonly TilingOverlayViewModel m_viewModel = new();
        private double m_panelHeight = 22.0;
        private double m_windowPadding = 4.0;
        private int m_panelFontSize = 12;
        private IReadOnlyCollection<TilingNode> m_previousSnapshot = [];
        private readonly Dictionary<TilingNode, TilingNodeViewModel> m_nodeViewModels = [];
        private string? m_focusBorderColor;
        private Rectangle? m_previewRectangle;
        private IWindow? m_intentSourceWindow;

        public TilingOverlayRenderer(IDisplay display, Func<IntPtr> overlayAnchorSource)
        {
            m_overlay = new OverlayHost(display)
            {
                AnchorSource = overlayAnchorSource
            };

            m_panelItemPrimaryActionCommand = new DelegateCommand<TilingNodeViewModel>(OnOverlayPanelItemClick);
            m_panelItemCloseActionCommand = new DelegateCommand<TilingNodeViewModel>(OnOverlayPanelItemCloseAction);
            m_display = display;
            m_display.ScalingChanged += OnDisplayScalingChanged;
            AppTheme.Changed += OnApplicationThemeChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            m_disposables.Add(App.Current.AppState.Settings
                .Subscribe(settings =>
                {
                    m_panelHeight = settings.PanelHeight;
                    m_windowPadding = settings.WindowPadding;
                    m_panelFontSize = settings.PanelFontSize;
                    m_focusBorderColor = settings.FocusBorderColor;
                    UpdateResources();
                }));
        }

        private void OnApplicationThemeChanged(object? sender, EventArgs e) => UpdateResources();

        // The Windows accent swatch follows the system accent color.
        private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) => UpdateResources();

        private void OnDisplayScalingChanged(object? sender, DisplayScalingChangedEventArgs e)
        {
            UpdateResources();
        }

        public void UpdateOverlay(IReadOnlyCollection<TilingNode> snapshot, IReadOnlyCollection<TilingNode> focusedPath, WindowNode? activeTab)
        {
            UpdateViewModels(snapshot, focusedPath);
            foreach (var (node, viewModel) in m_nodeViewModels)
            {
                // A container's tab is highlighted while the focused window is inside it.
                viewModel.IsActiveTab = ReferenceEquals(node, activeTab) ||
                    node is StackPanelNode { IsContainer: true } && ReferenceEquals(node, activeTab?.Parent);
                if (viewModel is TilingPanelViewModel panel)
                    panel.HasFocus = ReferenceEquals(node, activeTab?.Parent) || activeTab != null && ReferenceEquals(node, TilingWorkspace.GetGroup(activeTab));
            }

            UpdateDividers(snapshot.OfType<SplitPanelNode>());
            if (!m_isOverlayInit)
            {
                m_isOverlayInit = true;

                var overlayView = new TilingOverlay
                {
                    ViewModel = m_viewModel
                };
                overlayView.DividerResized += (divider, delta) => DividerResized?.Invoke(divider, delta);
                m_overlay.Content = overlayView;

                m_overlay.NonHitTestableContent = new NonHitTestableTilingOverlay
                {
                    ViewModel = m_viewModel
                };
            }
        }

        private void UpdateDividers(IEnumerable<SplitPanelNode> splits)
        {
            var dividers = new List<SplitDividerViewModel>();
            int thickness = Math.Max(6, (int)(6 * m_display.Scaling));
            foreach (var split in splits)
            {
                var bounds = split.ComputedRectangle;
                for (int i = 0; i < split.Children.Count - 1; i++)
                {
                    var child = split.Children[i].ComputedRectangle;

                    var rect = split.Orientation == PanelOrientation.Horizontal
                        ? Rectangle.OffsetAndSize(child.Right + split.Children[i].Padding.Right - thickness / 2,
                            bounds.Top, thickness, bounds.Height)
                        : Rectangle.OffsetAndSize(bounds.Left, child.Bottom + split.Children[i].Padding.Bottom - thickness / 2,
                            bounds.Width, thickness);
                    if (rect.Width > 0 && rect.Height > 0)
                        dividers.Add(new SplitDividerViewModel(AdjustForDisplay(rect), split, i));
                }
            }
            m_viewModel.SetDividers(dividers);
        }

        private void UpdateResources()
        {
            if (m_overlay.Dispatcher.Thread != Thread.CurrentThread)
            {
                m_overlay.Dispatcher.BeginInvoke(() => UpdateResources());
                return;
            }

            m_viewModel.DisplayScaling = m_display.Scaling;
            m_viewModel.FocusBorderBrush = wintabs.Models.FocusBorderPalette.Find(m_focusBorderColor)
                .CreateBrush(AppTheme.IsDark);
            m_viewModel.FontSize = m_display.Scaling * m_panelFontSize;
            m_viewModel.IconSize = m_display.Scaling * m_panelFontSize;
        }

        private Rectangle AdjustForDisplay(Rectangle rectangle)
        {
            var bounds = m_display.Bounds;
            return new Rectangle(rectangle.Left - bounds.Left, rectangle.Top - bounds.Top, rectangle.Right - bounds.Left, rectangle.Bottom - bounds.Top);
        }

        private void UpdateViewModels(IReadOnlyCollection<TilingNode> snapshot, IReadOnlyCollection<TilingNode> focusedPath)
        {
            (var addList, var removeList, var persistList) = m_previousSnapshot.Changes(snapshot);
            m_previousSnapshot = snapshot;

            foreach (var removedNode in removeList)
            {
                if (m_nodeViewModels.TryGetValue(removedNode, out var vm))
                {
                    m_nodeViewModels.Remove(removedNode);
                    switch (vm)
                    {
                        case TilingPanelViewModel panelViewModel:
                            m_viewModel.PanelElements.Remove(panelViewModel);
                            break;
                        case TilingWindowViewModel windowViewModel:
                            m_viewModel.WindowElements.Remove(windowViewModel);
                            break;
                        default:
                            continue;
                    }
                    vm.Dispose();
                }
            }

            foreach (var addedNode in addList)
            {
                var vm = CreateViewModel(addedNode, focusedPath);
                switch (vm)
                {
                    case TilingPanelViewModel panelViewModel:
                        m_viewModel.PanelElements.Add(panelViewModel);
                        break;
                    case TilingWindowViewModel windowViewModel:
                        m_viewModel.WindowElements.Add(windowViewModel);
                        break;
                    default:
                        continue;
                }
            }

            foreach (var persistedNode in persistList.Concat(addList))
            {
                var vm = GetViewModel(persistedNode);
                switch (vm)
                {
                    case TilingPanelViewModel panelViewModel:
                        UpdateViewModel(panelViewModel, (PanelNode)persistedNode, focusedPath);
                        break;
                    case TilingWindowViewModel windowViewModel:
                        UpdateViewModel(windowViewModel, (WindowNode)persistedNode, focusedPath);
                        break;
                    default:
                        continue;
                }
            }
        }

        private TilingNodeViewModel? GetViewModel(TilingNode node)
        {
            if (m_nodeViewModels.TryGetValue(node, out var vm))
            {
                return vm;
            }
            return null;
        }

        private TilingNodeViewModel? CreateViewModel(TilingNode node, IEnumerable<TilingNode> focusedPath)
        {
            switch (node)
            {
                case StackPanelNode panelNode:
                    var panelViewModel = new TilingPanelViewModel
                    {
                        SelectUngroupedCommand = new ViewModels.DelegateCommand(_ =>
                        {
                            if (panelNode.UngroupedSelectedTab is { } tab)
                                TilingNodeFocusRequested?.Invoke(this, tab);
                        }),
                        RenameCommand = new ViewModels.DelegateCommand(_ => RenameContainerRequested?.Invoke(panelNode)),
                        UnpackCommand = new ViewModels.DelegateCommand(_ => UnpackContainerRequested?.Invoke(panelNode)),
                        BeginGroupDrag = (start, click) => m_overlay.BeginDrag(start,
                            (point, commit) => GroupMoved?.Invoke(panelNode.Parent is StackPanelNode outer ? outer : panelNode, point, commit),
                            click, () =>
                            {
                                PreviewRectangle = null;
                                DragCanceled?.Invoke();
                            }),
                    };
                    UpdateViewModel(panelViewModel, panelNode, focusedPath);
                    m_nodeViewModels.Add(node, panelViewModel);
                    return panelViewModel;
                case WindowNode windowNode:
                    var windowViewModel = new TilingWindowViewModel
                    {
                        SplitTabCommand = new ViewModels.DelegateCommand(direction =>
                        {
                            if (direction is string value && Enum.TryParse<TilingDirection>(value, out var edge))
                                TabSplitRequested?.Invoke(windowNode, edge);
                        }, _ => TilingWorkspace.GetGroup(windowNode)?.Windows.Skip(1).Any() == true),
                        MoveToContainer = container => MoveToContainerRequested?.Invoke(windowNode, container),
                        WrapInContainerCommand = new ViewModels.DelegateCommand(_ => WrapInContainerRequested?.Invoke(windowNode)),
                    };
                    windowViewModel.FloatActionPressed += WindowViewModel_FloatActionPressed;
                    windowViewModel.AlwaysFloatPressed += WindowViewModel_AlwaysFloatPressed;
                    windowViewModel.IsAlwaysFloating = window => IsAlwaysFloating(window);
                    UpdateViewModel(windowViewModel, windowNode, focusedPath);
                    m_nodeViewModels.Add(node, windowViewModel);
                    return windowViewModel;
                default:
                    return null;
            }
        }

        internal (StackPanelNode Group, Rectangle Bounds)? FindGroupTabAt(WinMan.Point point)
        {
            if (m_overlay.Content is not TilingOverlay view || PresentationSource.FromVisual(view) == null)
                return null;

            var local = view.PointFromScreen(new System.Windows.Point(point.X, point.Y));
            var tab = FindGroupTab(view, local);
            if (tab?.DataContext is not TilingPanelViewModel { Node: StackPanelNode group })
                return null;

            var topLeft = tab.PointToScreen(new System.Windows.Point());
            var bottomRight = tab.PointToScreen(new System.Windows.Point(tab.ActualWidth, tab.ActualHeight));
            return (group, new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y));
        }

        internal bool IsOnUngroupedTab(WinMan.Point point) =>
            m_overlay.Content is TilingOverlay view && PresentationSource.FromVisual(view) != null &&
            FindVisibleDropElement(view, view.PointFromScreen(new System.Windows.Point(point.X, point.Y)), IsUngroupedTab) != null;

        private static bool IsUngroupedTab(FrameworkElement element) =>
            element is System.Windows.Controls.Button { Name: "UngroupedDropTab" };

        internal static TilingNodeTab? FindGroupTab(FrameworkElement view, System.Windows.Point point) =>
            FindVisibleDropElement(view, point, element =>
                element is TilingNodeTab { DataContext: TilingPanelViewModel { IsContainer: true } }) as TilingNodeTab;

        private static FrameworkElement? FindVisibleDropElement(FrameworkElement view, System.Windows.Point point,
            Predicate<FrameworkElement> matches)
        {
            return FindVisibleTab(view);

            FrameworkElement? FindVisibleTab(FrameworkElement element)
            {
                if (element.Visibility != Visibility.Visible)
                    return null;
                if (element is TilingNodeTab || IsUngroupedTab(element) || element.ClipToBounds || element is System.Windows.Controls.ScrollContentPresenter)
                {
                    var bounds = new Rect(element.RenderSize);
                    if (element != view)
                        bounds = element.TransformToAncestor(view).TransformBounds(bounds);
                    if (!bounds.Contains(point))
                        return null;
                }
                if (matches(element))
                    return element;

                for (int i = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element) - 1; i >= 0; i--)
                    if (System.Windows.Media.VisualTreeHelper.GetChild(element, i) is FrameworkElement child && FindVisibleTab(child) is { } hit)
                        return hit;
                return null;
            }
        }

        internal void Show()
        {
            m_overlay.Show();
        }

        internal void Hide()
        {
            m_overlay.Hide();
        }

        private void WindowViewModel_FloatActionPressed(object sender, RoutedEventArgs e)
        {
            FloatRequested?.Invoke(this, (WindowNode)((TilingWindowViewModel)sender!).Node!);
        }

        private void WindowViewModel_AlwaysFloatPressed(object sender, RoutedEventArgs e)
        {
            AlwaysFloatRequested?.Invoke(this, (WindowNode)((TilingWindowViewModel)sender!).Node!);
        }

        private void OnSetPreviewRectangle(Rectangle? oldValue, Rectangle? newValue)
        {
            m_viewModel.PreviewRectangle = newValue.HasValue ? AdjustForDisplay(newValue.Value) : new Rectangle();
        }


        private void UpdateViewModel(TilingWindowViewModel vm, WindowNode node, IEnumerable<TilingNode> focusedPath)
        {
            vm.Overlay = m_viewModel;
            vm.Node = node;
            vm.Title = node.WindowReference.Title;
            vm.AppName = node.WindowReference.GetCachedAppName();
            vm.CanWrapInContainer = node.Parent is StackPanelNode { IsContainer: false };
            vm.HasFocus = node.Parent is StackPanelNode group ? group.SelectedChild == node : focusedPath.Contains(node);
            vm.ComputedBounds = AdjustForDisplay(node.ComputedRectangle);
            vm.PrimaryActionCommand = m_panelItemPrimaryActionCommand;
            vm.CloseCommand = m_panelItemCloseActionCommand;
            vm.ActionsHeight = m_panelHeight + 4;
            vm.RevealHighlightRadius = (16 + m_panelHeight + m_windowPadding) * 2;
        }

        private void UpdateViewModel(TilingPanelViewModel vm, PanelNode node, IEnumerable<TilingNode> focusedPath)
        {
            vm.Overlay = m_viewModel;
            vm.Node = node;
            vm.HasFocus = focusedPath.Contains(node);
            vm.ChildHasDirectFocus = node.Children.Contains(focusedPath.FirstOrDefault());
            vm.GroupNumber = node is StackPanelNode group ? group.GroupNumber : 0;
            vm.ComputedBounds = AdjustForDisplay(node.ComputedRectangle);
            vm.PrimaryActionCommand = m_panelItemPrimaryActionCommand;

            var container = node as StackPanelNode is { IsContainer: true } c ? c : null;
            vm.IsContainer = container != null;
            vm.CloseCommand = vm.IsContainer ? null : m_panelItemCloseActionCommand;
            vm.Title = container?.ContainerTitle ?? "";

            // A group's bar sits in the padding above its windows; a container's bar is the strip its own padding
            // reserves inside the group, and only shows while the container is the group's selected tab.
            var headerLeft = vm.ComputedBounds.Left - (node.Parent == null || container != null ? 0 : node.Padding.Left);
            var headerTop = vm.ComputedBounds.Top - (node.Parent == null ? 0 : node.Padding.Top);
            vm.HasGroupRow = node is StackPanelNode { HasContainers: true };
            vm.HasUngroupedTabs = vm.HasGroupRow && node.Children.OfType<WindowNode>().Any();
            vm.ShowUngroupedRow = vm.HasGroupRow && ((StackPanelNode)node).SelectedChild is WindowNode;
            vm.RowHeight = Math.Max(0, PanelPadding.Top - PanelSpacing);
            vm.RowGap = PanelSpacing;
            vm.HeaderBounds = Rectangle.OffsetAndSize(
                headerLeft + PanelSpacing / 2,
                headerTop + PanelSpacing / 2,
                (int)(vm.ComputedBounds.Width - PanelSpacing),
                (int)(vm.RowHeight + (vm.ShowUngroupedRow ? vm.RowHeight + vm.RowGap : 0)));

            vm.IsHeaderVisible = container != null
                ? ((StackPanelNode)container.Parent!).SelectedChild == container
                : !IsObscured(node, focusedPath);

            if (HaveChildrenChanged(vm, node))
            {
                var desired = node.Children.Select(GetViewModel).OfType<TilingNodeViewModel>().ToList();
                SyncTabs(vm.WindowTabs, desired.Where(x => x is TilingWindowViewModel));
                SyncTabs(vm.ChildNodes, desired);
            }
            if (node is StackPanelNode outer && vm.HasGroupRow)
            {
                var groups = vm.ChildNodes.OfType<TilingPanelViewModel>().ToArray();
                int index = vm.HasUngroupedTabs ? Math.Min(outer.UngroupedIndex, groups.Length) : groups.Length;
                SyncTabs(vm.OuterTabs, groups.Take(index));
                SyncTabs(vm.OuterTabsAfterUngrouped, groups.Skip(index));
            }
            else
            {
                vm.OuterTabs.Clear();
                vm.OuterTabsAfterUngrouped.Clear();
            }
        }

        private static void SyncTabs(System.Collections.ObjectModel.ObservableCollection<TilingNodeViewModel> tabs,
            IEnumerable<TilingNodeViewModel> desired)
        {
            var items = desired.ToList();
            for (int i = tabs.Count - 1; i >= 0; i--)
                if (!items.Contains(tabs[i]))
                    tabs.RemoveAt(i);
            for (int i = 0; i < items.Count; i++)
            {
                int oldIndex = tabs.IndexOf(items[i]);
                if (oldIndex < 0)
                    tabs.Insert(i, items[i]);
                else if (oldIndex != i)
                    tabs.Move(oldIndex, i);
            }
        }

        private bool HaveChildrenChanged(TilingPanelViewModel vm, PanelNode node)
        {
            if (vm.ChildNodes.Count != node.Children.Count)
            {
                return true;
            }
            int i = 0;
            foreach (var child in node.Children)
            {
                var childViewModel = GetViewModel(child);
                if (childViewModel == null)
                {
                    continue;
                }
                if (childViewModel != vm.ChildNodes[i])
                {
                    return true;
                }
                i++;
            }
            return false;
        }

        private static bool IsObscured(PanelNode node, IEnumerable<TilingNode> focusedPath)
        {
            if (focusedPath.Contains(node))
                return false;

            var stackAncestors = node.Ancestors
                .OfType<StackPanelNode>();

            if (!stackAncestors.Any())
                return false;

            return stackAncestors
                .All(x => focusedPath.Contains(x));
        }

        private void OnOverlayPanelItemClick(TilingNodeViewModel viewModel)
        {
            TilingNodeFocusRequested?.Invoke(this, viewModel.Node!);
        }

        private void OnOverlayPanelItemCloseAction(TilingNodeViewModel viewModel)
        {
            TilingNodeCloseRequested?.Invoke(this, viewModel.Node!);
        }

        public void InvalidateView()
        {
            m_viewModel.SetDividers([]);
            m_viewModel.PanelElements.Clear();
            m_viewModel.WindowElements.Clear();
            foreach (var vm in m_nodeViewModels)
            {
                vm.Value.Dispose();
            }
            m_nodeViewModels.Clear();
            m_previousSnapshot = [];
        }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
        public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
        {
            m_overlay.Close();
            m_viewModel.Dispose();
            m_display.ScalingChanged -= OnDisplayScalingChanged;
            AppTheme.Changed -= OnApplicationThemeChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            m_disposables.Dispose();
            InvalidateView();

            TilingNodeFocusRequested = null;
            TilingNodeCloseRequested = null;

            FloatRequested = null;
            AlwaysFloatRequested = null;
        }
    }
}
