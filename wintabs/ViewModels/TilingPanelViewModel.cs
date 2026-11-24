using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

using WinMan;

namespace wintabs.ViewModels
{
    public class TilingPanelViewModel : TilingNodeViewModel
    {
        private ObservableCollection<TilingNodeViewModel> m_childNodes = [];
        private Rectangle m_bounds;
        private bool m_isHeaderObscured;


        internal Action<Point, Action>? BeginGroupDrag { get; set; }

        public ObservableCollection<TilingNodeViewModel> ChildNodes { get => m_childNodes; set => SetField(ref m_childNodes, value); }

        public ObservableCollection<TilingNodeViewModel> OuterTabs { get; } = [];
        public ObservableCollection<TilingNodeViewModel> OuterTabsAfterUngrouped { get; } = [];
        public ObservableCollection<TilingNodeViewModel> WindowTabs { get; } = [];
        public ICommand? SelectUngroupedCommand { get; internal set; }
        public bool HasGroupRow { get => m_hasGroupRow; set => SetField(ref m_hasGroupRow, value); }
        private bool m_hasGroupRow;
        public bool ShowUngroupedRow { get => m_showUngroupedRow; set => SetField(ref m_showUngroupedRow, value); }
        private bool m_showUngroupedRow;
        public bool HasUngroupedTabs { get => m_hasUngroupedTabs; set => SetField(ref m_hasUngroupedTabs, value); }
        private bool m_hasUngroupedTabs;
        public double RowHeight { get => m_rowHeight; set => SetField(ref m_rowHeight, value); }
        private double m_rowHeight = 18;
        public double RowGap { get => m_rowGap; set => SetField(ref m_rowGap, value); }
        private double m_rowGap;

        public Rectangle HeaderBounds { get => m_bounds; set => SetField(ref m_bounds, value); }

        public bool IsHeaderVisible { get => m_isHeaderObscured; set => SetField(ref m_isHeaderObscured, value); }


        public int GroupNumber { get => m_groupNumber; set => SetField(ref m_groupNumber, value); }

        private int m_groupNumber;


        /// <summary>The label of a container's tab: its name, or its numbered default title.</summary>
        public string Title { get => m_title; set => SetField(ref m_title, value); }
        private string m_title = "";

    }
}
