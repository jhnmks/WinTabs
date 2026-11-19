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
        public event EventHandler<WindowNode>? IgnoreProcessRequested;
        public event EventHandler<WindowNode>? IgnoreClassRequested;

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
                viewModel.IsActiveTab = ReferenceEquals(node, activeTab);
                if (viewModel is TilingPanelViewModel panel)
                    panel.HasFocus = ReferenceEquals(node, activeTab?.Parent);
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
                        BeginGroupDrag = start => m_overlay.BeginDrag(start,
                            (point, commit) => GroupMoved?.Invoke(panelNode, point, commit),
                            () => { }, () =>
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
                        }, _ => windowNode.Parent?.Children.Count > 1),
                    };
                    windowViewModel.FloatActionPressed += WindowViewModel_FloatActionPressed;
                    windowViewModel.IgnoreClassPressed += WindowViewModel_IgnoreClassPressed;
                    windowViewModel.IgnoreProcessPressed += WindowViewModel_IgnoreProcessPressed;
                    UpdateViewModel(windowViewModel, windowNode, focusedPath);
                    m_nodeViewModels.Add(node, windowViewModel);
                    return windowViewModel;
                default:
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

        private void WindowViewModel_IgnoreProcessPressed(object sender, RoutedEventArgs e)
        {
            IgnoreProcessRequested?.Invoke(this, ((WindowNode)((TilingWindowViewModel)sender!).Node!));
        }

        private void WindowViewModel_IgnoreClassPressed(object sender, RoutedEventArgs e)
        {
            IgnoreClassRequested?.Invoke(this, ((WindowNode)((TilingWindowViewModel)sender!).Node!));
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
            vm.HasFocus = node.Parent is StackPanelNode group ? group.SelectedTab == node : focusedPath.Contains(node);
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
            vm.ComputedBounds = AdjustForDisplay(node.ComputedRectangle);
            vm.PrimaryActionCommand = m_panelItemPrimaryActionCommand;
            vm.CloseCommand = m_panelItemCloseActionCommand;

            var headerLeft = vm.ComputedBounds.Left - (node.Parent == null ? 0 : node.Padding.Left);
            var headerTop = vm.ComputedBounds.Top - (node.Parent == null ? 0 : node.Padding.Top);
            vm.HeaderBounds = Rectangle.OffsetAndSize(
                headerLeft + PanelSpacing / 2,
                headerTop + PanelSpacing / 2,
                (int)(vm.ComputedBounds.Width - PanelSpacing),
                (int)(PanelPadding.Top - PanelSpacing));

            vm.IsHeaderVisible = !IsObscured(node, focusedPath);

            if (HaveChildrenChanged(vm, node))
            {
                var desired = node.Children.Select(GetViewModel).OfType<TilingNodeViewModel>().ToList();
                for (int i = vm.ChildNodes.Count - 1; i >= 0; i--)
                {
                    if (!desired.Contains(vm.ChildNodes[i]))
                        vm.ChildNodes.RemoveAt(i);
                }
                for (int i = 0; i < desired.Count; i++)
                {
                    int currentIndex = vm.ChildNodes.IndexOf(desired[i]);
                    if (currentIndex < 0)
                        vm.ChildNodes.Insert(i, desired[i]);
                    else if (currentIndex != i)
                        vm.ChildNodes.Move(currentIndex, i);
                }
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
            IgnoreProcessRequested = null;
            IgnoreClassRequested = null;
        }
    }
}
