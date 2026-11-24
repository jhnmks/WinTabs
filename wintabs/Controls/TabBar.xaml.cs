using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using wintabs.ViewModels;

namespace wintabs.Controls
{
    /// <summary>
    /// Interaction logic for TabBar.xaml
    /// </summary>
    public partial class TabBar : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(ObservableCollection<TilingNodeViewModel>),
            typeof(TabBar));

        public ObservableCollection<TilingNodeViewModel> ItemsSource
        {
            get => (ObservableCollection<TilingNodeViewModel>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty ShowUngroupedProperty = DependencyProperty.Register(
            nameof(ShowUngrouped), typeof(bool), typeof(TabBar));

        public bool ShowUngrouped
        {
            get => (bool)GetValue(ShowUngroupedProperty);
            set => SetValue(ShowUngroupedProperty, value);
        }

        private void OnUngroupedNumberMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not TilingPanelViewModel { BeginGroupDrag: { } begin } group)
                return;
            var point = PointToScreen(e.GetPosition(this));
            begin(new WinMan.Point((int)point.X, (int)point.Y), () =>
            {
                if (group.SelectUngroupedCommand?.CanExecute(null) == true)
                    group.SelectUngroupedCommand.Execute(null);
            });
            e.Handled = true;
        }

        public TabBar()
        {
            InitializeComponent();
        }
    }
}
