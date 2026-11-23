using System.Linq;
using System.Windows;
using System.Windows.Controls;

using wintabs.Utilities;
using wintabs.ViewModels;

namespace wintabs.Pages.Settings
{
    public partial class RulesPage : UserControl
    {
        private readonly SettingsViewModel m_viewModel;

        public RulesPage(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = m_viewModel = viewModel;
        }

        // Pick from the apps that are open right now; that is how users know them.
        private void OnAddAppClick(object sender, RoutedEventArgs e)
        {
            var listed = m_viewModel.AlwaysFloatingApps.Select(x => x.ProcessName).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
            RunningAppsMenu.Items.Clear();
            foreach (var app in AppNames.GetRunningApps().Where(x => !listed.Contains(x.ProcessName)))
            {
                var item = new MenuItem { Header = app.Name, ToolTip = app.ProcessName + ".exe" };
                item.Click += (_, _) => m_viewModel.AddAlwaysFloatingApp(app.ProcessName);
                RunningAppsMenu.Items.Add(item);
            }
            if (RunningAppsMenu.Items.Count == 0)
                RunningAppsMenu.Items.Add(new MenuItem { Header = "No other apps are open", IsEnabled = false });
            RunningAppsMenu.PlacementTarget = AddAppButton;
            RunningAppsMenu.IsOpen = true;
        }
    }
}
