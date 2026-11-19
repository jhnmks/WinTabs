using WinMan;

namespace wintabs.Utilities
{
    internal static class WorkspaceExtensions
    {
        public static int Index(this IDisplay display)
        {
            return display.Workspace.DisplayManager.Displays.IndexOf(display);
        }
    }
}
