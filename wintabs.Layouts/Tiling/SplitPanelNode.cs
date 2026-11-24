using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using WinMan;

using static wintabs.Layouts.Flex;

namespace wintabs.Layouts.Tiling
{
    public class SplitPanelNode : GridLikeNode
    {
        public override TilingNodeType Type => TilingNodeType.Split;

        public override IReadOnlyList<TilingNode> Children => m_children;

        public PanelOrientation Orientation { get; set; }

        private readonly List<TilingNode> m_children = [];

        private readonly Flex m_constraints;

        public SplitPanelNode()
        {
            m_constraints = new Flex();
            m_constraints.SetContainerWidth(1);
        }

        protected override void AttachCore(int index, TilingNode node)
        {
            m_children.Insert(index, node);
            m_constraints.InsertItem(index, 0, 0);
        }

        protected override void DetachCore(TilingNode node)
        {
            int index = m_children.IndexOf(node);
            if (index == -1)
            {
                throw new InvalidOperationException();
            }

            m_children.RemoveAt(index);
            m_constraints.RemoveItem(index);
        }

        internal override void MeasureCore()
        {
            int width = 0;
            int height = 0;
            if (Orientation == PanelOrientation.Horizontal)
            {
                foreach (var child in Children)
                {
                    child.Measure();
                    var childRect = child.MinSize;
                    width += childRect.X;
                    height = Math.Max(height, childRect.Y);
                }
            }
            else
            {
                foreach (var child in Children)
                {
                    child.Measure();
                    var childRect = child.MinSize;
                    height += childRect.Y;
                    width = Math.Max(width, childRect.X);
                }
            }
            var spacing = (m_children.OfType<WindowNode>().Count() + 1) * Spacing / 2;
            ContentMinSize = new Point(width + spacing + Padding.Left + Padding.Right, height + spacing + Padding.Top + Padding.Bottom);
            ContentMaxSize = new Point(short.MaxValue, short.MaxValue);
        }

        internal override void ArrangeCore(RectangleF rect)
        {
            if (Parent == null)
            {
                rect = rect.Pad(new RectangleF(Spacing / 2, Spacing / 2, Spacing / 2, Spacing / 2));
            }

            var containerWidth = Orientation == PanelOrientation.Horizontal
                ? rect.Width
                : rect.Height;
            m_constraints.SetContainerWidth(Math.Max(1, containerWidth));

            var newConstraints = m_constraints
                .Select(x => (x.MinWidth, x.MaxWidth))
                .ToArray();
            for (int i = 0; i < m_children.Count; i++)
            {
                var minWidth = Orientation == PanelOrientation.Horizontal
                    ? m_children[i].MinSize.X
                    : m_children[i].MinSize.Y;
                if (m_children[i] is WindowNode)
                {
                    minWidth += Spacing;
                }

                var maxWidth = Orientation == PanelOrientation.Horizontal
                    ? m_children[i].MaxSize.X
                    : m_children[i].MaxSize.Y;

                if (m_constraints[i].MaxWidth == 0)
                {
                    // Initialize
                    m_constraints.RemoveItem(i);
                    try
                    {
                        m_constraints.InsertItem(i, minWidth, maxWidth);
                    }
                    catch (UnsatisfiableFlexConstraintsException)
                    {
                        // Keep a consistent state
                        m_constraints.InsertItem(i, 0, 0);
                        throw;
                    }
                }
                newConstraints[i].MinWidth = minWidth;
                newConstraints[i].MaxWidth = maxWidth;
            }

            m_constraints.UpdateConstraints(newConstraints);

            RectangleF lastRect = RectangleF.OffsetAndSize(rect.Left, rect.Top, 0, 0);
            for (int i = 0; i < m_children.Count; i++)
            {
                var child = m_children[i];
                var constraints = m_constraints[i];
                RectangleF childRect;
                if (Orientation == PanelOrientation.Horizontal)
                {
                    var w = constraints.Width;
                    childRect = RectangleF.OffsetAndSize(lastRect.Right, rect.Top, w, rect.Height);
                    lastRect = childRect;
                }
                else
                {
                    var h = constraints.Width;
                    childRect = RectangleF.OffsetAndSize(rect.Left, lastRect.Bottom, rect.Width, h);
                    lastRect = childRect;
                }

                if (child is WindowNode)
                {
                    RectangleF padding = new(Spacing / 2, Spacing / 2, Spacing / 2, Spacing / 2);
                    childRect = childRect.Pad(padding);
                }
                child.Arrange(childRect);
            }
        }

        public bool ResizeDivider(int index, double delta)
        {
            return index >= 0 && index < Children.Count - 1 && m_constraints.ResizeDivider(index, delta);
        }

        public override bool CanResizeInOrientation(PanelOrientation orientation)
        {
            return Orientation == orientation;
        }

        public override bool ResizeBy(TilingNode node, double delta, GrowDirection direction)
        {
            if (Children.Count == 1)
            {
                return false;
            }

            try
            {
                int index = m_children.IndexOf(node);
                var item = m_children[index];
                var length = m_constraints.ContainerWidth;
                var newWeight = (m_constraints[index].Width + delta) / length;

                double minWeight = 1.0 / 16;
                double clampedWeight = Math.Min(1.0 - minWeight, Math.Max(minWeight, newWeight));
                m_constraints.ResizeItem(index, clampedWeight * length, direction);
                return true;
            }
            catch (UnsatisfiableFlexConstraintsException e)
            {
                Console.WriteLine(e);
                return false;
            }
        }

        internal override void SetReference(int index, TilingNode node)
        {
            m_children[index] = node;
        }

        public override void Move(int fromIndex, int toIndex)
        {
            var n = m_children[fromIndex];
            m_children.RemoveAt(fromIndex);
            m_children.Insert(toIndex, n);

            m_constraints.MoveItem(fromIndex, toIndex);
        }
    }

    //class StackPanel : Panel
    //{
    //    public override NodeType Type => NodeType.Stack;
    //}
}
