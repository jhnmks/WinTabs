using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Diagnostics;
using System.Reactive.Linq;

using wintabs.DllImports;

using wintabs.Layouts.Tiling;
using wintabs.Utilities;
using wintabs.Windows;
using wintabs.Models;
using WinMan;
using WinMan.Windows;
using wintabs.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using System.Reactive.Disposables;
using System.Reactive;
using Serilog;
using wintabs.Toasts;
using System.Threading;
using wintabs.Pages.Settings;
using System.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Text;

using System.Data;
using System.Windows.Threading;
using SystemParameters = wintabs.Utilities.SystemParameters;

namespace wintabs
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        private readonly Win32Workspace m_workspace;
        private ITilingService m_tiling;
        private readonly CompositeDisposable m_subscriptions;
        private readonly UnmanagedResourceGuard? m_unmanagedResourceGuard;
        private readonly ToastService m_toasts;
        private readonly FloatingWindows m_floating;
        private readonly FloatingWindowBar m_floatingBar;

        private readonly TimeSpan ToastDurationLong = TimeSpan.FromMilliseconds(3000);
        private readonly TimeSpan ToastDurationShort = TimeSpan.FromMilliseconds(1000);

        private readonly ILogger m_logger;



        private readonly TaskbarIcon m_notifyIcon;
        private readonly ContextMenu m_contextMenu;
        private readonly LowLevelKeyboardHook m_llkbdHook;
        private readonly IntPtr m_hwnd;
        private bool m_soundOnFailure;
        private bool m_notifyVirtualDesktopServiceIncompatibility;
        private TabShortcuts? m_shortcuts;
        private DispatcherTimer m_dispatcherTimer;
        private readonly DispatcherTimer m_resumeLayoutTimer;
        private bool m_sessionLocked;
        private bool m_suspended;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public MainWindow()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            InitializeComponent();

            m_logger = App.Current.Logger;

            m_unmanagedResourceGuard = ApplySystemParameters();

            BindingErrorListener.Listen(OnBindingError);

            m_hwnd = new WindowInteropHelper(this).EnsureHandle();

            m_workspace = new Win32Workspace();
            m_workspace.UnhandledException += OnWorkspaceUnhandledException;

            m_workspace.Open();

            m_toasts = new ToastService(m_workspace);
            m_floating = new FloatingWindows(m_workspace);
            m_floatingBar = new FloatingWindowBar(m_workspace, m_floating);


            m_workspace.VirtualDesktopManager.CurrentDesktopChanged += OnVirtualDesktopChanged;
            m_workspace.FocusedWindowChanged += OnFocusedWindowChanged;


            var settings = App.Current.AppState.Settings
                .Do(x => m_soundOnFailure = x.SoundOnFailure)
                .Do(x => m_notifyVirtualDesktopServiceIncompatibility = x.NotifyVirtualDesktopServiceIncompatibility)
                .Do(x =>
                {
                })
                .DistinctUntilChanged();

            var appearanceSettings = settings.Select(x => x.Appearance).DistinctUntilChanged();

            var multiMonitorObservable = settings
                .Select(x => x.MultiMonitorSupport)
                .Take(1)
                .Do(async multiMonitorSupport => await Dispatcher.InvokeAsync(() =>
                {
                    if (multiMonitorSupport)
                    {
                        m_tiling = new MultiDisplayTilingService(m_workspace, settings, m_floating);
                    }
                    else
                    {
                        m_tiling = new TilingService(m_workspace, m_workspace.DisplayManager.PrimaryDisplay, settings, m_floating, true);
                    }

                    if (m_sessionLocked || m_suspended || m_resumeLayoutTimer?.IsEnabled == true)
                        m_tiling.SetWorkspacePaused(true);
                    m_tiling.Start();
                }))
                .Select(_ => Unit.Default);

            var exclusionListSettings = settings
                .DistinctUntilChanged(x => (x.ProcessIgnoreList, x.ClassIgnoreList))
                .Do(async x => await Dispatcher.InvokeAsync(() => m_floating.SetRules(x.ProcessIgnoreList, x.ClassIgnoreList)))
                .Select(_ => Unit.Default);

            m_llkbdHook = App.Current.Services.GetRequiredService<LowLevelKeyboardHook>();
            m_shortcuts = new TabShortcuts(m_llkbdHook, command => Dispatcher.BeginInvoke(() => OnShortcut(command)));

            m_subscriptions =
            [
                settings.Subscribe(new NotifyUnhandledObserver<Settings>()),
                appearanceSettings.Subscribe(appearance => _ = Dispatcher.InvokeAsync(() =>
                {
                    AppTheme.Apply(appearance);
                })),
                // Rules first, so windows that already match never become tabs at startup.
                exclusionListSettings.Subscribe(new NotifyUnhandledObserver<Unit>()),
                multiMonitorObservable.Subscribe(new NotifyUnhandledObserver<Unit>()),
            ];

            m_notifyIcon = new TaskbarIcon
            {
                Icon = wintabs.Resources.Files.Icon
            };
            m_notifyIcon.TrayLeftMouseDown += OnNotifyIconLeftMouseDown;
            m_notifyIcon.TrayRightMouseDown += OnNotifyIconRightMouseDown;
            m_notifyIcon.Visibility = Visibility.Visible;

            m_contextMenu = (ContextMenu)FindResource("NotifierContextMenu");


            m_dispatcherTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            m_dispatcherTimer.Tick += new EventHandler(OnDispatcherTimerTick);
            m_dispatcherTimer.Start();

            m_resumeLayoutTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            m_resumeLayoutTimer.Tick += OnResumeLayoutTick;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            _ = PInvoke.SetWindowLong(new(m_hwnd), GetWindowLongPtr_nIndex.GWL_EXSTYLE,
                (int)(WINDOWS_EX_STYLE.WS_EX_TOOLWINDOW | WINDOWS_EX_STYLE.WS_EX_TOPMOST));

            Loaded += OnLoaded;
            Show();
            Hide();

        }

        private UnmanagedResourceGuard? ApplySystemParameters()
        {
            bool windowArranging;
            try
            {
                windowArranging = SystemParameters.Instance.WindowArranging;
                SystemParameters.Instance.WindowArranging = false;
            }
            catch
            {
                return null;
            }
            return new(() =>
            {
                SystemParameters.Instance.WindowArranging = windowArranging;
            });
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    m_sessionLocked = e.Reason == SessionSwitchReason.SessionLock;
                    UpdateWorkspacePause();
                }, DispatcherPriority.Send);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode is PowerModes.Suspend or PowerModes.Resume)
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    m_suspended = e.Mode == PowerModes.Suspend;
                    UpdateWorkspacePause();
                }, DispatcherPriority.Send);
            }
        }

        private void UpdateWorkspacePause()
        {
            m_resumeLayoutTimer.Stop();
            if (m_sessionLocked || m_suspended)
            {
                m_tiling?.SetWorkspacePaused(true);
                return;
            }

            // Wait for Windows to finish reconnecting displays and moving windows.
            m_tiling?.SetWorkspacePaused(true);
            m_resumeLayoutTimer.Start();
        }

        private void OnResumeLayoutTick(object? sender, EventArgs e)
        {
            m_resumeLayoutTimer.Stop();
            m_tiling?.SetWorkspacePaused(false);
        }

        private void OnDispatcherTimerTick(object? sender, EventArgs e)
        {
            if (m_tiling == null)
            {
                return;
            }

            bool anyDiscovered = m_tiling.DiscoverWindows();
            if (anyDiscovered)
            {
                m_tiling.Refresh();
            }
        }

        private void OnBindingError(string? msg)
        {
#if DEBUG
            throw new DataException($"Binding error: {msg}");
#endif
        }

        private void OnFocusedWindowChanged(object? sender, FocusedWindowChangedEventArgs e)
        {
            if (e.NewFocusedWindow != null)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    m_contextMenu.IsOpen = false;
                });
            }
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (m_workspace.VirtualDesktopManager.GetType().Name == "DummyVirtualDesktopManager" && Environment.OSVersion.Version.Build >= 17661 && m_notifyVirtualDesktopServiceIncompatibility)
            {
                if (new Windows.MessageBox { IconGlyph = "\xF1AD", Title = "Your version of Windows is not supported! OS Build: " + Environment.OSVersion.Version.Build, Message = "WinTabs tries to use the internal Windows Virtual Desktop Service, which is unsupported by Microsoft. This message can be disabled in Settings.\n\nIt appears that in your version of Windows, Microsoft has changed the VDS interface, and WinTabs cannot detect your virtual desktops.\n\nIf you are running a supported non-Insider Windows build and you see this message, please select Yes to report it." }.ShowDialog() == true)
                {
                    m_logger.Warning("Unsupported OS Version: " + Environment.OSVersion.Version.ToString());
                }
            }

        }

        private void OnWorkspaceUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            m_logger.Error((Exception)e.ExceptionObject, "Unhandled exception thrown from " + m_workspace);
            Dispatcher.RethrowOnDispatcher((Exception)e.ExceptionObject!);
        }

        private async Task HandleCommandExceptionAsync(TilingFailedException e, string? friendlyActionName)
        {
            var messageText = friendlyActionName != null
                ? "Could not " + friendlyActionName + "!"
                : "Operation failed!";
            var hintText = GetTilingErrorText(e.FailReason);

            PlayBeepSound();
            await ShowToastAsync(messageText, hintText, ToastDurationLong);
        }

        private async void OnShortcut(TabShortcutCommand command)
        {
            string friendlyActionName = command.Shortcut switch
            {
                TabShortcut.CycleTab => "switch tabs",
                TabShortcut.CycleGroup => "switch groups",
                TabShortcut.MoveGroup => "move the group",
                TabShortcut.MoveTabToGroup => $"move the tab to group {command.GroupNumber}",
                TabShortcut.TileTab => "tile the tab",
                TabShortcut.CheatSheet => "show the shortcuts",
                _ => "move the tab",
            };
            m_logger.Debug("Shortcut {Command}", command);
            int offset = command.Direction is TilingDirection.Left or TilingDirection.Up ? -1 : 1;
            try
            {
                switch (command.Shortcut)
                {
                    case TabShortcut.CycleTab:
                        m_tiling.CycleTab(offset);
                        break;
                    case TabShortcut.MoveTab:
                        m_tiling.MoveTab(offset);
                        break;
                    case TabShortcut.CycleGroup:
                        m_tiling.CycleGroup(offset);
                        break;
                    case TabShortcut.CreateGroup:
                        m_tiling.CreateGroup();
                        break;
                    case TabShortcut.UnpackGroups:
                        m_tiling.UnpackGroups();
                        break;
                    case TabShortcut.MoveGroup:
                        m_tiling.MoveGroup(offset);
                        break;
                    case TabShortcut.MoveTabToGroup:
                        if (!m_tiling.MoveTabToGroup(command.GroupNumber))
                        {
                            m_logger.Information("Ctrl+Shift+{Group}: no tab group {Group} to move the tab into", command.GroupNumber);
                            PlayBeepSound();
                            await ShowToastAsync($"No tab group {command.GroupNumber}", "Use the number shown on the group's tab or badge.", ToastDurationLong);
                        }
                        break;
                    case TabShortcut.TileTab:
                        m_tiling.TileTab(command.Direction);
                        break;
                    case TabShortcut.CheatSheet:
                        ShowCheatSheet();
                        break;
                }
            }
            catch (TilingFailedException e)
            {
                await HandleCommandExceptionAsync(e, friendlyActionName);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or InvalidReferenceException
                or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
            {
                // A shortcut must never take WinTabs down; report it and keep running.
                m_logger.Error(e, "Shortcut {Command} failed", command);
                PlayBeepSound();
                await ShowToastAsync($"Could not {friendlyActionName}", e.Message, ToastDurationLong);
            }
        }

        private void PlayBeepSound()
        {
            if (m_soundOnFailure)
            {
                SystemSounds.Beep.Play();
            }
        }

        private static string? GetTilingErrorText(TilingError e)
        {
            return e switch
            {
                TilingError.MissingAdjacentWindow => "No adjacent window!",
                TilingError.MissingTarget => "No focused window!",
                TilingError.NoGroupInDirection => "There is no group in that direction in this row.",
                _ => null,
            };
        }

        internal static void OpenSettings()
        {
            if (App.Current.Windows.OfType<SettingsWindow>().Any())
            {
                App.Current.Windows.OfType<SettingsWindow>().First().Activate();
            }
            else
            {
                new SettingsWindow(new ViewModels.SettingsViewModel(App.Current.AppState.Settings)).Show();
            }
        }

        private Task ShowToastAsync(string messageText, TimeSpan duration)
        {
            return ShowToastAsync(messageText, null, duration);
        }

        private async Task ShowToastAsync(string messageText, string? hintText, TimeSpan duration)
        {
            await Dispatcher.RunAsync(() => m_toasts.ShowToastAsync(new MessageBoxContent
            {
                Text = messageText,
                HintText = hintText,
            }, new CancellationTokenSource(duration).Token));
        }

        private void OnNotifyIconLeftMouseDown(object? sender, RoutedEventArgs e)
        {
            m_contextMenu.IsOpen = false;
            OpenSettings();
        }

        private void OnNotifyIconRightMouseDown(object? sender, RoutedEventArgs e)
        {
            m_contextMenu.IsOpen = true;
        }

        private async void OnVirtualDesktopChanged(object? sender, CurrentDesktopChangedEventArgs e)
        {
            m_logger.Debug("Virtual desktop changed...");
            if (!ExplorerFeature.HasVirtualDesktopTooltip())
            {
                await ShowToastAsync(e.NewDesktop.Name, ToastDurationShort);
            }
        }

        private void OnSettingsClick(object? sender, RoutedEventArgs e)
        {
            m_contextMenu.IsOpen = false;
            OpenSettings();
        }

        private void ShowCheatSheet()
        {
            var foreground = m_workspace.FocusedWindow;
            var display = foreground == null ? null
                : m_workspace.DisplayManager.Displays.FirstOrDefault(d => d.Bounds.Contains(foreground.Position.Center));
            CheatSheetWindow.Toggle(display ?? m_workspace.DisplayManager.PrimaryDisplay);
        }

        internal static void ShowCheatSheetFromUi()
        {
            App.Current.Windows.OfType<MainWindow>().FirstOrDefault()?.ShowCheatSheet();
        }

        private void OnShortcutsClick(object? sender, RoutedEventArgs e)
        {
            m_contextMenu.IsOpen = false;
            ShowCheatSheet();
        }

        private void OnExitClick(object? sender, RoutedEventArgs e)
        {
            m_logger.Debug("Application exit requested!");
            m_contextMenu.IsOpen = false;
            App.Current.Terminate();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            e.Cancel = true;
        }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
        public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            m_resumeLayoutTimer.Stop();
            m_resumeLayoutTimer.Tick -= OnResumeLayoutTick;
            m_unmanagedResourceGuard?.Dispose();

            m_workspace.VirtualDesktopManager.CurrentDesktopChanged -= OnVirtualDesktopChanged;
            m_workspace.FocusedWindowChanged -= OnFocusedWindowChanged;

            m_logger.Debug($"Disposing of {nameof(MainWindow)}...");

            if (m_tiling?.Active == true)
            {
                m_tiling.Stop();
            }
            m_shortcuts?.Dispose();

            m_dispatcherTimer.Tick -= OnDispatcherTimerTick;
            m_dispatcherTimer?.Stop();

            m_logger.Debug($"Stopping the tiling window manager...");
            m_tiling?.Dispose();
            m_floatingBar.Dispose();
            m_floating.Dispose();
            m_logger.Debug($"Closing the workspace...");
            m_workspace?.Dispose();
            m_logger.Debug($"Closing all other subscriptions...");
            m_subscriptions?.Dispose();

            m_notifyIcon?.Dispose();
        }
    }
}
