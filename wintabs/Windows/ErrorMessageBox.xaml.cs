using System;
using System.Windows;


namespace wintabs.Windows
{
    /// <summary>
    /// Interaction logic for ErrorMessageBox.xaml
    /// </summary>
    public partial class ErrorMessageBox : Window
    {
        public static readonly DependencyProperty DetailsVisibilityProperty = DependencyProperty.Register(
            nameof(DetailsVisibility),
            typeof(Visibility),
            typeof(ErrorMessageBox),
            new PropertyMetadata(Visibility.Collapsed));

        public Visibility DetailsVisibility
        {
            get => (Visibility)GetValue(DetailsVisibilityProperty);
            set => SetValue(DetailsVisibilityProperty, value);
        }

        public static readonly DependencyProperty ExceptionObjectProperty = DependencyProperty.Register(
            nameof(ExceptionObject),
            typeof(Exception),
            typeof(ErrorMessageBox),
            new PropertyMetadata(null));

        public Exception? ExceptionObject
        {
            get => (Exception?)GetValue(ExceptionObjectProperty);
            set => SetValue(ExceptionObjectProperty, value);
        }

        public string? ExceptionMessage => ExceptionObject != null
            ? $"\n{ExceptionObject.GetType()}: {ExceptionObject.Message}"
            : null;

        public string ExceptionText => ExceptionObject?.ToString() ?? "null";

        public static readonly DependencyProperty IsRestartEnabledProperty = DependencyProperty.Register(
            nameof(IsRestartEnabled),
            typeof(bool),
            typeof(ErrorMessageBox),
            new PropertyMetadata(true));

        public bool IsRestartEnabled
        {
            get => (bool)GetValue(IsRestartEnabledProperty);
            set => SetValue(IsRestartEnabledProperty, value);
        }

        public ErrorMessageBox()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void OnDetailsButtonClick(object sender, RoutedEventArgs e)
        {
            DetailsVisibility = DetailsVisibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void OnViewLogButtonClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnQuitButtonClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (IsRestartEnabled)
            {
                App.Current.RestartOnClose = true;
            }
        }
    }
}
