using System;
using System.Threading.Tasks;

using WinMan;
using wintabs.Layouts.Tiling;


namespace wintabs
{
    internal class TilingFailedEventArgs(TilingError reason, IWindow? window = null) : EventArgs
    {
        public TilingError FailReason { get; } = reason;
        public IWindow? FailSource { get; } = window;
    }

    internal interface ITilingService : IDisposable
    {
        event EventHandler<TilingFailedEventArgs> PlacementFailed;


        bool Active { get; }

        IWorkspace Workspace { get; }

        bool DiscoverWindows();
        void Refresh();
        void SetWorkspacePaused(bool paused);

        Task ToggleDesktopAsync();
        void CycleTab(int offset);
        void CycleGroup(int offset);
        void CreateGroup();
        void UnpackGroups();
        void MoveGroup(int offset);
        void MoveTab(int offset);
        void TileTab(TilingDirection direction);
        bool MoveTabToGroup(int groupNumber);

        void Stop();
        void Start();
        Rectangle GetBounds();
    }
}
