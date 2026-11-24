using System.Windows.Input;

using WinMan;
using wintabs.Layouts.Tiling;

namespace wintabs.ViewModels
{
    public abstract class TilingNodeViewModel : ViewModelBase
    {
        private TilingOverlayViewModel? m_overlay;
        private TilingNode? m_node;
        private bool m_hasFocus;
        private bool m_isActiveTab;
        private Rectangle m_windowBounds;
        private ICommand? m_primaryActionCommand;
        private ICommand? m_closeActionCommand;

        public TilingOverlayViewModel? Overlay { get => m_overlay; set => SetField(ref m_overlay, value); }
        public TilingNode? Node { get => m_node; set => SetField(ref m_node, value); }
        public bool HasFocus { get => m_hasFocus; set => SetField(ref m_hasFocus, value); }
        public bool IsActiveTab { get => m_isActiveTab; set => SetField(ref m_isActiveTab, value); }
        public Rectangle ComputedBounds { get => m_windowBounds; set => SetField(ref m_windowBounds, value); }

        public ICommand? PrimaryActionCommand { get => m_primaryActionCommand; set => SetField(ref m_primaryActionCommand, value); }

        public ICommand? CloseCommand { get => m_closeActionCommand; set => SetField(ref m_closeActionCommand, value); }

        public ICommand? SplitTabCommand { get; internal set; }

        /// <summary>"Put in new Group" on a window's tab; only for tabs that are not in a container yet.</summary>
        public ICommand? WrapInContainerCommand { get; internal set; }
        public ICommand? RenameCommand { get; internal set; }
        public ICommand? UnpackCommand { get; internal set; }
        public bool IsContainer { get => m_isContainer; set => SetField(ref m_isContainer, value); }
        private bool m_isContainer;

        public bool CanWrapInContainer { get => m_canWrapInContainer; set => SetField(ref m_canWrapInContainer, value); }
        private bool m_canWrapInContainer;

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
        public override void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
        {
            base.Dispose();
            PrimaryActionCommand = null;
            CloseCommand = null;
            SplitTabCommand = null;
            WrapInContainerCommand = null;
            RenameCommand = null;
            UnpackCommand = null;
        }

    }

}
