using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

using wintabs.Models;
using wintabs.Utilities;

using Serilog;

namespace wintabs.ViewModels
{
    public sealed record AlwaysFloatingApp(string Name, string ProcessName, ICommand RemoveCommand);

    public sealed class SettingsViewModel : ViewModelBase
    {
        private static readonly char[] AsciiWhitespaceChars = [' ', '\t', '\r', '\v', '\f', '\n'];

        public IObservableFileEntity<Settings> Model { get; }

        public bool NotifyVirtualDesktopServiceIncompatibility { get => m_notifyVirtualDesktopServiceIncompatibility; set => SetField(ref m_notifyVirtualDesktopServiceIncompatibility, value); }

        public bool RunsAtStartup
        {
            get => m_runsAtStartup;
            set
            {
                if (value)
                {
                    Autostart.EnableAsync()
                        .ContinueWith(t =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                SetField(ref m_runsAtStartup, t.Result, nameof(RunsAtStartup));
                            });
                        });
                }
                else
                {
                    Autostart.DisableAsync()
                        .ContinueWith(t =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                SetField(ref m_runsAtStartup, !t.Result, nameof(RunsAtStartup));
                            });
                        });
                }
            }
        }

        public AppearanceMode Appearance { get => m_appearance; set => SetField(ref m_appearance, value); }

        public int PanelHeight
        {
            get => m_panelHeight;
            set => SetField(ref m_panelHeight, value);
        }

        public int PanelFontSize
        {
            get => m_panelFontSize;
            set => SetField(ref m_panelFontSize, value);
        }

        public int WindowPadding
        {
            get => m_windowPadding;
            set => SetField(ref m_windowPadding, value);
        }

        public IReadOnlyList<FocusBorderSwatch> FocusBorderSwatches => FocusBorderPalette.Swatches;
        public string FocusBorderColor { get => m_focusBorderColor; set => SetField(ref m_focusBorderColor, FocusBorderPalette.Find(value).Name); }

        public IList<string>? ProcessIgnoreList
        {
            get => m_processIgnoreList;
            set
            {
                SetField(ref m_processIgnoreList, value);
                AlwaysFloatingApps = [.. (value ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => new AlwaysFloatingApp(AppNames.Describe(x), x, new DelegateCommand(_ => RemoveAlwaysFloatingApp(x))))
                    .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)];
                // Derived from ProcessIgnoreList, so it must not trigger a second save.
                base.NotifyPropertyChanged(nameof(AlwaysFloatingApps));
            }
        }

        /// <summary>The "Apps that always float" list: <see cref="ProcessIgnoreList"/> with readable names.</summary>
        public IReadOnlyList<AlwaysFloatingApp> AlwaysFloatingApps { get; private set; } = [];

        public void AddAlwaysFloatingApp(string processName)
        {
            processName = ByProcessNameMatcher.Normalize(processName);
            if (processName.Length == 0 || ProcessIgnoreList == null ||
                ProcessIgnoreList.Any(x => string.Equals(ByProcessNameMatcher.Normalize(x), processName, StringComparison.OrdinalIgnoreCase)))
                return;
            ProcessIgnoreList = [.. ProcessIgnoreList, processName];
        }

        private void RemoveAlwaysFloatingApp(string processName)
        {
            if (ProcessIgnoreList != null)
                ProcessIgnoreList = [.. ProcessIgnoreList.Where(x => x != processName)];
        }

        public IList<string>? ClassIgnoreList { get => m_classIgnoreList; set => SetField(ref m_classIgnoreList, value); }

        public bool MultiMonitorSupport { get => m_multiMonitorSupport; set => SetField(ref m_multiMonitorSupport, value); }

        public bool SoundOnFailure { get => m_soundOnFailure; set => SetField(ref m_soundOnFailure, value); }

        private bool m_runsAtStartup;
        private bool m_notifyVirtualDesktopServiceIncompatibility;
        private readonly IDisposable m_subscription;
        private bool m_isInit = false;
        private AppearanceMode m_appearance;
        private int m_panelHeight;
        private int m_panelFontSize;
        private int m_windowPadding;
        private IList<string>? m_processIgnoreList;
        private IList<string>? m_classIgnoreList;
        private bool m_multiMonitorSupport;
        private bool m_soundOnFailure;
        private string m_focusBorderColor = FocusBorderPalette.Swatches[0].Name;
        private readonly ILogger m_logger = App.Current.Logger;

        public SettingsViewModel(IObservableFileEntity<Settings> observable)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            Model = observable;
            m_subscription = observable
                .Subscribe(settings =>
            {
                dispatcher.Invoke(() =>
                {
                    m_isInit = false;
                    m_logger.Debug($"{nameof(wintabs.ViewModels.SettingsViewModel)} received new Settings");

                    NotifyVirtualDesktopServiceIncompatibility = settings.NotifyVirtualDesktopServiceIncompatibility;
                    Appearance = settings.Appearance;
                    WindowPadding = settings.WindowPadding;
                    PanelHeight = settings.PanelHeight;
                    PanelFontSize = settings.PanelFontSize;
                    ProcessIgnoreList = settings.ProcessIgnoreList;
                    ClassIgnoreList = settings.ClassIgnoreList;
                    MultiMonitorSupport = settings.MultiMonitorSupport;
                    SoundOnFailure = settings.SoundOnFailure;
                    FocusBorderColor = FocusBorderPalette.Find(settings.FocusBorderColor).Name;

                    Autostart.IsEnabledAsync()
                        .ContinueWith(t =>
                        {
                            dispatcher.Invoke(() =>
                            {
                                SetField(ref m_runsAtStartup, t.Result, nameof(RunsAtStartup));
                            });

                            m_isInit = true;
                        });
                });
            });

        }

        protected override void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            base.NotifyPropertyChanged(propertyName);

            if (!m_isInit)
            {
                return;
            }

            SaveChanges();
        }

        public override void Dispose()
        {
            m_subscription?.Dispose();
        }

        private void SaveChanges()
        {
            _ = Model.SaveAsync(x =>
            {
                m_logger.Debug($"{nameof(SettingsViewModel)} is overwriting existing Settings");

                return x with
                {
                    NotifyVirtualDesktopServiceIncompatibility = NotifyVirtualDesktopServiceIncompatibility,
                    Appearance = Appearance,
                    WindowPadding = WindowPadding,
                    PanelHeight = PanelHeight,
                    PanelFontSize = PanelFontSize,
                    ProcessIgnoreList = [.. ProcessIgnoreList!],
                    ClassIgnoreList = [.. ClassIgnoreList!],
                    MultiMonitorSupport = MultiMonitorSupport,
                    SoundOnFailure = SoundOnFailure,
                    FocusBorderColor = FocusBorderColor,
                };
            });
        }
    }
}
