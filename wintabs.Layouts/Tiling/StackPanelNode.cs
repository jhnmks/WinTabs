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


        private List<TilingNode> m_children = [];
        private WindowNode? m_selectedTab;

        /// <summary>
        /// Label shown on the group's badge: the lowest number not used by another group with tabs.
        /// Groups without tabs have no number (0).
        /// </summary>
        public int GroupNumber => m_cloneNumber ?? GroupNumbers.Get(this);

        // Clones (used for layout previews) show the original's number without claiming one.
        private int? m_cloneNumber;

        public WindowNode? SelectedTab => m_selectedTab?.Parent == this ? m_selectedTab : m_children.OfType<WindowNode>().FirstOrDefault();

        public void SelectTab(WindowNode tab)
        {
            if (tab.Parent != this)
                throw new ArgumentException("The tab must belong to this group.", nameof(tab));

            m_selectedTab = tab;
        }

        protected override void AttachCore(int index, TilingNode node)
        {
            m_children.Insert(index, node);
        }

        protected override void DetachCore(TilingNode node)
        {
            if (node == m_selectedTab)
                m_selectedTab = null;

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

            foreach (var child in m_children)
            {
                RectangleF childRect = rect;
                if (child is WindowNode)
                {
                    RectangleF padding = new(Spacing / 2, Spacing / 2, Spacing / 2, Spacing / 2);
                    childRect = childRect.Pad(padding);
                }
                child.Arrange(childRect);
            }
        }

        internal override void SetReference(int index, TilingNode node)
        {
            m_children[index] = node;
        }

        public override object Clone()
        {
            var copy = (StackPanelNode)base.Clone();
            copy.m_cloneNumber = GroupNumber;
            copy.m_children = m_children.Select(x => (TilingNode)x.Clone()).ToList();
            copy.m_selectedTab = m_selectedTab != null ? copy.m_children.OfType<WindowNode>()
                .FirstOrDefault(tab => tab.GenerationID == m_selectedTab.GenerationID) : null;
            foreach (var child in copy.m_children)
            {
                child.Parent = copy;
            }
            return copy;
        }

        public override Point GetMaxChildSize(TilingNode node)
        {
            if (!Children.Contains(node))
            {
                throw new InvalidOperationException($"Node {node} is not a child of {this}");
            }

            return GetMaxSizeForInsert(node);
        }

        public override Point GetMaxSizeForInsert(TilingNode node)
        {
            if (node is WindowNode)
            {
                return new Point(ComputedRectangle.Width - Spacing, ComputedRectangle.Height - Spacing);
            }
            return ComputedRectangle.Size;
        }

        internal override void MeasureCore()
        {
            int width = 0, height = 0;
            foreach (var child in Children)
            {
                child.Measure();
                var minChild = child.MinSize;
                if (child is WindowNode)
                {
                    minChild = new Point(minChild.X + Spacing, minChild.Y + Spacing);
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
