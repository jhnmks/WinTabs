using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

using WinMan;
using wintabs.Layouts.Tiling;
using System.Linq;

namespace wintabs.ViewModels
{
    public class TilingWindowViewModel : TilingNodeViewModel
    {
        internal Action<StackPanelNode>? MoveToContainer { get; set; }

        private enum RevealState
        {
            /// <summary>
            /// Not enabled, mouse is far away
            /// </summary>
            Hidden,
            /// <summary>
            /// Disabled by mouse movement from the side.
            /// </summary>
            IndicatorVisible,
            /// <summary>
            /// Enabled
            /// </summary>
            Visible,
        }

        public string? Title { get => m_title; set => SetField(ref m_title, value); }
        public string AppName { get => m_appName; set => SetField(ref m_appName, value); }
        private string m_appName = "App";

        public Visibility ActionsVisibility { get => m_actionsVisibility; set => SetField(ref m_actionsVisibility, value); }

        public double ActionsHeight { get => m_actionsHeight; set => SetField(ref m_actionsHeight, value); }

        public double RevealHighlightRadius { get => m_revealHighlightRadius; set => SetField(ref m_revealHighlightRadius, value); }

        public double RevealHighlightOpacity { get => m_revealHighlightOpacity; set => SetField(ref m_revealHighlightOpacity, value); }

        private IWorkspace? m_workspace;
        private string? m_title;
        private Visibility m_actionsVisibility = Visibility.Hidden;
        private double m_actionsHeight = 22;
        private RevealState m_actionsRevealState = RevealState.Hidden;
        private double m_revealHighlightOpacity = 0;
        private double m_revealHighlightRadius = 64;
        private bool m_isMoving = false;
        private WindowNode? m_currentNode;

        public event RoutedEventHandler? FloatActionPressed;
        public event RoutedEventHandler? AlwaysFloatPressed;

        public ICommand FloatCommand { get; }
        public ICommand AlwaysFloatCommand { get; }

        /// <summary>"Always float [App]", or "Stop always floating [App]" when the app is already on the list.</summary>
        public string AlwaysFloatText { get => m_alwaysFloatText; private set => SetField(ref m_alwaysFloatText, value); }
        private string m_alwaysFloatText = "Always float";

        internal Func<IWindow, bool> IsAlwaysFloating { get; set; } = _ => false;

        public TilingWindowViewModel()
        {
            FloatCommand = new DelegateCommand(_ => FloatActionPressed?.Invoke(this, new RoutedEventArgs()));
            AlwaysFloatCommand = new DelegateCommand(_ => AlwaysFloatPressed?.Invoke(this, new RoutedEventArgs()));
        }

        /// <summary>Refreshes the menu text just before the menu opens; the list can change at any time.</summary>
        public void UpdateMenu()
        {
            if (Node is WindowNode node)
                AlwaysFloatText = (IsAlwaysFloating(node.WindowReference) ? "Stop always floating " : "Always float ") + AppName;
        }

        public override void Dispose()
        {
            base.Dispose();
            FloatActionPressed = null;
            AlwaysFloatPressed = null;
            Node = null;
        }

