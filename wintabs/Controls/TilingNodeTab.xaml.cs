using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using wintabs.Models;
using wintabs.Utilities;

using wintabs.ViewModels;
using wintabs.Layouts.Tiling;

namespace wintabs.Controls
{
    public partial class TilingNodeTab : UserControl
    {
        private LowLevelMouseHook? m_mouseHook;

        public TilingNodeTab()
        {
            InitializeComponent();
            Unloaded += (_, _) =>
            {
                TabMenu.IsOpen = false;
                StopWatchingMenuDismissal();
            };
        }

        private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || DataContext is not TilingPanelViewModel { IsContainer: true, RenameCommand: { } rename } ||
                !rename.CanExecute(null))
                return;

            e.Handled = true;
            rename.Execute(null);
        }

        private void OnGroupNumberMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not TilingPanelViewModel { BeginGroupDrag: { } begin } group)
                return;
            var point = PointToScreen(e.GetPosition(this));
            begin(new WinMan.Point((int)point.X, (int)point.Y), () =>
            {
                if (group.PrimaryActionCommand?.CanExecute(group) == true)
                    group.PrimaryActionCommand.Execute(group);
            });
            e.Handled = true;
        }

        // A window's tab offers tiling and "Put in new Group"; a container's tab offers Rename and Unpack.
        private void OnMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu || !ReferenceEquals(e.Source, menu))
                return;
            if (m_mouseHook == null && Application.Current is App app)
            {
                m_mouseHook = app.Services.GetService<LowLevelMouseHook>();
                if (m_mouseHook != null)
                    m_mouseHook.ButtonStateChanged += OnMouseButtonChanged;
            }

            bool container = DataContext is TilingPanelViewModel { IsContainer: true };
            bool canWrap = DataContext is TilingNodeViewModel { CanWrapInContainer: true };
            var window = container ? Visibility.Collapsed : Visibility.Visible;
            TileLeft.Visibility = TileRight.Visibility = TileAbove.Visibility = TileBelow.Visibility = window;
            WrapItem.Visibility = canWrap ? Visibility.Visible : Visibility.Collapsed;
            RenameItem.Visibility = UnpackItem.Visibility = container ? Visibility.Visible : Visibility.Collapsed;
            GroupColorItem.Visibility = container ? Visibility.Visible : Visibility.Collapsed;
            GroupColorItem.Items.Clear();
            if (DataContext is TilingPanelViewModel { IsContainer: true, Node: StackPanelNode groupNode } panel)
            {
                AddColorOption("Default", null);
                foreach (var swatch in FocusBorderPalette.Swatches)
                    AddColorOption(swatch.Name, swatch.Name, swatch.CreateBrush(AppTheme.IsDark));

                void AddColorOption(string title, string? color, System.Windows.Media.Brush? brush = null)
                {
                    GroupColorItem.Items.Add(new MenuItem
                    {
                        Header = title,
                        IsCheckable = true,
                        IsChecked = color == groupNode.ContainerColor,
                        Icon = brush == null ? null : new Border { Background = brush, Width = 12, Height = 12, CornerRadius = new CornerRadius(2) },
                        Command = panel.GroupColorCommand,
                        CommandParameter = color,
                    });
                }
            }
            MoveToContainerItem.Items.Clear();
            if (DataContext is TilingWindowViewModel { Node: WindowNode tab, MoveToContainer: { } move } &&
                TilingWorkspace.GetGroup(tab) is { } group)
            {
                foreach (var destination in group.Children.OfType<StackPanelNode>().Where(x => x != tab.Parent))
                {
                    var item = new MenuItem
                    {
                        Header = destination.ContainerTitle,
                        IsEnabled = TilingWorkspace.CanAcceptTab(tab, destination, null),
                    };
                    item.Click += (_, _) => move(destination);
                    MoveToContainerItem.Items.Add(item);
                }
            }
            var hasDestinations = MoveToContainerItem.Items.Count != 0;
            MoveToContainerItem.Visibility = hasDestinations ? Visibility.Visible : Visibility.Collapsed;
            ContainerSeparator.Visibility = canWrap || hasDestinations ? Visibility.Visible : Visibility.Collapsed;
        }
        private void OnMenuClosed(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, e.Source))
                StopWatchingMenuDismissal();
        }

        private void StopWatchingMenuDismissal()
        {
            if (m_mouseHook == null)
                return;
            m_mouseHook.ButtonStateChanged -= OnMouseButtonChanged;
            m_mouseHook = null;
        }

        // The overlay does not take foreground activation, so clicks in another app may bypass WPF capture.
        private void OnMouseButtonChanged(object? sender, ref LowLevelMouseHook.ButtonStateChangedEventArgs e)
        {
            if (!e.IsPressed)
                return;
            var point = new Point(e.X, e.Y);
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                if (TabMenu.IsOpen && !ContainsClick(TabMenu, point))
                    TabMenu.IsOpen = false;
            });
        }

        private static bool ContainsClick(ItemsControl menu, Point screenPoint)
        {
            if (menu is ContextMenu && InPopup(menu, screenPoint))
                return true;

            for (int i = 0; i < menu.Items.Count; i++)
            {
                if (menu.ItemContainerGenerator.ContainerFromIndex(i) is not MenuItem { IsSubmenuOpen: true } item)
                    continue;
                if ((item.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement child && InPopup(child, screenPoint)) ||
                    ContainsClick(item, screenPoint))
                    return true;
            }
            return false;
        }

        private static bool InPopup(FrameworkElement element, Point screenPoint) =>
            PresentationSource.FromVisual(element)?.RootVisual is FrameworkElement root &&
            new Rect(root.RenderSize).Contains(root.PointFromScreen(screenPoint));
    }
}
