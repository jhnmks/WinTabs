using System;
using System.Runtime.InteropServices;

using wintabs.Layouts.Tiling;
using wintabs.DllImports;

namespace wintabs.Utilities
{
    internal enum TabShortcut
    {
        CycleTab,
        MoveTab,
        CycleGroup,
        MoveGroup,
        CreateGroup,
        UnpackGroups,
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
    /// <item>Ctrl + Win + arrow: switch groups in the outer row.</item>
    /// <item>Ctrl + Win + Shift + arrow: move the active group in the outer row.</item>
    /// <item>Ctrl + Alt + arrow: tile the focused tab beside its group in that direction.</item>
    /// <item>Ctrl + Alt + /: show or hide the shortcuts cheat sheet.</item>
    /// </list>
    /// </summary>
    internal sealed class TabShortcuts : IDisposable
    {
        private readonly LowLevelKeyboardHook m_hook;
        private readonly Action<TabShortcutCommand> m_handler;
        private readonly System.Collections.Generic.HashSet<KeyCode> m_heldGroupKeys = [];

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
            if (m_heldGroupKeys.Contains(e.KeyCode))
            {
                if (!e.IsPressed)
                    m_heldGroupKeys.Remove(e.KeyCode);
                e.Handled = true;
                return;
            }
            if (!e.IsPressed || Match(e.KeyCode) is not TabShortcutCommand command)
                return;

            // Swallow the key so the focused app (and Windows) never sees it.
            e.Handled = true;
            if (command.Shortcut is TabShortcut.CreateGroup or TabShortcut.UnpackGroups)
                m_heldGroupKeys.Add(e.KeyCode);
            if (command.Shortcut is TabShortcut.CycleGroup or TabShortcut.MoveGroup or TabShortcut.CreateGroup or TabShortcut.UnpackGroups)
                SuppressStartMenu();
            m_handler(command);
        }

        private static void SuppressStartMenu()
        {
            // The swallowed shortcut must not leave a lone Win press that opens Start on release.
            const ushort unassignedKey = 0xE8;
            INPUT[] input = new INPUT[2];
            input[0].type = input[1].type = INPUT_typeFlags.INPUT_KEYBOARD;
            input[0].Anonymous.ki.wVk = input[1].Anonymous.ki.wVk = unassignedKey;
            input[0].Anonymous.ki.dwExtraInfo = input[1].Anonymous.ki.dwExtraInfo = FocusHelper.InjectedInputTag;
            input[1].Anonymous.ki.dwFlags = keybd_eventFlags.KEYEVENTF_KEYUP;
            PInvoke.SendInput(input, Marshal.SizeOf<INPUT>());
        }

        // Ask Windows for the live modifier state instead of tracking key events ourselves:
        // a missed key-up (e.g. Win + L locks before the release arrives) would otherwise
        // leave a modifier "stuck" and silently disable shortcuts.
        private static bool IsDown(KeyCode key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

        private static TabShortcutCommand? Match(KeyCode key)
        {
            bool ctrl = IsDown(KeyCode.LeftCtrl) || IsDown(KeyCode.RightCtrl);
            bool shift = IsDown(KeyCode.LeftShift) || IsDown(KeyCode.RightShift);
            bool alt = IsDown(KeyCode.LeftAlt) || IsDown(KeyCode.RightAlt);
            bool win = IsDown(KeyCode.LWin) || IsDown(KeyCode.RWin);

            return Match(key, ctrl, alt, shift, win);
        }

        internal static TabShortcutCommand? Match(KeyCode key, bool ctrl, bool alt, bool shift, bool win)
        {
            if (ToDirection(key) is TilingDirection direction)
            {
                return (ctrl, alt, shift, win) switch
                {
                    (true, false, false, false) => new(TabShortcut.CycleTab, direction),
                    (true, false, true, false) => new(TabShortcut.MoveTab, direction),
                    (true, false, false, true) => new(TabShortcut.CycleGroup, direction),
                    (true, false, true, true) => new(TabShortcut.MoveGroup, direction),
                    (true, true, false, false) => new(TabShortcut.TileTab, direction),
                    _ => null,
                };
            }

            if (key == KeyCode.G && ctrl && win && !alt && !shift)
                return new(TabShortcut.CreateGroup);

            if (key == KeyCode.U && ctrl && win && !alt && !shift)
                return new(TabShortcut.UnpackGroups);

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
