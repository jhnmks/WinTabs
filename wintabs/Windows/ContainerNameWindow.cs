using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using wintabs.Utilities;

namespace wintabs.Windows
{
    internal sealed class ContainerNameWindow : Window
    {
        private readonly TextBox m_name;

        public string? ContainerName => string.IsNullOrWhiteSpace(m_name.Text) ? null : m_name.Text.Trim();

        public ContainerNameWindow(string? name, int number)
        {
            Title = "Rename group";
            Width = 360;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = true;

            m_name = new TextBox { Text = name ?? "", MaxLength = 80, Margin = new Thickness(0, 8, 0, 16) };
            System.Windows.Automation.AutomationProperties.SetName(m_name, "Group name");
            var save = new Button { Content = "Save", IsDefault = true, MinWidth = 72 };
            save.Click += (_, _) => DialogResult = true;
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = "Leave blank to use the selected app’s name." },
                    m_name,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { save, new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0) } },
                    },
                },
            };
            FocusManager.SetFocusedElement(this, m_name);
            ContentRendered += (_, _) =>
            {
                FocusHelper.ForceActivate(new WindowInteropHelper(this).Handle);
                Keyboard.Focus(m_name);
                m_name.SelectAll();
            };
        }
    }
}