        protected override void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            base.NotifyPropertyChanged(propertyName);
            if (propertyName == nameof(Node))
            {
                if (Node is WindowNode node)
                {
                    var workspace = node.WindowReference.Workspace;
                    if (m_workspace != workspace && m_workspace != null)
                    {
                        m_workspace.CursorLocationChanged -= OnCursorLocationChanged;
                    }
                    m_workspace = workspace;

                    if (workspace != null)
                    {
                        workspace.CursorLocationChanged += OnCursorLocationChanged;
                    }

                    if (m_currentNode != null)
                    {
                        m_currentNode.WindowReference.Removed -= WindowReference_Removed;
                        m_currentNode.WindowReference.PositionChangeStart -= WindowReference_PositionChangeStart;
                        m_currentNode.WindowReference.PositionChangeEnd -= WindowReference_PositionChangeEnd;
                        m_currentNode.WindowReference.TitleChanged -= WindowReference_TitleChanged;
                    }
                    m_currentNode = node;
                    node.WindowReference.Removed += WindowReference_Removed;
                    node.WindowReference.PositionChangeStart += WindowReference_PositionChangeStart;
                    node.WindowReference.PositionChangeEnd += WindowReference_PositionChangeEnd;
                    node.WindowReference.TitleChanged += WindowReference_TitleChanged;
                }
                else
                {
                    if (m_currentNode != null)
                    {
                        m_currentNode.WindowReference.Removed -= WindowReference_Removed;
                        m_currentNode.WindowReference.PositionChangeStart -= WindowReference_PositionChangeStart;
                        m_currentNode.WindowReference.PositionChangeEnd -= WindowReference_PositionChangeEnd;
                        m_currentNode.WindowReference.TitleChanged -= WindowReference_TitleChanged;
                    }
                    if (m_workspace != null)
                    {
                        m_workspace.CursorLocationChanged -= OnCursorLocationChanged;
                    }
                    m_workspace = null;
                    m_currentNode = null;
                }
            }
        }

        private void WindowReference_TitleChanged(object? sender, WindowTitleChangedEventArgs e)
        {
            Title = e.NewTitle;
        }

        private void WindowReference_Removed(object? sender, WindowChangedEventArgs e)
        {
            var window = (IWindow)sender!;
            window.Removed -= WindowReference_Removed;
            window.PositionChangeEnd -= WindowReference_PositionChangeEnd;
            window.PositionChangeStart -= WindowReference_PositionChangeStart;
            window.TitleChanged -= WindowReference_TitleChanged;
        }

        private void WindowReference_PositionChangeEnd(object? sender, WindowPositionChangedEventArgs e)
        {
            m_isMoving = false;
        }

        private void WindowReference_PositionChangeStart(object? sender, WindowPositionChangedEventArgs e)
        {
            m_isMoving = true;
        }

        private void OnCursorLocationChanged(object? sender, CursorLocationChangedEventArgs e)
        {
            if (Node is WindowNode node)
            {

                if (node.WindowReference.Workspace.FocusedWindow != node.WindowReference)
                {
                    RevealHighlightOpacity = 0;
                    ActionsVisibility = Visibility.Collapsed;
                    m_actionsRevealState = RevealState.Hidden;
                    return;
                }

                var windowPos = node.WindowReference.Position;

                var x = e.NewLocation.X - windowPos.Left;
                var y = e.NewLocation.Y - windowPos.Top;

                var isInBoundsX = 0 <= x && x <= windowPos.Width;

                var dpi = node.WindowReference.Workspace.DisplayManager.Displays.FirstOrDefault(x => x.Bounds.Contains(windowPos.Center))?.Scaling ?? 1.0;
                var revealHighlightRadius = RevealHighlightRadius * dpi;

                if (isInBoundsX && -(revealHighlightRadius / 2) < y && y <= 0)
                {
                    if (m_actionsRevealState == RevealState.IndicatorVisible)
                    {
                        m_actionsRevealState = RevealState.Visible;
                    }
                }
                else if (isInBoundsX && 0 <= y && y < revealHighlightRadius)
                {
                    if (m_actionsRevealState == RevealState.Hidden)
                    {
                        m_actionsRevealState = RevealState.IndicatorVisible;
                    }
                    RevealHighlightOpacity = 1 - Math.Pow(-y / revealHighlightRadius, 2);
                }
                else
                {
                    m_actionsRevealState = RevealState.Hidden;
                    RevealHighlightOpacity = 0;
                }

                if (m_actionsRevealState != RevealState.IndicatorVisible)
                {
                    RevealHighlightOpacity = 0;
                }

                ActionsVisibility = m_actionsRevealState == RevealState.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (m_isMoving)
                {
                    RevealHighlightOpacity = 0;
                    ActionsVisibility = Visibility.Collapsed;
                }
            }
        }
    }
}
