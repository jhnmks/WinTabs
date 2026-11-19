using System;
using System.Collections.ObjectModel;

using WinMan;

namespace wintabs.ViewModels
{
    public class TilingPanelViewModel : TilingNodeViewModel
    {
        private ObservableCollection<TilingNodeViewModel> m_childNodes = [];
        private Rectangle m_bounds;
        private bool m_isHeaderObscured;

        private bool m_childHasDirectFocus;

        internal Action<Point>? BeginGroupDrag { get; set; }

        public ObservableCollection<TilingNodeViewModel> ChildNodes { get => m_childNodes; set => SetField(ref m_childNodes, value); }

        public Rectangle HeaderBounds { get => m_bounds; set => SetField(ref m_bounds, value); }

        public bool IsHeaderVisible { get => m_isHeaderObscured; set => SetField(ref m_isHeaderObscured, value); }

        public bool ChildHasDirectFocus { get => m_childHasDirectFocus; set => SetField(ref m_childHasDirectFocus, value); }

        public int GroupNumber { get => m_groupNumber; set => SetField(ref m_groupNumber, value); }

        private int m_groupNumber;
    }
}
