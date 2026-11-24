#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using wintabs.Models;

namespace wintabs.Tests
{
    [TestClass]
    public class ShortcutViewsTest
    {
        [TestMethod]
        public void GroupShortcutModifiersDoNotChangeWindowTabShortcuts()
        {
            var key = wintabs.Utilities.KeyCode.Left;
            Assert.AreEqual(wintabs.Utilities.TabShortcut.CycleGroup, wintabs.Utilities.TabShortcuts.Match(key, true, false, false, true)?.Shortcut);
            Assert.AreEqual(wintabs.Utilities.TabShortcut.MoveGroup, wintabs.Utilities.TabShortcuts.Match(key, true, false, true, true)?.Shortcut);
            Assert.AreEqual(wintabs.Utilities.TabShortcut.CycleTab, wintabs.Utilities.TabShortcuts.Match(key, true, false, false, false)?.Shortcut);
            Assert.AreEqual(wintabs.Utilities.TabShortcut.MoveTab, wintabs.Utilities.TabShortcuts.Match(key, true, false, true, false)?.Shortcut);
            Assert.IsNull(wintabs.Utilities.TabShortcuts.Match(key, false, false, false, true));
        }

        [TestMethod]
        public void SectionsStartWhereTheSectionChanges()
        {
            var all = ShortcutCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                bool expected = i == 0 || all[i - 1].Section != all[i].Section;
                Assert.AreEqual(expected, all[i].IsFirstInSection, all[i].Title);
            }

            // Each section appears as one contiguous block, so grouping never splits it.
            var order = all.Select(s => s.Section).ToList();
            foreach (var section in order.Distinct())
            {
                int first = order.IndexOf(section), last = order.LastIndexOf(section);
                Assert.IsTrue(order.Skip(first).Take(last - first + 1).All(s => s == section), section);
            }
        }

        [TestMethod]
        public void ShortcutViewsAndContainerBarsRender()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // Create the real application so the page gets the same theme and styles as at runtime.
                    if (Application.Current is not App)
                    {
                        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                            .AddSingleton<Serilog.ILogger>(services, Serilog.Core.Logger.None);
                        new App(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                            .BuildServiceProvider(services)).InitializeComponent();
                    }
                    var page = (FrameworkElement)Activator.CreateInstance(
                        Type.GetType("wintabs.Pages.Settings.ShortcutsPage, wintabs")!, [null])!;
                    page.Measure(new Size(900, double.PositiveInfinity));
                    page.Arrange(new Rect(page.DesiredSize));
                    page.UpdateLayout();

                    AssertShowsEveryShortcut(page);

                    // The cheat sheet shares the same grouped list and key-cap template.
                    var sheetType = Type.GetType("wintabs.Windows.CheatSheetWindow, wintabs")!;
                    var sheet = (Window)Activator.CreateInstance(sheetType,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        null, [new WinMan.Rectangle(0, 0, 1920, 1080)], null)!;
                    var content = (FrameworkElement)sheet.Content;
                    sheet.Content = null;
                    content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    content.Arrange(new Rect(content.DesiredSize));
                    content.UpdateLayout();
                    AssertShowsEveryShortcut(content);
                    sheet.Close();

                    var about = new Pages.Settings.AboutPage(null!);
                    about.Measure(new Size(800, double.PositiveInfinity));
                    about.Arrange(new Rect(about.DesiredSize));
                    about.UpdateLayout();
                    var manual = (StackPanel)about.FindName("Manual");
                    var guide = manual.Children.OfType<TextBlock>().ToArray();
                    Assert.AreEqual("WinTabs user guide", guide[0].Text);
                    Assert.IsTrue(guide.Any(t => t.Text == "Move, merge, or swap whole tiles"));
                    Assert.IsTrue(guide.Any(t => t.Text == "Let a window float"));
                    Assert.IsTrue(guide.Any(t => t.Inlines.OfType<System.Windows.Documents.Bold>().Any()));
                    Assert.IsFalse(guide.Any(t => t.Text.Contains("**") || t.Text.Contains('`')));

