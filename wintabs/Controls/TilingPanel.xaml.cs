using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using wintabs.ViewModels;

namespace wintabs.Controls
{
    /// <summary>
    /// Interaction logic for TilingPanel.xaml
    /// </summary>
    public partial class TilingPanel : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TilingPanelViewModel),
            typeof(TilingPanel),
            new PropertyMetadata(null));

        public TilingPanelViewModel ViewModel
        {
            get => (TilingPanelViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public TilingPanel()
        {
            InitializeComponent();
        }

        private void OnGroupMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.BeginGroupDrag is not { } begin)
                return;

            var point = PointToScreen(e.GetPosition(this));
            begin(new WinMan.Point((int)point.X, (int)point.Y), () => { });
            e.Handled = true;
        }


        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ViewModelProperty)
            {
                DataContext = ViewModel;
            }
        }
    }
}
