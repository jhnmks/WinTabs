using System;
using System.Threading.Tasks;

using WinMan;
using wintabs.Layouts.Tiling;


namespace wintabs
{
    internal interface ITilingService : IDisposable
    {

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
