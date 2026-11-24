using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Media;

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

        // The bundled guide uses a small Markdown subset, rendered with native WPF controls.
        private void RenderManual(TextReader reader)
        {
            string? line = reader.ReadLine();
            while (line != null)
            {
                if (line.StartsWith("| "))
                {
                    var table = new Grid { Margin = new Thickness(0, 6, 0, 12) };
                    table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                    table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                    do
                    {
                        var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
                        if (cells.Length == 2 && !cells.All(cell => Regex.IsMatch(cell, @"^:?-+:?$")))
                            AddTableRow(table, cells);
                        line = reader.ReadLine();
                    } while (line?.StartsWith("| ") == true);
                    Manual.Children.Add(table);
                    continue;
                }

                if (line.StartsWith("# ") || line.StartsWith("## "))
                {
                    bool title = line.StartsWith("# ");
                    var heading = new TextBlock
                    {
                        Text = line[(title ? 2 : 3)..],
                        FontSize = title ? 30 : 22,
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    };
                    System.Windows.Automation.AutomationProperties.SetHeadingLevel(heading, title
                        ? System.Windows.Automation.AutomationHeadingLevel.Level1
                        : System.Windows.Automation.AutomationHeadingLevel.Level2);
                    var rule = new Border
                    {
                        Child = heading,
                        Padding = new Thickness(0, 0, 0, 8),
                        Margin = new Thickness(0, title ? 0 : 22, 0, 12),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                    };
                    rule.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                    Manual.Children.Add(rule);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    bool bullet = line.StartsWith("- ");
                    var text = FormatText(bullet ? line[2..] : line);
                    if (bullet)
                    {
                        var row = new DockPanel { Margin = new Thickness(12, 0, 0, 6) };
                        var marker = new TextBlock { Text = "•", Width = 18, LineHeight = 22 };
                        DockPanel.SetDock(marker, Dock.Left);
                        row.Children.Add(marker);
                        row.Children.Add(text);
                        Manual.Children.Add(row);
                    }
                    else
                    {
                        text.Margin = new Thickness(0, 0, 0, 10);
                        Manual.Children.Add(text);
                    }
                }
                line = reader.ReadLine();
            }
        }

        private static void AddTableRow(Grid table, string[] cells)
        {
            int row = table.RowDefinitions.Count;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int column = 0; column < cells.Length; column++)
            {
                var text = FormatText(cells[column]);
                if (row == 0)
                    text.FontWeight = FontWeights.SemiBold;
                var cell = new Border
                {
                    Child = text,
                    Padding = new Thickness(10, 6, 10, 6),
                    BorderThickness = new Thickness(column == 0 ? 1 : 0, row == 0 ? 1 : 0, 1, 1),
                };
                cell.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                if (row % 2 == 0)
                    cell.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                table.Children.Add(cell);
            }
        }

        private static TextBlock FormatText(string markdown)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
            foreach (var part in Regex.Split(markdown, @"(\*\*[^*]+\*\*|`[^`]+`)"))
            {
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                    text.Inlines.Add(new Bold(new Run(part[2..^2])));
                else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                {
                    var code = new Run(part[1..^1]) { FontFamily = new FontFamily("Consolas"), FontSize = 13 };
                    code.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                    text.Inlines.Add(code);
                }
                else
                    text.Inlines.Add(part);
            }
            return text;
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
