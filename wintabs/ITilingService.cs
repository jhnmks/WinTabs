using System;
using System.Threading.Tasks;

using WinMan;
using wintabs.Layouts.Tiling;
using wintabs.Utilities;
using System.Collections.Generic;

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



        IReadOnlyCollection<IWindowMatcher> ExclusionMatchers { get; set; }

        bool DiscoverWindows();
        void Refresh();
        void SetWorkspacePaused(bool paused);
        void Float();
        Task ToggleDesktopAsync();
        void CycleTab(int offset);
        void MoveTab(int offset);
        void TileTab(TilingDirection direction);
        bool MoveTabToGroup(int groupNumber);

        void Stop();
        void Start();
        Rectangle GetBounds();
    }
}
