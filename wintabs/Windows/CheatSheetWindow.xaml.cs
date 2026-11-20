using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using wintabs.DllImports;
using wintabs.Models;

using WinMan;

namespace wintabs.Windows
{
    /// <summary>
    /// A small always-on-top reference of the keyboard shortcuts, centred on the display the user is working on.
    /// </summary>
    public partial class CheatSheetWindow : Window
    {
        private static CheatSheetWindow? s_current;

        private readonly Rectangle m_workArea;

        private CheatSheetWindow(Rectangle workArea)
        {
            m_workArea = workArea;
            InitializeComponent();
            Shortcuts.ItemsSource = ShortcutCatalog.All;
        }

        /// <summary>
        /// Shows the cheat sheet on <paramref name="display"/>, or closes it if it is already open.
        /// </summary>
        public static void Toggle(IDisplay display)
        {
            if (s_current != null)
            {
                s_current.Close();
                return;
            }

            s_current = new CheatSheetWindow(display.WorkArea);
            s_current.Closed += (_, _) => s_current = null;
            s_current.Show();
            s_current.Activate();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            // Work with physical pixels so the window lands dead centre on any monitor and DPI.
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!PInvoke.GetWindowRect(new(hwnd), out var rect))
                return;
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            int x = m_workArea.Left + (m_workArea.Width - width) / 2;
            int y = m_workArea.Top + (m_workArea.Height - height) / 2;
            PInvoke.SetWindowPos(new(hwnd), new(-1), x, y, 0, 0,
                SetWindowPos_uFlags.SWP_NOSIZE | SetWindowPos_uFlags.SWP_NOACTIVATE);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
                Close();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Close();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // Clicking anywhere else dismisses it.
            if (IsLoaded && s_current == this)
                Close();
        }
    }
}
