using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using wintabs.ViewModels;

namespace wintabs.Controls
{
    /// <summary>
    /// Interaction logic for TilingWindow.xaml
    /// </summary>
    public partial class TilingWindow : UserControl
    {

        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TilingWindowViewModel),
            typeof(TilingWindow),
            new PropertyMetadata(null));

        public TilingWindowViewModel ViewModel
        {
            get => (TilingWindowViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public TilingWindow()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ViewModelProperty)
            {
                DataContext = ViewModel;
                MoreContextMenu.IsOpen = false;
            }
        }

        private void OnMoreClick(object sender, RoutedEventArgs e)
        {
            MoreContextMenu.IsOpen = true;
            MoreContextMenu.DataContext = ViewModel;
            var child = (UIElement)VisualTreeHelper.GetChild(VisualTreeHelper.GetChild(MoreContextMenu, 0), 0);
            child.MouseEnter -= OnContextMenuMouseEnter;
            child.MouseEnter += OnContextMenuMouseEnter;
        }

        private void OnContextMenuMouseEnter(object sender, MouseEventArgs e)
        {
            var child = (UIElement)sender;
            child.MouseEnter -= OnContextMenuMouseEnter;
            child.MouseLeave -= OnContextMenuMouseLeave;
            child.MouseLeave += OnContextMenuMouseLeave;
        }

        private void OnContextMenuMouseLeave(object sender, MouseEventArgs e)
        {
            var child = (UIElement)sender;
            child.MouseLeave -= OnContextMenuMouseLeave;
            MoreContextMenu.IsOpen = false;
        }
    }
}
