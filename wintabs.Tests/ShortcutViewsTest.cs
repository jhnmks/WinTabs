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
        public void ShortcutsPageRendersEveryShortcutGroupedBySection()
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
