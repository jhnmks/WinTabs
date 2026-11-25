using System;
using System.Collections.Generic;
using System.Linq;

using WinMan;

namespace wintabs.Layouts.Tiling
{
    public class StackPanelNode : PanelNode
    {
        public override TilingNodeType Type => TilingNodeType.Stack;

        public override IReadOnlyList<TilingNode> Children => m_children;


        private readonly List<TilingNode> m_children = [];
        private TilingNode? m_selected;
        private WindowNode? m_ungroupedSelected;

        public WindowNode? UngroupedSelectedTab => m_ungroupedSelected?.Parent == this
            ? m_ungroupedSelected : Children.OfType<WindowNode>().FirstOrDefault();

        /// <summary>
        /// A container is a tab of a group that holds tabs of its own, shown in a second tab bar. Containers sit
        /// directly in a group and never hold other containers.
        /// </summary>
        public bool IsContainer => Parent is StackPanelNode;
        public int UngroupedIndex { get; set; } = int.MaxValue;

        public bool HasContainers => !IsContainer && Children.Any(child => child is StackPanelNode);

        /// <summary>The name the user gave a container, or <c>null</c> to use its default title.</summary>
        public string? Name { get; set; }

        public string ContainerTitle => Name ?? $"Group {GroupNumber}";

        /// <summary>
        /// Label shown on the group's badge, shared across displays.
        /// Groups without tabs have no number (0).
        /// </summary>
        public int GroupNumber => GroupNumbers.Get(this);

        /// <summary>The tab shown in this row: a window, or a container.</summary>
        public TilingNode? SelectedChild => m_selected?.Parent == this ? m_selected : m_children.FirstOrDefault();

        /// <summary>The window shown: the selected tab, or the window selected inside the selected container.</summary>
        public WindowNode? SelectedTab => SelectedChild switch
        {
            WindowNode tab => tab,
            StackPanelNode container => container.SelectedTab,
            _ => null,
        };

        public void SelectTab(TilingNode child)
        {
            if (child.Parent != this)
                throw new ArgumentException("The tab must belong to this group.", nameof(child));

            m_selected = child;
            if (child is WindowNode window)
                m_ungroupedSelected = window;
        }

        protected override void AttachCore(int index, TilingNode node)
        {
            m_children.Insert(index, node);
        }

        protected override void DetachCore(TilingNode node)
        {
            if (node == m_selected)
                m_selected = null;
            if (node == m_ungroupedSelected)
                m_ungroupedSelected = null;

            if (!m_children.Remove(node))
            {
                throw new InvalidOperationException();
            }
        }

        internal override void ArrangeCore(RectangleF rect)
        {
            if (Parent == null)
            {
                rect = rect.Pad(new RectangleF(Padding));
            }

            // Every tab fills the group's window area; a container's own Padding makes room for its tab bar.
            foreach (var child in m_children)
            {
                RectangleF childRect = rect;
                if (child is WindowNode)
                {
                    int leading = Spacing / 2;
                    int trailing = Spacing - leading;
                    RectangleF padding = new(leading, leading, trailing, trailing);
                    childRect = childRect.Pad(padding);
                    if (HasContainers)
                        childRect = childRect.Pad(new RectangleF(0, Padding.Top, 0, 0));
                }
                child.Arrange(childRect);
            }
        }

        internal override void SetReference(int index, TilingNode node)
        {
            m_children[index] = node;
        }

        public Point GetAvailableTabSize() =>
            new(ComputedRectangle.Width - Spacing, ComputedRectangle.Height - Spacing - (HasContainers ? Padding.Top : 0));

        internal override void MeasureCore()
        {
            int width = 0, height = 0;
            foreach (var child in Children)
            {
                child.Measure();
                var minChild = child.MinSize;
                if (child is WindowNode)
                {
                    minChild = new Point(minChild.X + Spacing, minChild.Y + Spacing + (HasContainers ? Padding.Top : 0));
                }
                width = Math.Max(width, minChild.X);
                height = Math.Max(height, minChild.Y);
            }
            ContentMinSize = new Point(width, height);
        }

        public override void Move(int fromIndex, int toIndex)
        {
            var n = m_children[fromIndex];
            m_children.RemoveAt(fromIndex);
            m_children.Insert(toIndex, n);
        }
    }

    //class StackPanel : Panel
    //{
    //    public override NodeType Type => NodeType.Stack;
    //}
}
