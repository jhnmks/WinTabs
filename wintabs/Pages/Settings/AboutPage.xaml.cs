using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
            using var stream = typeof(AboutPage).Assembly.GetManifestResourceStream("WinTabs.UserManual.md");
            if (stream == null)
            {
                Manual.Children.Add(new TextBlock { Text = "The user guide is unavailable." });
                return;
            }

            using var reader = new StreamReader(stream);
            RenderManual(reader);
        }

        // The bundled guide uses headings, paragraphs, bullet lists, bold text, and inline shortcuts.
        private void RenderManual(TextReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("# ") || line.StartsWith("## "))
                {
                    bool title = line.StartsWith("# ");
                    var heading = new TextBlock
                    {
                        Text = line[(title ? 2 : 3)..],
                        Style = (Style)FindResource(title ? "SubtitleTextBlockStyle" : "SettingsSectionStyle"),
                        TextWrapping = TextWrapping.Wrap,
                    };
                    System.Windows.Automation.AutomationProperties.SetHeadingLevel(heading, title
                        ? System.Windows.Automation.AutomationHeadingLevel.Level1
                        : System.Windows.Automation.AutomationHeadingLevel.Level2);
                    Manual.Children.Add(heading);
                    continue;
                }

                bool bullet = line.StartsWith("- ");
                var text = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(bullet ? 8 : 0, 4, 0, bullet ? 2 : 6),
                };
                if (bullet)
                    text.Inlines.Add("• ");

                foreach (var part in Regex.Split(bullet ? line[2..] : line, @"(\*\*[^*]+\*\*|`[^`]+`)"))
                {
                    if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                        text.Inlines.Add(new Bold(new Run(part[2..^2])));
                    else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                        text.Inlines.Add(new Run(part[1..^1]) { FontWeight = FontWeights.SemiBold });
                    else
                        text.Inlines.Add(part);
                }
                Manual.Children.Add(text);
            }
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
