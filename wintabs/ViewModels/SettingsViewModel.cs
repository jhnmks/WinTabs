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
    public record class KeybindingGroup(string GroupKey, IList<KeybindingViewModel> Keybindings);

    public sealed class SettingsViewModel : ViewModelBase
    {
        private static readonly char[] AsciiWhitespaceChars = [' ', '\t', '\r', '\v', '\f', '\n'];

        public IObservableFileEntity<Settings> Model { get; }

        public ActivationHotkey? SelectedActivationHotkey { get => m_activationHotkey; set => SetField(ref m_activationHotkey, value); }

        public ObservableCollection<ActivationHotkey> ActivationHotkeyOptions { get; }
            = new ObservableCollection<ActivationHotkey>(ActivationHotkey.AllowedHotkeys);

        public bool ActivateOnCapsLock { get => m_activateOnCapsLock; set => SetField(ref m_activateOnCapsLock, value); }
        public bool NotifyVirtualDesktopServiceIncompatibility { get => m_notifyVirtualDesktopServiceIncompatibility; set => SetField(ref m_notifyVirtualDesktopServiceIncompatibility, value); }

        public bool ShowContextHints { get => m_showContextHints; set => SetField(ref m_showContextHints, value); }
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


        public ObservableCollection<KeybindingViewModel>? Keybindings
        {
            get => m_keybindings; set
            {
                SetField(ref m_keybindings, value);
                base.NotifyPropertyChanged(nameof(KeybindingGroups));
            }
        }

        [DerivedProperty(nameof(Keybindings))]
        public IList<KeybindingGroup>? KeybindingGroups => CreateKeybindingGroups(m_keybindings!);

        public IList<string>? ProcessIgnoreList { get => m_processIgnoreList; set => SetField(ref m_processIgnoreList, value); }

        public IList<string>? ClassIgnoreList { get => m_classIgnoreList; set => SetField(ref m_classIgnoreList, value); }

        public bool MultiMonitorSupport { get => m_multiMonitorSupport; set => SetField(ref m_multiMonitorSupport, value); }

        public bool SoundOnFailure { get => m_soundOnFailure; set => SetField(ref m_soundOnFailure, value); }

        private bool m_runsAtStartup;
        private bool m_notifyVirtualDesktopServiceIncompatibility;
        private readonly IDisposable m_subscription;
        private bool m_isInit = false;
        private ObservableCollection<KeybindingViewModel>? m_keybindings;
        private AppearanceMode m_appearance;
        private int m_panelHeight;
        private int m_panelFontSize;
        private int m_windowPadding;
        private ActivationHotkey? m_activationHotkey;
        private bool m_activateOnCapsLock;
        private IList<string>? m_processIgnoreList;
        private IList<string>? m_classIgnoreList;
        private bool m_multiMonitorSupport;
        private bool m_showContextHints;
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

                    SelectedActivationHotkey = settings.ActivationHotkey;
                    ActivateOnCapsLock = settings.ActivateOnCapsLock;
                    NotifyVirtualDesktopServiceIncompatibility = settings.NotifyVirtualDesktopServiceIncompatibility;
                    Appearance = settings.Appearance;
                    WindowPadding = settings.WindowPadding;
                    PanelHeight = settings.PanelHeight;
                    PanelFontSize = settings.PanelFontSize;
                    ProcessIgnoreList = settings.ProcessIgnoreList;
                    ClassIgnoreList = settings.ClassIgnoreList;
                    MultiMonitorSupport = settings.MultiMonitorSupport;
                    ShowContextHints = settings.ShowContextHints;
                    SoundOnFailure = settings.SoundOnFailure;
                    FocusBorderColor = FocusBorderPalette.Find(settings.FocusBorderColor).Name;

                    var newKeybindings = KeybindingViewModel.FromDictionary(settings.Keybindings);
                    if (m_keybindings == null)
                    {
                        Keybindings = [.. newKeybindings];
                        foreach (var vm in Keybindings)
                        {
                            vm.PropertyChanged += OnKeybindingPropertyChanged;
                        }
                    }
                    else
                    {
                        Debug.Assert(m_keybindings.Count == newKeybindings.Count);
                        foreach (var vm in newKeybindings)
                        {
                            var existingVm = m_keybindings.First(x => x.Action == vm.Action);
                            existingVm.Pattern = vm.Pattern;
                            existingVm.IsDirectMode = vm.IsDirectMode;
                        }
                    }

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

        private void OnKeybindingPropertyChanged(object? c, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(KeybindingViewModel.Pattern) || e.PropertyName == nameof(KeybindingViewModel.IsDirectMode))
            {
                var vm = ((KeybindingViewModel)c!);
                var keybindingSet = new Dictionary<IReadOnlySet<KeyCode>, KeybindingViewModel>(EqualityComparer<KeyCode>.Default.ToSequenceComparer());
                var duplicatesToRemove = Keybindings!
                    .Where(x => x.Pattern != null)
                    .GroupBy(x => x.Pattern, EqualityComparer<KeyCode>.Default.ToSequenceComparer())
                    .Where(g => g.Count() > 1);
                foreach (var duplicates in duplicatesToRemove)
                {
                    foreach (var duplicate in duplicates)
                    {
                        if (duplicate != vm)
                        {
                            duplicate.Pattern = null;
                        }
                    }
                }

                bool isValid = !vm.IsDirectMode || vm.Pattern == null
                    || KeyCodeHelper.CanMapToValidModifiersAndKeyCode(vm.Pattern);

                if (!isValid)
                {
                    vm.HasErrors = true;
                    vm.SetIsDirectModeInternal(false);
                }
                else
                {
                    vm.HasErrors = false;
                    SaveChanges();
                }
            }
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
                    ActivationHotkey = SelectedActivationHotkey!,
                    ActivateOnCapsLock = ActivateOnCapsLock,
                    NotifyVirtualDesktopServiceIncompatibility = NotifyVirtualDesktopServiceIncompatibility,
                    Appearance = Appearance,
                    Keybindings = KeybindingViewModel.ToDictionary(m_keybindings!),
                    WindowPadding = WindowPadding,
                    PanelHeight = PanelHeight,
                    PanelFontSize = PanelFontSize,
                    ShowContextHints = ShowContextHints,
                    ProcessIgnoreList = [.. ProcessIgnoreList!],
                    ClassIgnoreList = [.. ClassIgnoreList!],
                    MultiMonitorSupport = MultiMonitorSupport,
                    SoundOnFailure = SoundOnFailure,
                    FocusBorderColor = FocusBorderColor,
                };
            });
        }

        private static List<KeybindingGroup> CreateKeybindingGroups(IList<KeybindingViewModel> keybindings)
        {
            var buckets = new[]
            {
                ("WinTabs", new HashSet<BindableAction>{
                    BindableAction.ToggleManager,
                    BindableAction.ShowDesktop,
                    BindableAction.RefreshWorkspace,
                    BindableAction.Cancel,
                }),
                ("Windows", new HashSet<BindableAction>{
                    BindableAction.ToggleFloatingMode,
                    BindableAction.MoveLeft,
                    BindableAction.MoveRight,
                    BindableAction.MoveDown,
                    BindableAction.MoveUp,
                    BindableAction.SwapLeft,
                    BindableAction.SwapRight,
                    BindableAction.SwapDown,
                    BindableAction.SwapUp,
                }),
                ("Focus", new HashSet<BindableAction>{
                    BindableAction.MoveFocusLeft,
                    BindableAction.MoveFocusRight,
                    BindableAction.MoveFocusDown,
                    BindableAction.MoveFocusUp,
                }),
                ("Virtual Desktops", new HashSet<BindableAction>{
                    BindableAction.SwitchToPreviousDesktop,
                    BindableAction.SwitchToLeftDesktop,
                    BindableAction.SwitchToRightDesktop,
                    BindableAction.MoveToPreviousDesktop,
                    BindableAction.MoveToLeftDesktop,
                    BindableAction.MoveToRightDesktop,
                    BindableAction.SwitchToDesktop1,
                    BindableAction.SwitchToDesktop2,
                    BindableAction.SwitchToDesktop3,
                    BindableAction.SwitchToDesktop4,
                    BindableAction.SwitchToDesktop5,
                    BindableAction.SwitchToDesktop6,
                    BindableAction.SwitchToDesktop7,
                    BindableAction.SwitchToDesktop8,
                    BindableAction.SwitchToDesktop9,
                    BindableAction.MoveToDesktop1,
                    BindableAction.MoveToDesktop2,
                    BindableAction.MoveToDesktop3,
                    BindableAction.MoveToDesktop4,
                    BindableAction.MoveToDesktop5,
                    BindableAction.MoveToDesktop6,
                    BindableAction.MoveToDesktop7,
                    BindableAction.MoveToDesktop8,
                    BindableAction.MoveToDesktop9,
                }),
                ("Displays", new HashSet<BindableAction>{
                    BindableAction.SwitchToPreviousDisplay,
                    BindableAction.MoveToPreviousDisplay,
                    BindableAction.SwitchToDisplay1,
                    BindableAction.SwitchToDisplay2,
                    BindableAction.SwitchToDisplay3,
                    BindableAction.SwitchToDisplay4,
                    BindableAction.SwitchToDisplay5,
                    BindableAction.SwitchToDisplay6,
                    BindableAction.SwitchToDisplay7,
                    BindableAction.SwitchToDisplay8,
                    BindableAction.SwitchToDisplay9,
                    BindableAction.MoveToDisplay1,
                    BindableAction.MoveToDisplay2,
                    BindableAction.MoveToDisplay3,
                    BindableAction.MoveToDisplay4,
                    BindableAction.MoveToDisplay5,
                    BindableAction.MoveToDisplay6,
                    BindableAction.MoveToDisplay7,
                    BindableAction.MoveToDisplay8,
                    BindableAction.MoveToDisplay9,
                })
            };

            List<BindableAction> missing = [.. Enum.GetValues(typeof(BindableAction)).OfType<BindableAction>().Except(buckets.SelectMany(x => x.Item2))];
            Debug.Assert(missing.Count == 0);

            return buckets.Select((bucket) =>
            {
                var (group, actions) = bucket;
                return new KeybindingGroup(
                    GroupKey: group,
                    Keybindings: keybindings.Where(x => actions.Contains(x.Action)).ToList());
            }).Where(group => group.Keybindings.Count > 0).ToList();
        }
    }
}
