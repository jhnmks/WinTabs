using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using wintabs.Utilities;
using wintabs.ViewModels;

using Serilog;

namespace wintabs.Windows
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly ILogger m_logger = App.Current.Logger;
        private readonly SettingsViewModel m_viewModel;

        public SettingsWindow(SettingsViewModel viewModel)
        {
            m_logger.Debug($"Initialising {nameof(SettingsWindow)}");
            m_viewModel = viewModel;
            DataContext = viewModel;

            InitializeComponent();
            m_logger.Debug($"Initialised {nameof(SettingsWindow)} successfully");
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            FocusManager.SetFocusedElement(this, this);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            m_viewModel.Dispose();
            FocusManager.SetFocusedElement(this, null);
            Keyboard.ClearFocus();
            GCHelper.ScheduleCollection();
        }

        private void PagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = e.AddedItems.Cast<PageItem>().First();
            GoToPage(item.Page!);
        }

        public void GoToPage(Type pageType)
        {
            var page = (UIElement)Activator.CreateInstance(pageType, m_viewModel)!;
            Dispatcher.InvokeAsync(() => PageContent.Child = page, DispatcherPriority.ContextIdle);
        }

        private void OnQuitButtonClick(object sender, RoutedEventArgs e)
        {
            App.Current.Terminate();
        }
    }
}
