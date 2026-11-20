using System.Windows;
using System.Windows.Controls;

using wintabs.ViewModels;

namespace wintabs.Pages.Settings
{
    public partial class ShortcutsPage : UserControl
    {
        public ShortcutsPage(SettingsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }

        private void OnShowCheatSheet(object sender, RoutedEventArgs e)
        {
            MainWindow.ShowCheatSheetFromUi();
        }
    }
}
