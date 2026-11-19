using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

using wintabs.ViewModels;

namespace wintabs.Controls
{
    /// <summary>
    /// Interaction logic for TilingOverlay.xaml
    /// </summary>
    public partial class TilingOverlay : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TilingOverlayViewModel),
            typeof(TilingOverlay),
            new PropertyMetadata(null));

        public TilingOverlayViewModel ViewModel
        {
            get => (TilingOverlayViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public TilingOverlay()
        {
            InitializeComponent();
        }

        internal event Action<SplitDividerViewModel, double>? DividerResized;
        private (SplitDividerViewModel Divider, Point Start)? m_resize;

        private void OnDividerDragStarted(object sender, DragStartedEventArgs e)
        {
            if (sender is Thumb { DataContext: SplitDividerViewModel divider })
                m_resize = (divider, PointToScreen(Mouse.GetPosition(this)));
        }

        private void OnDividerDragCompleted(object sender, DragCompletedEventArgs e)
        {
            var resize = m_resize;
            m_resize = null;
            if (e.Canceled || resize is not { } drag || PresentationSource.FromVisual(this) == null)
                return;

            var delta = PointToScreen(Mouse.GetPosition(this)) - drag.Start;
            DividerResized?.Invoke(drag.Divider, drag.Divider.IsVertical ? delta.X : delta.Y);
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ViewModelProperty)
            {
                if (e.OldValue is ViewModelBase oldViewModel)
                {
                    oldViewModel.PropertyChanged -= OnDataContextPropertyChanged;
                }
                DataContext = ViewModel;
                ViewModel.PropertyChanged += OnDataContextPropertyChanged;
            }
        }

        private void OnDataContextPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Duration duration = new(TimeSpan.FromMilliseconds(200));
            var ease = new SineEase
            {
                //Bounces = 2,
                EasingMode = EasingMode.EaseOut,
                //Bounciness = 2
            };

            if (e.PropertyName == nameof(TilingOverlayViewModel.OverlayVisibility))
            {
                BeginAnimation(OpacityProperty, null);

                DoubleAnimation opacityAnimation = new(1, duration)
                {
                    EasingFunction = ease,
                };

                if (ViewModel.OverlayVisibility == Visibility.Visible)
                {
                    opacityAnimation.From = 0;
                    opacityAnimation.To = 1;
                    IsHitTestVisible = true;
                }
                else
                {
                    opacityAnimation.From = 1;
                    opacityAnimation.To = 0;
                    IsHitTestVisible = false;
                }

                BeginAnimation(OpacityProperty, opacityAnimation);
            }
        }
    }
}
