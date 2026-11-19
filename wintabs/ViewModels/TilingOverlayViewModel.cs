using System.Collections.ObjectModel;
using System.Windows;

using WinMan;

namespace wintabs.ViewModels
{
    internal sealed record SplitDividerViewModel(Rectangle Bounds, Layouts.Tiling.SplitPanelNode Split, int Index)
    {
        public bool IsVertical => Split.Orientation == Layouts.Tiling.PanelOrientation.Horizontal;
        public System.Windows.Input.Cursor Cursor => IsVertical ? System.Windows.Input.Cursors.SizeWE : System.Windows.Input.Cursors.SizeNS;
    }

    public class TilingOverlayViewModel : ViewModelBase
    {
        private Visibility m_overlayVisibility;
        private ObservableCollection<TilingPanelViewModel> m_panelElements = [];
        private ObservableCollection<TilingWindowViewModel> m_windowElements = [];
        private System.Collections.Generic.IReadOnlyList<SplitDividerViewModel> m_dividers = [];
        private System.Windows.Media.Brush? m_focusBorderBrush;
        private Rectangle m_previewRectangle;
        private double m_displayScaling;
        private double m_fontSize;
        private double m_iconSize;

        public double DisplayScaling { get => m_displayScaling; set => SetField(ref m_displayScaling, value); }
        public double FontSize { get => m_fontSize; set => SetField(ref m_fontSize, value); }
        public double IconSize { get => m_iconSize; set => SetField(ref m_iconSize, value); }

        public Visibility OverlayVisibility { get => m_overlayVisibility; set => SetField(ref m_overlayVisibility, value); }

        public ObservableCollection<TilingPanelViewModel> PanelElements { get => m_panelElements; set => SetField(ref m_panelElements, value); }

        public ObservableCollection<TilingWindowViewModel> WindowElements { get => m_windowElements; set => SetField(ref m_windowElements, value); }

        public System.Collections.IEnumerable Dividers => m_dividers;

        internal void SetDividers(System.Collections.Generic.IReadOnlyList<SplitDividerViewModel> dividers)
        {
            if (!System.Linq.Enumerable.SequenceEqual(m_dividers, dividers))
            {
                m_dividers = dividers;
                NotifyPropertyChanged(nameof(Dividers));
            }
        }

        public System.Windows.Media.Brush? FocusBorderBrush { get => m_focusBorderBrush; set => SetField(ref m_focusBorderBrush, value); }
        public Rectangle PreviewRectangle { get => m_previewRectangle; set => SetField(ref m_previewRectangle, value); }

        [DerivedProperty(nameof(PreviewRectangle))]
        public bool IsPreviewRectangleVisible => m_previewRectangle.Width > 0;
    }
}
