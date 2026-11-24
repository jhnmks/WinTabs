namespace wintabs.Layouts.Tiling
{
    public abstract class GridLikeNode : PanelNode
    {
        public abstract bool CanResizeInOrientation(PanelOrientation orientation);

        public abstract bool ResizeBy(TilingNode node, double delta, GrowDirection direction);
    }
}
