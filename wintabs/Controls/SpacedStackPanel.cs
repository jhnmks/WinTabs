using System;
using System.Windows;
using System.Windows.Controls;

namespace wintabs.Controls
{
    /// <summary>
    /// A <see cref="StackPanel"/> that inserts a fixed gap between visible children.
    /// </summary>
    public class SpacedStackPanel : StackPanel
    {
        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            nameof(Spacing), typeof(double), typeof(SpacedStackPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        private bool IsHorizontal => Orientation == Orientation.Horizontal;

        protected override Size MeasureOverride(Size constraint)
        {
            var available = IsHorizontal
                ? new Size(double.PositiveInfinity, constraint.Height)
                : new Size(constraint.Width, double.PositiveInfinity);

            double along = 0, across = 0;
            int visible = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(available);
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                visible++;
                var size = child.DesiredSize;
                along += IsHorizontal ? size.Width : size.Height;
                across = Math.Max(across, IsHorizontal ? size.Height : size.Width);
            }

            along += Math.Max(0, visible - 1) * Spacing;
            return IsHorizontal ? new Size(along, across) : new Size(across, along);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            double offset = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                var size = child.DesiredSize;
                if (IsHorizontal)
                {
                    child.Arrange(new Rect(offset, 0, size.Width, arrangeSize.Height));
                    offset += size.Width + Spacing;
                }
                else
                {
                    child.Arrange(new Rect(0, offset, arrangeSize.Width, size.Height));
                    offset += size.Height + Spacing;
                }
            }
            return arrangeSize;
        }
    }
}
