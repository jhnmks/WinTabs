using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using WinMan;

using wintabs.Utilities;

namespace wintabs
{
    /// <summary>
    /// Why a window is kept out of the tab layout. A window can float for several reasons at once and only
    /// returns to its tab group when none are left.
    /// </summary>
    [Flags]
    internal enum FloatReason
    {
        None = 0,

        /// <summary>The user pressed the float button (or auto-float of new windows is on).</summary>
        Manual = 1,

        /// <summary>Its app always floats, or it matches a window class rule.</summary>
        Rule = 2,

        /// <summary>It could not fit into any tab group.</summary>
        DoesNotFit = 4,
    }

    /// <summary>
    /// The single record of which windows float and why, shared by every display. <see cref="Changed"/> fires
    /// whenever a window's reasons change, so every display can take the window out of or back into its tabs.
    /// </summary>
    internal sealed class FloatingWindows : IDisposable
    {
        // Always on: the file copy progress dialog and RemoteApp (RDP) windows.
        private static readonly IWindowMatcher[] BuiltInRules =
        [
            new ByClassNameMatcher("OperationStatusWindow"),
            new ByClassNameMatcher("RAIL_WINDOW"),
        ];

        private readonly IWorkspace m_workspace;
        private readonly Lock m_lock = new();
        private readonly Dictionary<IWindow, FloatReason> m_reasons = [];
        /// <summary>
        /// Windows the user put back into tabs although a rule matches. The rule stays, but stops applying to
        /// that window until it closes or the rule is removed.
        /// </summary>
        private readonly HashSet<IWindow> m_ruleOverridden = [];
        private IReadOnlyList<IWindowMatcher> m_rules = BuiltInRules;
        private HashSet<string> m_alwaysFloatingApps = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<IWindow>? Changed;

        public FloatingWindows(IWorkspace workspace)
        {
            m_workspace = workspace;
            m_workspace.WindowRemoved += OnWindowRemoved;
        }

        public void Dispose()
        {
            m_workspace.WindowRemoved -= OnWindowRemoved;
            Changed = null;
        }

        /// <summary>
        /// Replaces the rules and re-checks every open window in both directions: windows that now match start
        /// floating, and windows whose rule was removed go back into their tab group (unless floating for another reason).
        /// </summary>
        public void SetRules(IEnumerable<string> processNames, IEnumerable<string> classNames)
        {
            var apps = processNames.Select(ByProcessNameMatcher.Normalize).Where(x => x.Length != 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IWindowMatcher[] rules =
            [
                .. BuiltInRules,
                .. apps.Select(x => new ByProcessNameMatcher(x)),
                .. classNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => new ByClassNameMatcher(x.Trim())),
            ];

            lock (m_lock)
            {
                m_rules = rules;
                m_alwaysFloatingApps = apps;
            }

            List<IWindow> changed = [];
            foreach (var window in m_workspace.GetSnapshot())
            {
                bool matches = Matches(window, rules);
                lock (m_lock)
                {
                    if (!matches)
                        m_ruleOverridden.Remove(window);
                    var reasons = m_reasons.GetValueOrDefault(window);
                    var updated = matches && !m_ruleOverridden.Contains(window)
                        ? reasons | FloatReason.Rule
                        : reasons & ~FloatReason.Rule;
                    if (Set(window, reasons, updated))
                        changed.Add(window);
                }
            }
            foreach (var window in changed)
                Changed?.Invoke(this, window);
        }

        public FloatReason GetReasons(IWindow window)
        {
            lock (m_lock)
                return m_reasons.GetValueOrDefault(window);
        }

        public bool IsFloating(IWindow window) => GetReasons(window) != FloatReason.None;

        /// <summary>True when the window's app is on the "always float" list.</summary>
        public bool IsAlwaysFloating(IWindow window)
        {
            var name = SafeProcessName(window);
            lock (m_lock)
                return name != null && m_alwaysFloatingApps.Contains(name);
        }

        public void Add(IWindow window, FloatReason reason) => Update(window, x => x | reason);

        /// <summary>Applies the rules to a window that just appeared. Safe to call more than once.</summary>
        public void OnWindowAdded(IWindow window, bool autoFloat)
        {
            IReadOnlyList<IWindowMatcher> rules;
            lock (m_lock)
                rules = m_rules;
            var reasons = (Matches(window, rules) ? FloatReason.Rule : FloatReason.None)
                | (autoFloat ? FloatReason.Manual : FloatReason.None);
            if (reasons != FloatReason.None)
                Update(window, x => x | (m_ruleOverridden.Contains(window) ? reasons & ~FloatReason.Rule : reasons));
        }

        /// <summary>
        /// The user put the window back into its tab group: every reason is dropped. A rule that still matches is
        /// kept, but no longer applies to this window until it closes.
        /// </summary>
        public void Retab(IWindow window)
        {
            Update(window, x =>
            {
                if ((x & FloatReason.Rule) != 0)
                    m_ruleOverridden.Add(window);
                return FloatReason.None;
            });
        }

        /// <summary>Adds the app to, or removes it from, the "always float" list in the settings.</summary>
        public static Task SetAlwaysFloatingAsync(string processName, bool alwaysFloat)
        {
            processName = ByProcessNameMatcher.Normalize(processName);
            if (processName.Length == 0)
                return Task.CompletedTask;
            return App.Current.AppState.Settings.SaveAsync(settings =>
            {
                List<string> apps = [.. settings.ProcessIgnoreList.Where(x =>
                    !string.Equals(ByProcessNameMatcher.Normalize(x), processName, StringComparison.OrdinalIgnoreCase))];
                if (alwaysFloat)
                    apps.Add(processName);
                return settings with { ProcessIgnoreList = apps };
            });
        }

        private void Update(IWindow window, Func<FloatReason, FloatReason> update)
        {
            bool changed;
            lock (m_lock)
            {
                var reasons = m_reasons.GetValueOrDefault(window);
                changed = Set(window, reasons, update(reasons));
            }
            if (changed)
                Changed?.Invoke(this, window);
        }

        // Call with the lock held.
        private bool Set(IWindow window, FloatReason previous, FloatReason reasons)
        {
            if (previous == reasons)
                return false;
            if (reasons == FloatReason.None)
                m_reasons.Remove(window);
            else
                m_reasons[window] = reasons;
            return true;
        }

        private void OnWindowRemoved(object? sender, WindowChangedEventArgs e)
        {
            lock (m_lock)
            {
                m_reasons.Remove(e.Source);
                m_ruleOverridden.Remove(e.Source);
            }
        }

        private static bool Matches(IWindow window, IReadOnlyList<IWindowMatcher> rules)
        {
            try
            {
                return rules.Any(x => x.Matches(window));
            }
            catch (InvalidWindowReferenceException)
            {
                return false;
            }
        }

        private static string? SafeProcessName(IWindow window)
        {
            try
            {
                return window.GetCachedProcessName();
            }
            catch (InvalidWindowReferenceException)
            {
                return null;
            }
        }
    }
}
