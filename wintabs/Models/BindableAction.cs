using System;
using System.Reflection;

using wintabs.Utilities;

namespace wintabs.Models
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ActionAttribute(string caption, string description) : Attribute
    {
        public string Caption { get; } = caption;
        public string Description { get; } = description;
    }

    internal static class BindableActionExtensions
    {
        private static ActionAttribute Info(BindableAction action) =>
            typeof(BindableAction).GetField(action.ToString())?.GetCustomAttribute<ActionAttribute>()
            ?? new ActionAttribute(action.ToString(), string.Empty);

        public static string GetCaption(this BindableAction action) => Info(action).Caption;

        public static string GetDescription(this BindableAction action) => Info(action).Description;
    }

    public enum BindableAction
    {
        // Group: WinTabs
        [Action("Toggle window management", "Toggle WinTabs's window management functionality")]
        [DefaultKeybinding(KeyCode.F11)]
        ToggleManager,
        [Action("Force refresh workspace", "Force a change-detection cycle of the whole workspace")]
        [DefaultKeybinding(KeyCode.R)]
        RefreshWorkspace,
        [Action("Cancel", "Cancel the action")]
        [DefaultKeybinding(KeyCode.Escape)]
        Cancel,

        // Group: Focus
        [Action("Move focus left", "Focus the window to the left of the current window")]
        [DefaultKeybinding(KeyCode.Left)]
        MoveFocusLeft,
        [Action("Move focus up", "Focus the window above the current window")]
        [DefaultKeybinding(KeyCode.Up)]
        MoveFocusUp,
        [Action("Move focus right", "Focus the window to the right of the current window")]
        [DefaultKeybinding(KeyCode.Right)]
        MoveFocusRight,
        [Action("Move focus down", "Focus the window below the current window")]
        [DefaultKeybinding(KeyCode.Down)]
        MoveFocusDown,
        [Action("Show desktop", "Show/hide the desktop")]
        [DefaultKeybinding(KeyCode.D)]
        ShowDesktop,

        // Group: Windows
        [Action("Float window", "Toggle floating mode for the focused window")]
        [DefaultKeybinding(KeyCode.F)]
        ToggleFloatingMode,
        [Action("Move left", "Move the window next to the window on the left")]
        [DefaultKeybinding(KeyCode.LeftCtrl, KeyCode.Left)]
        MoveLeft,
        [Action("Move up", "Move the window next to the window above")]
        [DefaultKeybinding(KeyCode.LeftCtrl, KeyCode.Up)]
        MoveUp,
        [Action("Move right", "Move the window next to the window on the right")]
        [DefaultKeybinding(KeyCode.LeftCtrl, KeyCode.Right)]
        MoveRight,
        [Action("Move down", "Move the window next to the window below")]
        [DefaultKeybinding(KeyCode.LeftCtrl, KeyCode.Down)]
        MoveDown,
        [Action("Swap left", "Swap the current window with the window on its left")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.Left)]
        SwapLeft,
        [Action("Swap up", "Swap the current window with the window above it")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.Up)]
        SwapUp,
        [Action("Swap right", "Swap the current window with the window on its right")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.Right)]
        SwapRight,
        [Action("Swap down", "Swap the current window with the window below it")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.Down)]
        SwapDown,

        // Group: Virtual Desktops
        [Action("Switch to previous desktop", "Switch from the current virtual desktop to the previous virtual desktop")]
        [DefaultKeybinding(KeyCode.Q)]
        SwitchToPreviousDesktop,
        [Action("Switch to desktop on the left", "Switch from the current virtual desktop to the virtual desktop on the left")]
        [DefaultKeybinding(KeyCode.Z)]
        SwitchToLeftDesktop,
        [Action("Switch to desktop on the right", "Switch from the current virtual desktop to the virtual desktop on the right")]
        [DefaultKeybinding(KeyCode.X)]
        SwitchToRightDesktop,
        [Action("Switch to desktop 1", "Switch from the current virtual desktop to virtual desktop 1")]
        [DefaultKeybinding(KeyCode.D1)]
        SwitchToDesktop1,
        [Action("Switch to desktop 2", "Switch from the current virtual desktop to virtual desktop 2")]
        [DefaultKeybinding(KeyCode.D2)]
        SwitchToDesktop2,
        [Action("Switch to desktop 3", "Switch from the current virtual desktop to virtual desktop 3")]
        [DefaultKeybinding(KeyCode.D3)]
        SwitchToDesktop3,
        [Action("Switch to desktop 4", "Switch from the current virtual desktop to virtual desktop 4")]
        [DefaultKeybinding(KeyCode.D4)]
        SwitchToDesktop4,
        [Action("Switch to desktop 5", "Switch from the current virtual desktop to virtual desktop 5")]
        [DefaultKeybinding(KeyCode.D5)]
        SwitchToDesktop5,
        [Action("Switch to desktop 6", "Switch from the current virtual desktop to virtual desktop 6")]
        [DefaultKeybinding(KeyCode.D6)]
        SwitchToDesktop6,
        [Action("Switch to desktop 7", "Switch from the current virtual desktop to virtual desktop 7")]
        [DefaultKeybinding(KeyCode.D7)]
        SwitchToDesktop7,
        [Action("Switch to desktop 8", "Switch from the current virtual desktop to virtual desktop 8")]
        [DefaultKeybinding(KeyCode.D8)]
        SwitchToDesktop8,
        [Action("Switch to desktop 9", "Switch from the current virtual desktop to virtual desktop 9")]
        [DefaultKeybinding(KeyCode.D9)]
        SwitchToDesktop9,

        [Action("Move window to previous desktop", "Move the focused window from the current virtual desktop to the previous virtual desktop")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.Q)]
        MoveToPreviousDesktop,
        [Action("Move window to left desktop", "Move the focused window from the current virtual desktop to virtual desktop on the left")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.Z)]
        MoveToLeftDesktop,
        [Action("Move window to right desktop", "Move the focused window from the current virtual desktop to virtual desktop on the right")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.X)]
        MoveToRightDesktop,
        [Action("Move window to desktop 1", "Move the focused window from the current virtual desktop to virtual desktop 1")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D1)]
        MoveToDesktop1,
        [Action("Move window to desktop 2", "Move the focused window from the current virtual desktop to virtual desktop 2")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D2)]
        MoveToDesktop2,
        [Action("Move window to desktop 3", "Move the focused window from the current virtual desktop to virtual desktop 3")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D3)]
        MoveToDesktop3,
        [Action("Move window to desktop 4", "Move the focused window from the current virtual desktop to virtual desktop 4")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D4)]
        MoveToDesktop4,
        [Action("Move window to desktop 5", "Move the focused window from the current virtual desktop to virtual desktop 5")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D5)]
        MoveToDesktop5,
        [Action("Move window to desktop 6", "Move the focused window from the current virtual desktop to virtual desktop 6")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D6)]
        MoveToDesktop6,
        [Action("Move window to desktop 7", "Move the focused window from the current virtual desktop to virtual desktop 7")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D7)]
        MoveToDesktop7,
        [Action("Move window to desktop 8", "Move the focused window from the current virtual desktop to virtual desktop 8")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D8)]
        MoveToDesktop8,
        [Action("Move window to desktop 9", "Move the focused window from the current virtual desktop to virtual desktop 9")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.D9)]
        MoveToDesktop9,

        // Group: Multiple Displays
        [Action("Switch to previous display", "Switch from the current display to the previous display")]
        [DefaultKeybinding(KeyCode.E)]
        SwitchToPreviousDisplay,
        [Action("Switch to display 1", "Switch from the current display to display 1")]
        [DefaultKeybinding(KeyCode.F1)]
        SwitchToDisplay1,
        [Action("Switch to display 2", "Switch from the current display to display 2")]
        [DefaultKeybinding(KeyCode.F2)]
        SwitchToDisplay2,
        [Action("Switch to display 3", "Switch from the current display to display 3")]
        [DefaultKeybinding(KeyCode.F3)]
        SwitchToDisplay3,
        [Action("Switch to display 4", "Switch from the current display to display 4")]
        [DefaultKeybinding(KeyCode.F4)]
        SwitchToDisplay4,
        [Action("Switch to display 5", "Switch from the current display to display 5")]
        [DefaultKeybinding(KeyCode.F5)]
        SwitchToDisplay5,
        [Action("Switch to display 6", "Switch from the current display to display 6")]
        [DefaultKeybinding(KeyCode.F6)]
        SwitchToDisplay6,
        [Action("Switch to display 7", "Switch from the current display to display 7")]
        [DefaultKeybinding(KeyCode.F7)]
        SwitchToDisplay7,
        [Action("Switch to display 8", "Switch from the current display to display 8")]
        [DefaultKeybinding(KeyCode.F8)]
        SwitchToDisplay8,
        [Action("Switch to display 9", "Switch from the current display to display 9")]
        [DefaultKeybinding(KeyCode.F9)]
        SwitchToDisplay9,
        [Action("Move window to previous display", "Move the focused window from the current display to the previous display")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.E)]
        MoveToPreviousDisplay,
        [Action("Move window to display 1", "Move the focused window from the current display to display 1")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F1)]
        MoveToDisplay1,
        [Action("Move window to display 2", "Move the focused window from the current display to display 2")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F2)]
        MoveToDisplay2,
        [Action("Move window to display 3", "Move the focused window from the current display to display 3")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F3)]
        MoveToDisplay3,
        [Action("Move window to display 4", "Move the focused window from the current display to display 4")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F4)]
        MoveToDisplay4,
        [Action("Move window to display 5", "Move the focused window from the current display to display 5")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F5)]
        MoveToDisplay5,
        [Action("Move window to display 6", "Move the focused window from the current display to display 6")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F6)]
        MoveToDisplay6,
        [Action("Move window to display 7", "Move the focused window from the current display to display 7")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F7)]
        MoveToDisplay7,
        [Action("Move window to display 8", "Move the focused window from the current display to display 8")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F8)]
        MoveToDisplay8,
        [Action("Move window to display 9", "Move the focused window from the current display to display 9")]
        [DefaultKeybinding(KeyCode.LeftShift, KeyCode.F9)]
        MoveToDisplay9,
    }
}
