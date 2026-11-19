using System.Windows.Controls;

using wintabs.ViewModels;

namespace wintabs.Pages.Settings
{
    /// <summary>
    /// Interaction logic for InterfacePage.xaml
    /// </summary>
    public partial class InterfacePage : UserControl
    {
        public InterfacePage(SettingsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}