                    var overlay = new ViewModels.TilingOverlayViewModel { FontSize = 12, IconSize = 12 };
                    var group = new ViewModels.TilingPanelViewModel { Overlay = overlay, GroupNumber = 1, HasGroupRow = true };
                    var container = new ViewModels.TilingPanelViewModel { Overlay = overlay, IsContainer = true, Title = "Work", GroupNumber = 2, GroupColorBrush = Brushes.LightBlue };
                    container.WindowTabs.Add(new ViewModels.TilingWindowViewModel { Overlay = overlay, AppName = "Editor" });
                    group.OuterTabs.Add(container);
                    group.WindowTabs.Add(new ViewModels.TilingWindowViewModel { Overlay = overlay, AppName = "Loose app" });
                    foreach (var (row, expected) in new[] { (group, "Work"), (container, "Editor") })
                    {
                        var bar = new Controls.TilingPanel { ViewModel = row };
                        bar.Measure(new Size(600, 24));
                        bar.Arrange(new Rect(0, 0, 600, 24));
                        bar.UpdateLayout();
                        var labels = Descendants(bar).OfType<TextBlock>().Where(IsShown).Select(t => t.Text).ToArray();
                        CollectionAssert.Contains(labels, expected);
                        if (row.IsContainer)
                            CollectionAssert.DoesNotContain(labels, "Work");
                        else
                            CollectionAssert.DoesNotContain(labels, "Loose app");
                    }
                    group.HasUngroupedTabs = true;
                    group.ShowUngroupedRow = true;
                    var ungrouped = new Controls.TilingPanel { ViewModel = group };
                    ungrouped.Measure(new Size(600, 50));
                    ungrouped.Arrange(new Rect(0, 0, 600, 50));
                    ungrouped.UpdateLayout();
                    var looseApp = Descendants(ungrouped).OfType<TextBlock>().Single(t => t.Text == "Loose app" && IsShown(t));
                    var work = Descendants(ungrouped).OfType<TextBlock>().Single(t => t.Text == "Work" && IsShown(t));
                    var ungroupedButton = Descendants(ungrouped).OfType<Button>().Single(b => Equals(b.Content, "Ungrouped") && IsShown(b));
                    Assert.IsTrue(looseApp.TranslatePoint(new Point(), ungrouped).Y > work.TranslatePoint(new Point(), ungrouped).Y);
                    Assert.IsTrue(ungroupedButton.TranslatePoint(new Point(), ungrouped).X > work.TranslatePoint(new Point(), ungrouped).X);
                    Assert.AreEqual(FontWeights.Bold, work.FontWeight);
                    var groupHit = TilingOverlayRenderer.FindGroupTab(ungrouped,
                        work.TranslatePoint(new Point(work.ActualWidth / 2, work.ActualHeight / 2), ungrouped));
                    Assert.AreSame(container, groupHit?.DataContext);
                    Assert.IsNull(TilingOverlayRenderer.FindGroupTab(ungrouped,
                        ungroupedButton.TranslatePoint(new Point(5, 5), ungrouped)));
                    Assert.AreEqual(0.55, ungroupedButton.Opacity);
                    var ungroupedText = Descendants(ungroupedButton).OfType<TextBlock>().Single(t => t.Text == "Ungrouped");
                    Assert.IsTrue(ungroupedText.DesiredSize.Height <= ungroupedButton.ActualHeight);
                    group.HasFocus = true;
                    Assert.AreEqual(1.0, ungroupedButton.Opacity);
                    var workButton = Descendants(ungrouped).OfType<Button>()
                        .Single(b => Descendants(b).Contains(work));
                    Assert.AreEqual(0.55, workButton.Opacity);
                    var badgeStyle = Application.Current.FindResource("GroupNumberBadgeStyle");
                    foreach (var (button, number) in new[] { (workButton, "2"), (ungroupedButton, "1") })
                    {
                        var badge = Descendants(button).OfType<Border>().Single(b => ReferenceEquals(b.Style, badgeStyle));
                        Assert.AreEqual(number, ((TextBlock)((Viewbox)badge.Child).Child).Text);
                        Assert.IsTrue(badge.ActualWidth > 0);
                        Assert.AreEqual(badge.ActualWidth, badge.ActualHeight);
                        Assert.AreEqual(Color.FromRgb(0xC4, 0x2B, 0x1C), ((SolidColorBrush)badge.Background).Color);
                    }
                    container.IsActiveTab = true;
                    Assert.AreEqual(1.0, workButton.Opacity);
                    Assert.IsTrue(Descendants(ungrouped).OfType<Border>().Any(b => ReferenceEquals(b.Background, Brushes.LightBlue) && IsShown(b)));
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null)
                throw new AssertFailedException(failure.ToString(), failure);
        }

        private static bool IsShown(DependencyObject element)
        {
            for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
                if (current is UIElement { Visibility: not Visibility.Visible })
                    return false;
            return true;
        }

        private static void AssertShowsEveryShortcut(DependencyObject view)
        {
            var texts = Descendants(view).OfType<TextBlock>().Select(t => t.Text).ToHashSet();
            foreach (var shortcut in ShortcutCatalog.All)
            {
                Assert.IsTrue(texts.Contains(shortcut.Title), shortcut.Title);
                foreach (var key in shortcut.Keys)
                    Assert.IsTrue(texts.Contains(key), key);
            }
            foreach (var section in ShortcutCatalog.All.Select(s => s.Section).Distinct())
                Assert.IsTrue(texts.Contains(section), section);
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var grandChild in Descendants(child))
                    yield return grandChild;
            }
        }
    }
}
