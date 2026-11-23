#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using WinMan;

using wintabs.Tests.TestUtilities;
using wintabs.Utilities;

namespace wintabs.Tests
{
    [TestClass]
    public class FloatingWindowsTest
    {
        private readonly IWindow m_explorer = new WindowMockFactory().CreateExplorerWindow();

        private FloatingWindows Create(List<IWindow> changes)
        {
            var workspace = new Mock<IWorkspace>();
            workspace.Setup(x => x.GetSnapshot()).Returns([m_explorer]);
            var floating = new FloatingWindows(workspace.Object);
            floating.Changed += (_, window) => changes.Add(window);
            return floating;
        }

        [TestMethod]
        public void RemovingTheRuleReleasesTheWindow()
        {
            List<IWindow> changes = [];
            var floating = Create(changes);

            floating.SetRules(["Explorer.exe"], []);
            Assert.AreEqual(FloatReason.Rule, floating.GetReasons(m_explorer));
            Assert.IsTrue(floating.IsAlwaysFloating(m_explorer));

            floating.SetRules([], []);
            Assert.IsFalse(floating.IsFloating(m_explorer));
            Assert.IsFalse(floating.IsAlwaysFloating(m_explorer));
            Assert.AreEqual(2, changes.Count);
        }

        [TestMethod]
        public void AWindowFloatedByHandStaysFloatingWhenItsRuleGoes()
        {
            var floating = Create([]);
            floating.SetRules(["explorer"], []);
            floating.Add(m_explorer, FloatReason.Manual);

            floating.SetRules([], []);
            Assert.AreEqual(FloatReason.Manual, floating.GetReasons(m_explorer));
        }

        [TestMethod]
        public void RetabbingOverridesTheRuleUntilTheRuleIsRemoved()
        {
            var floating = Create([]);
            floating.SetRules(["explorer"], []);

            floating.Retab(m_explorer);
            Assert.IsFalse(floating.IsFloating(m_explorer));

            // Unrelated rule changes do not undo the user's choice.
            floating.SetRules(["explorer", "notepad"], []);
            Assert.IsFalse(floating.IsFloating(m_explorer));
            floating.OnWindowAdded(m_explorer, autoFloat: false);
            Assert.IsFalse(floating.IsFloating(m_explorer));

            // Removing and adding the rule again does.
            floating.SetRules([], []);
            floating.SetRules(["explorer"], []);
            Assert.AreEqual(FloatReason.Rule, floating.GetReasons(m_explorer));
        }

        [TestMethod]
        public void ProcessNamesIgnoreCaseSpacesAndExe()
        {
            Assert.AreEqual("Taskmgr", ByProcessNameMatcher.Normalize(" Taskmgr.EXE "));
            Assert.IsTrue(new ByProcessNameMatcher("EXPLORER.exe").Matches(m_explorer));
            Assert.IsFalse(new ByProcessNameMatcher("").Matches(m_explorer));
        }

        [TestMethod]
        public void ClassRulesMatchExactlyOrByRegex()
        {
            // Only real Win32 windows have a class; anything else never matches.
            Assert.IsFalse(new ByClassNameMatcher("#32770").Matches(m_explorer));
            Assert.IsFalse(new ByClassNameMatcher("[").Matches(m_explorer));
        }

        [TestMethod]
        public void RulesPageRenders()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current is not App)
                    {
                        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                            .AddSingleton<Serilog.ILogger>(services, Serilog.Core.Logger.None);
                        new App(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                            .BuildServiceProvider(services)).InitializeComponent();
                    }
                    var settings = new Mock<wintabs.Models.IObservableFileEntity<wintabs.Models.Settings>>();
                    var viewModel = new ViewModels.SettingsViewModel(settings.Object)
                    {
                        ProcessIgnoreList = ["explorer", "SomeAppThatIsNotRunning"],
                        ClassIgnoreList = ["#32770"],
                    };
                    var page = new Pages.Settings.RulesPage(viewModel);
                    page.Measure(new Size(900, double.PositiveInfinity));
                    page.Arrange(new Rect(page.DesiredSize));
                    page.UpdateLayout();

                    var texts = Descendants(page).OfType<TextBlock>().Select(t => t.Text).ToList();
                    CollectionAssert.Contains(texts, "Apps that always float");
                    CollectionAssert.Contains(texts, "SomeAppThatIsNotRunning");
                    CollectionAssert.Contains(texts, "explorer.exe");
                    CollectionAssert.Contains(texts, "Advanced: window class rules");
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

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
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
