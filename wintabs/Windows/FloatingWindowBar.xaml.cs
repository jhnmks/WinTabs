using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

using WinMan;

using wintabs.DllImports;
using wintabs.Utilities;

namespace wintabs.Windows
{
    /// <summary>
    /// The float button and ⋯ menu for floating windows, which have no tab overlay. Like on a tab, it appears
    /// when the mouse comes near the top edge of the focused window.
    /// </summary>
    public partial class FloatingWindowBar : Window
    {
        private readonly IWorkspace m_workspace;
        private readonly FloatingWindows m_floating;
        private readonly IntPtr m_hwnd;
        private IWindow? m_target;
        private volatile bool m_updateQueued;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_LBUTTON = 0x01;

        internal FloatingWindowBar(IWorkspace workspace, FloatingWindows floating)
        {
            InitializeComponent();
            m_workspace = workspace;
            m_floating = floating;
            m_hwnd = new WindowInteropHelper(this).EnsureHandle();
            // Clicking the bar must not take focus away from the window it belongs to.
            var style = PInvoke.GetWindowLong(new(m_hwnd), GetWindowLongPtr_nIndex.GWL_EXSTYLE);
            PInvoke.SetWindowLong(new(m_hwnd), GetWindowLongPtr_nIndex.GWL_EXSTYLE,
                style | (int)(WINDOWS_EX_STYLE.WS_EX_TOOLWINDOW | WINDOWS_EX_STYLE.WS_EX_NOACTIVATE));

            m_workspace.CursorLocationChanged += OnWorkspaceChanged;
            m_workspace.FocusedWindowChanged += OnWorkspaceChanged;
            m_floating.Changed += OnWorkspaceChanged;
        }

        internal void Dispose()
        {
            m_workspace.CursorLocationChanged -= OnWorkspaceChanged;
            m_workspace.FocusedWindowChanged -= OnWorkspaceChanged;
            m_floating.Changed -= OnWorkspaceChanged;
            Close();
        }

        private void OnWorkspaceChanged(object? sender, object e)
        {
            if (m_updateQueued)
                return;
            m_updateQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                m_updateQueued = false;
                Update();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Update()
        {
            if (MoreMenu.IsOpen)
                return;

            var target = GetTarget();
            if (target == null)
            {
                HideBar();
                return;
            }

            try
            {
                var window = target.Position;
                var cursor = m_workspace.CursorLocation;
                var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                bool overBar = IsVisible && PInvoke.GetWindowRect(new(m_hwnd), out RECT bar) &&
                    cursor.X >= bar.left && cursor.X < bar.right && cursor.Y >= bar.top && cursor.Y < bar.bottom;
                bool nearTop = cursor.X >= window.Left && cursor.X < window.Right &&
                    cursor.Y >= window.Top - 4 * scale && cursor.Y < window.Top + 40 * scale;
                // Dragging the window by its title bar must not drag the bar along.
                bool dragging = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 && !overBar;

                if (dragging || !(overBar || nearTop))
                {
                    HideBar();
                    return;
                }
                ShowBar(target, window);
            }
            catch (InvalidWindowReferenceException)
            {
                HideBar();
            }
        }

        private IWindow? GetTarget()
        {
            var focused = m_workspace.FocusedWindow;
            if (focused == null || focused.Handle == m_hwnd || !m_floating.IsFloating(focused))
                return null;
            try
            {
                return focused.State == WinMan.WindowState.Restored ? focused : null;
            }
            catch (InvalidWindowReferenceException)
            {
                return null;
            }
        }

        private void ShowBar(IWindow target, Rectangle window)
        {
            m_target = target;
            if (!IsVisible)
                Show();
            UpdateLayout();
            var scale = VisualTreeHelper.GetDpi(this);
            int width = (int)Math.Ceiling(ActualWidth * scale.DpiScaleX);
            int left = window.Left + (window.Width - width) / 2;
            PInvoke.SetWindowPos(new(m_hwnd), new(-1), left, window.Top + 1, 0, 0,
                SetWindowPos_uFlags.SWP_NOSIZE | SetWindowPos_uFlags.SWP_NOACTIVATE);
        }

        private void HideBar()
        {
            if (IsVisible)
                Hide();
        }

        private void OnRetabClick(object sender, RoutedEventArgs e)
        {
            if (m_target is IWindow window)
            {
                HideBar();
                m_floating.Retab(window);
            }
        }

        private void OnMoreClick(object sender, RoutedEventArgs e)
        {
            if (m_target is not IWindow window)
                return;
            AlwaysFloatText.Text = (m_floating.IsAlwaysFloating(window) ? "Stop always floating " : "Always float ")
                + window.GetCachedAppName();
            MoreMenu.PlacementTarget = MoreButton;
            MoreMenu.IsOpen = true;
        }

        private void OnAlwaysFloatClick(object sender, RoutedEventArgs e)
        {
            if (m_target is IWindow window)
            {
                HideBar();
                _ = FloatingWindows.SetAlwaysFloatingAsync(window.GetCachedProcessName(), !m_floating.IsAlwaysFloating(window));
            }
        }

        private void OnMenuClosed(object sender, RoutedEventArgs e) => OnWorkspaceChanged(this, e);
    }
}
