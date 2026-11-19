using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

using wintabs.ViewModels;

namespace wintabs.Pages.Settings
{
    public partial class AboutPage : UserControl
    {
        public AboutPage(SettingsViewModel _)
        {
            DataContext = new { Version = Startup.GetVersionString().Split('+')[0] };
            InitializeComponent();
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
