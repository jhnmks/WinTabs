using System;
using System.Runtime.InteropServices;

using wintabs.Layouts.Tiling;

namespace wintabs.Utilities
{
    internal enum TabShortcut
    {
        CycleTab,
        MoveTab,
        MoveTabToGroup,
        TileTab,
        CheatSheet,
    }

    internal readonly record struct TabShortcutCommand(TabShortcut Shortcut, TilingDirection Direction = default, int GroupNumber = 0);

    /// <summary>
    /// Fixed system-wide shortcuts:
    /// <list type="bullet">
    /// <item>Ctrl + arrow: cycle through the tabs of the focused group.</item>
    /// <item>Ctrl + Shift + arrow: move the focused tab one position within its group.</item>
    /// <item>Ctrl + Shift + 1-9: move the focused tab to the end of that numbered group.</item>
    /// <item>Ctrl + Alt + arrow: tile the focused tab beside its group in that direction.</item>
    /// <item>Ctrl + Alt + /: show or hide the shortcuts cheat sheet.</item>
    /// </list>
    /// </summary>
    internal sealed class TabShortcuts : IDisposable
    {
        private readonly LowLevelKeyboardHook m_hook;
        private readonly Action<TabShortcutCommand> m_handler;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern short GetAsyncKeyState(int vKey);


        public TabShortcuts(LowLevelKeyboardHook hook, Action<TabShortcutCommand> handler)
        {
            m_hook = hook;
            m_handler = handler;
            m_hook.KeyStateChanged += OnKeyStateChanged;
        }

        public void Dispose()
        {
            m_hook.KeyStateChanged -= OnKeyStateChanged;
        }

        private void OnKeyStateChanged(object? sender, ref LowLevelKeyboardHook.KeyStateChangedEventArgs e)
        {
            if (!e.IsPressed || Match(e.KeyCode) is not TabShortcutCommand command)
                return;

            // Swallow the key so the focused app (and Windows) never sees it.
            e.Handled = true;
            m_handler(command);
        }

        // Ask Windows for the live modifier state instead of tracking key events ourselves:
        // a missed key-up (e.g. Win + L locks before the release arrives) would otherwise
        // leave a modifier "stuck" and silently disable shortcuts.
        private static bool IsDown(KeyCode key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

        private static TabShortcutCommand? Match(KeyCode key)
        {
            bool ctrl = IsDown(KeyCode.ControlKey);
            bool shift = IsDown(KeyCode.ShiftKey);
            bool alt = IsDown(KeyCode.Menu);
            bool win = IsDown(KeyCode.LWin) || IsDown(KeyCode.RWin);

            if (ToDirection(key) is TilingDirection direction)
            {
                return (ctrl, alt, shift, win) switch
                {
                    (true, false, false, false) => new(TabShortcut.CycleTab, direction),
                    (true, false, true, false) => new(TabShortcut.MoveTab, direction),
                    (true, true, false, false) => new(TabShortcut.TileTab, direction),
                    _ => null,
                };
            }

            if (key == KeyCode.OemQuestion && ctrl && alt && !win && !shift)
                return new(TabShortcut.CheatSheet);

            if (alt)
                return null;

            if (ctrl && shift && !win && ToGroupNumber(key) is int groupNumber)
                return new(TabShortcut.MoveTabToGroup, GroupNumber: groupNumber);

            return null;
        }

        private static TilingDirection? ToDirection(KeyCode key) => key switch
        {
            KeyCode.Left => TilingDirection.Left,
            KeyCode.Right => TilingDirection.Right,
            KeyCode.Up => TilingDirection.Up,
            KeyCode.Down => TilingDirection.Down,
            _ => null,
        };

        private static int? ToGroupNumber(KeyCode key) => key switch
        {
            >= KeyCode.D1 and <= KeyCode.D9 => key - KeyCode.D0,
            >= KeyCode.NumPad1 and <= KeyCode.NumPad9 => key - KeyCode.NumPad0,
            _ => null,
        };
    }
}
