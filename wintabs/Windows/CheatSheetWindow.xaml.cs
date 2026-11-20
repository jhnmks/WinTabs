using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;

using wintabs.DllImports;

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
        private bool m_closing;

        private CheatSheetWindow(Rectangle workArea)
        {
            m_workArea = workArea;
            InitializeComponent();
            // Start off-screen; the window is placed before it ever becomes visible (see OnSourceInitialized).
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
        }

        /// <summary>
        /// Shows the cheat sheet on <paramref name="display"/>, or closes it if it is already open.
        /// </summary>
        public static void Toggle(IDisplay display)
        {
            if (s_current != null)
            {
                s_current.Dismiss();
                return;
            }

            s_current = new CheatSheetWindow(display.WorkArea);
            s_current.Closed += (_, _) => s_current = null;
            s_current.Show();
            s_current.Activate();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Lay the content out now, so the real size is known before the first frame is drawn,
            // then place the window dead centre in physical pixels (correct on any monitor and DPI).
            var hwnd = new WindowInteropHelper(this).Handle;
            var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            int width = (int)Math.Ceiling(DesiredSize.Width * scale);
            int height = (int)Math.Ceiling(DesiredSize.Height * scale);
            int x = m_workArea.Left + (m_workArea.Width - width) / 2;
            int y = m_workArea.Top + (m_workArea.Height - height) / 2;
            PInvoke.SetWindowPos(new(hwnd), new(-1), x, y, 0, 0,
                SetWindowPos_uFlags.SWP_NOSIZE | SetWindowPos_uFlags.SWP_NOACTIVATE);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        }

        /// <summary>
        /// Closes the window once. Closing deactivates the window, which would otherwise call Close()
        /// again while WPF is still closing it - and WPF throws in that case.
        /// </summary>
        private void Dismiss()
        {
            if (m_closing)
                return;
            m_closing = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            m_closing = true;
            base.OnClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
                Dismiss();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Dismiss();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // Clicking anywhere else dismisses it.
            if (IsLoaded)
                Dismiss();
        }
    }
}
